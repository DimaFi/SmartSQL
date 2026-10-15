using System.Data;
using System.Globalization;
using System.Net;
using Npgsql;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

/// <summary>Independent, bounded cursor queries. SQL is parsed by PostgreSQL in a read-only transaction.</summary>
public sealed class PostgresReadOnlySqlExecutor : ReadOnlySqlExecutorBase
{
    private readonly ConnectionProfile _profile;
    private string _password;

    private PostgresReadOnlySqlExecutor(ConnectionProfile profile, string password)
    {
        _profile = profile;
        _password = password;
    }

    public static async Task<PostgresReadOnlySqlExecutor> OpenAsync(ConnectionProfile profile, string transientPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transientPassword);
        profile.Validate();
        if (profile.Provider != DatabaseProviders.PostgreSql)
            throw new ArgumentException("Этот SQL-провайдер поддерживает только PostgreSQL.", nameof(profile));
        cancellationToken.ThrowIfCancellationRequested();
        var executor = new PostgresReadOnlySqlExecutor(profile, transientPassword);
        try
        {
            await executor.ExecuteAsync(new("SELECT pg_catalog.current_setting('transaction_read_only')", 1), cancellationToken).ConfigureAwait(false);
            return executor;
        }
        catch { await executor.DisposeAsync().ConfigureAwait(false); throw; }
    }

    protected override async Task<SqlReadResult> ExecuteCoreAsync(SqlReadRequest request, CancellationToken cancellationToken)
    {
        var settings = PostgresDatabaseBrowser.ConnectionSettings(_profile);
        settings.ApplicationName = "SmartSQL read-only SQL";
        settings.Options = "-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=3000 -c idle_in_transaction_session_timeout=15000 -c search_path=pg_catalog,public";
        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.UsePasswordProvider(_ => _password, (_, _) => ValueTask.FromResult(_password));
        builder.UseNegotiateOptionsCallback(options => options.Credential = new NetworkCredential(_profile.Username, _password));
        await using var dataSource = builder.Build();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await using (var configure = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await configure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using (var verify = new NpgsqlCommand("SELECT pg_catalog.current_setting('transaction_read_only')", connection, transaction))
            if ((string?)await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != "on")
                throw new DatabaseBrowserException("read_only_unavailable", "PostgreSQL не подтвердил режим чтения; SQL не выполнен.");

        // A single batch command with no parameters is sent as one native Parse message.
        // SchemaOnly parses/describes without Bind/Execute, including before any cursor is declared.
        await using (var preflight = SingleStatement(request.Sql, connection, transaction))
        await using (var description = await preflight.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false))
        {
            if (description.FieldCount is < 1 or > SqlReadLimits.MaxColumns)
                throw new DatabaseBrowserException("query_columns_unsupported", "SQL должен возвращать от 1 до 600 столбцов. Поддерживаются cursor-запросы SELECT, VALUES, TABLE и WITH.");
        }
        var cursor = "smartsql_" + Guid.NewGuid().ToString("N");
        await using (var declare = SingleStatement($"DECLARE \"{cursor}\" NO SCROLL CURSOR WITHOUT HOLD FOR {request.Sql}", connection, transaction))
            await declare.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using var fetch = new NpgsqlCommand($"FETCH FORWARD {(request.RowLimit + 1).ToString(CultureInfo.InvariantCulture)} FROM \"{cursor}\"", connection, transaction)
        {
            AllResultTypesAreUnknown = true, CommandTimeout = 15
        };
        var columns = new List<SqlReadColumn>();
        var rows = new List<SqlReadRow>();
        var truncated = false;
        var characters = 0;
        await using (var reader = await fetch.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false))
        {
            for (var index = 0; index < reader.FieldCount; index++)
                columns.Add(new(index, reader.GetName(index), reader.GetDataTypeName(index)));
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rows.Count == request.RowLimit) { truncated = true; break; }
                var cells = new SqlReadCell[columns.Count];
                for (var index = 0; index < cells.Length; index++)
                {
                    string? value = null;
                    if (!await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false))
                    {
                        using var text = reader.GetTextReader(index);
                        value = await PostgresDatabaseBrowser.ReadTextAsync(text, SqlReadLimits.MaxCellCharacters, cancellationToken).ConfigureAwait(false);
                    }
                    characters += value?.Length ?? 0;
                    if (characters > SqlReadLimits.MaxResultCharacters)
                        throw new DatabaseBrowserException("query_result_too_large", "Результат превышает 16 млн символов. Уменьшите лимит строк; данные не обрезаны.");
                    cells[index] = new(value);
                }
                rows.Add(new(cells));
            }
        }
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return new(columns, rows, truncated,
        [
            new("sql_read_only", "Отдельный SQL-сеанс: одна команда с результатом, read-only транзакция, обязательный откат, лимит строк и времени. Изменение таблиц из результата недоступно."),
            new("sql_native_text", "Значения представлены точным текстом PostgreSQL: NULL отличается от пустоты, numeric не преобразуется в .NET decimal. Даты и timezone следуют настройкам сеанса UTC."),
            new("sql_function_limits", "Режим чтения не является ОС-песочницей: пользовательские функции и расширения могут иметь внешние побочные эффекты. SHOW/EXPLAIN/CALL и ручные транзакции в этом срезе не поддерживаются.")
        ]);
    }

    private static NpgsqlBatch SingleStatement(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var batch = new NpgsqlBatch(connection, transaction) { Timeout = 15 };
        batch.BatchCommands.Add(new NpgsqlBatchCommand(sql));
        return batch;
    }

    protected override DatabaseBrowserException Sanitize(Exception exception)
    {
        if (exception is PostgresException server)
            return server.SqlState switch
            {
                "57014" or "55P03" => new("query_timed_out", "PostgreSQL отменил SQL-запрос или превысил время ожидания."),
                "25006" => new("query_write_denied", "Изменение данных запрещено в SQL-сеансе только для чтения."),
                "42501" => new("query_permission_denied", "Недостаточно прав для выполнения SQL-запроса."),
                "42601" or "42P02" or "0A000" => new("query_unsupported", "Поддерживается одна SQL-команда SELECT, VALUES, TABLE или WITH без параметров. Проверьте синтаксис и ограничения cursor-запроса."),
                _ => new("query_server_error", "PostgreSQL отклонил SQL-запрос. Проверьте синтаксис, имена объектов, типы и права; подробности драйвера скрыты.")
            };
        return PostgresDatabaseBrowser.Sanitize(exception);
    }

    protected override ValueTask ReleaseResourcesAsync()
    {
        _password = string.Empty;
        return ValueTask.CompletedTask;
    }
}
