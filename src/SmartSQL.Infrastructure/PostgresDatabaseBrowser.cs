using System.Data;
using System.Net;
using System.Security.Authentication;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

/// <summary>Existing PostgreSQL database access with explicit, restricted opt-in cell editing.</summary>
public sealed partial class PostgresDatabaseBrowser : IDatabaseEditor, IDatabaseRowCreator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TransientCredential _credential;
    private int _disposed;
    private const int MaxCellCharacters = 1_000_000;
    private const int MaxPageCharacters = 16_000_000;

    private PostgresDatabaseBrowser(NpgsqlDataSource dataSource, TransientCredential credential, bool editingEnabled)
    {
        _dataSource = dataSource;
        _credential = credential;
        EditingEnabled = editingEnabled;
    }

    private sealed class TransientCredential(string password)
    {
        private string _password = password;
        internal string Read() => Volatile.Read(ref _password);
        internal void Clear() => Volatile.Write(ref _password, string.Empty);
    }

    public static Task<PostgresDatabaseBrowser> OpenAsync(ConnectionProfile profile, string transientPassword,
        CancellationToken cancellationToken = default) => OpenCoreAsync(profile, transientPassword, false, cancellationToken);

    public static Task<PostgresDatabaseBrowser> OpenForEditingAsync(ConnectionProfile profile, string transientPassword,
        CancellationToken cancellationToken = default) => OpenCoreAsync(profile, transientPassword, true, cancellationToken);

    private static async Task<PostgresDatabaseBrowser> OpenCoreAsync(ConnectionProfile profile, string transientPassword,
        bool editingEnabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transientPassword);
        profile.Validate();
        if (profile.Provider != DatabaseProviders.PostgreSql)
            throw new ArgumentException("Этот провайдер поддерживает только PostgreSQL.", nameof(profile));
        cancellationToken.ThrowIfCancellationRequested();

        NpgsqlDataSource? dataSource = null;
        var credential = new TransientCredential(transientPassword);
        try
        {
            var builder = new NpgsqlDataSourceBuilder(ConnectionSettings(profile).ConnectionString);
            // A non-null password callback also prevents implicit PGPASSWORD/pgpass credential lookup.
            builder.UsePasswordProvider(_ => credential.Read(), (_, _) => ValueTask.FromResult(credential.Read()));
            builder.UseNegotiateOptionsCallback(options => options.Credential = new NetworkCredential(profile.Username, credential.Read()));
            dataSource = builder.Build();
            var browser = new PostgresDatabaseBrowser(dataSource, credential, editingEnabled);
            await browser.ReadAsync(async (connection, transaction, token) =>
            {
                await using var command = Command("SELECT pg_catalog.current_setting('transaction_read_only')", connection, transaction);
                if ((string?)await command.ExecuteScalarAsync(token).ConfigureAwait(false) != "on")
                    throw new DatabaseBrowserException("read_only_unavailable", "Сервер не подтвердил режим чтения. Подключение остановлено.");
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch (Exception exception)
        {
            credential.Clear();
            if (dataSource is not null)
                await dataSource.DisposeAsync().ConfigureAwait(false);
            ThrowSanitized(exception, cancellationToken);
            throw;
        }
    }

    internal static NpgsqlConnectionStringBuilder ConnectionSettings(ConnectionProfile profile) => new()
    {
        Host = profile.Host,
        Port = profile.Port,
        Database = profile.Database,
        Username = profile.Username,
        SslMode = profile.SslPolicy switch
        {
            ConnectionSslPolicy.Disable => SslMode.Disable,
            ConnectionSslPolicy.Prefer => SslMode.Prefer,
            ConnectionSslPolicy.Require => SslMode.Require,
            ConnectionSslPolicy.VerifyCA => SslMode.VerifyCA,
            ConnectionSslPolicy.VerifyFull => SslMode.VerifyFull,
            _ => throw new ArgumentException("Неизвестный режим SSL.", nameof(profile))
        },
        GssEncryptionMode = GssEncryptionMode.Disable,
        Pooling = false,
        Enlist = false,
        PersistSecurityInfo = false,
        IncludeErrorDetail = false,
        LogParameters = false,
        Timeout = 15,
        CommandTimeout = 15,
        CancellationTimeout = 1000,
        ApplicationName = "SmartSQL read-only browser",
        Timezone = "UTC",
        ClientEncoding = "UTF8",
        Options = "-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=3000 -c idle_in_transaction_session_timeout=15000 -c search_path=pg_catalog"
    };

    public async Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InvalidateEditSnapshots();
            return await ReadAsync<IReadOnlyList<DatabaseTableRef>>(async (connection, transaction, token) =>
            {
                await using var command = Command(PostgresBrowserSql.Tables, connection, transaction);
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                var tables = new List<DatabaseTableRef>();
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (tables.Count == 10_000)
                        throw new DatabaseBrowserException("catalog_too_large", "В базе больше 10 000 доступных объектов. Каталог слишком большой для текущего обозревателя.");
                    tables.Add(new DatabaseTableRef(reader.GetString(0), reader.GetString(1), ReadKind(reader.GetString(2))));
                }
                return tables;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _editGate.Release(); }
    }

    public async Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table, CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InvalidateEditSnapshots();
            return await ReadAsync<IReadOnlyList<DatabaseColumn>>(async (connection, transaction, token) =>
            {
                var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                return await ReadColumnsAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _editGate.Release(); }
    }

    public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize = PostgresBrowserSql.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(table, offset, pageSize, new DatabaseBrowseQuery(), cancellationToken);

    public async Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize,
        DatabaseBrowseQuery query, CancellationToken cancellationToken = default)
    {
        ValidateTable(table);
        PostgresBrowserSql.ValidatePage(offset, pageSize);
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        await _editGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        InvalidateEditSnapshots();
        try
        {
            return await ReadAsync(async (connection, transaction, token) =>
            {
                var relation = await ReadRelationAsync(table, connection, transaction, token).ConfigureAwait(false);
                var columns = await ReadColumnsAsync(relation.Id, connection, transaction, token).ConfigureAwait(false);
                var editing = EditingEnabled
                    ? await ReadEditMetadataAsync(relation.Id, relation.Table, connection, transaction, token).ConfigureAwait(false)
                    : null;
                var captureHandles = editing?.Capabilities.CanUpdate == true;
                var snapshots = new Dictionary<Guid, EditSnapshot>();
                var rows = new List<DatabaseDisplayRow>(pageSize);
                await using var command = Command(PostgresBrowserSql.Page(relation.Table, columns, query, captureHandles), connection, transaction);
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = pageSize + 1 });
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = offset });
                if (!string.IsNullOrEmpty(query.SearchText))
                    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = query.SearchText });
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
                var hasMore = false;
                var pageCharacters = 0;
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (rows.Count == pageSize)
                    {
                        hasMore = true;
                        break;
                    }
                    var values = new string?[columns.Count];
                    for (var index = 0; index < values.Length; index++)
                    {
                        if (await reader.IsDBNullAsync(index, token).ConfigureAwait(false))
                            continue;
                        using var text = await reader.GetTextReaderAsync(index, token).ConfigureAwait(false);
                        try
                        {
                            values[index] = await ReadTextAsync(text, MaxCellCharacters, token).ConfigureAwait(false);
                            pageCharacters += values[index]!.Length;
                            if (pageCharacters > MaxPageCharacters)
                                throw new DatabaseBrowserException("page_too_large", "Страница превышает 16 миллионов символов. Уменьшите число строк; данные не были обрезаны.");
                        }
                        catch (DatabaseBrowserException)
                        {
                            command.Cancel();
                            throw;
                        }
                    }
                    Guid? handle = null;
                    if (captureHandles)
                    {
                        handle = Guid.NewGuid();
                        snapshots.Add(handle.Value, new EditSnapshot(relation.Table, editing!, values.ToArray(), reader.GetString(columns.Count), DateTimeOffset.UtcNow));
                    }
                rows.Add(new DatabaseDisplayRow(values) { EditHandle = handle });
                }
                lock (_snapshotSync)
                    foreach (var item in snapshots)
                        _editSnapshots.Add(item.Key, item.Value);
                return new DatabasePage(relation.Table, columns, rows, offset, pageSize, hasMore,
                    PostgresBrowserSql.Diagnostics(relation.Table, columns, relation.RowLevelSecurity, relation.HasSubclasses, query));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            InvalidateEditSnapshots();
            throw;
        }
        finally { _editGate.Release(); }
    }

    internal static async Task<string> ReadTextAsync(TextReader reader, int maxCharacters, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxCharacters - text.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (text.Length + read > maxCharacters)
                throw new DatabaseBrowserException("cell_too_large", "Значение превышает 1 миллион символов и не может быть показано на текущем этапе. Данные не были обрезаны.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    private async Task<T> ReadAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            await using (var command = Command(PostgresBrowserSql.ConfigureReadOnlyTransaction, connection, transaction))
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var result = await read(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            ThrowSanitized(exception, cancellationToken);
            throw;
        }
    }

    private sealed record Relation(uint Id, DatabaseTableRef Table, bool RowLevelSecurity, bool HasSubclasses);

    private static async Task<Relation> ReadRelationAsync(DatabaseTableRef table, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command(PostgresBrowserSql.Relation, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = table.Schema });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = table.Name });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new DatabaseBrowserException("object_unavailable", "Объект не найден или нет прав на чтение. Обновите список таблиц.");
        var actual = new DatabaseTableRef(reader.GetString(1), reader.GetString(2), ReadKind(reader.GetString(3)));
        if (actual.Kind != table.Kind)
            throw new DatabaseBrowserException("object_changed", "Тип объекта изменился на сервере. Обновите список таблиц.");
        return new Relation(reader.GetFieldValue<uint>(0), actual, reader.GetBoolean(4), reader.GetBoolean(5));
    }

    private static async Task<IReadOnlyList<DatabaseColumn>> ReadColumnsAsync(uint relationId, NpgsqlConnection connection,
        NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command(PostgresBrowserSql.Columns, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Oid, Value = relationId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new List<DatabaseColumn>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            columns.Add(new DatabaseColumn(reader.GetString(0), columns.Count, reader.GetString(1), reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        return columns;
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        new(sql, connection, transaction) { CommandTimeout = 15 };

    private static DatabaseObjectKind ReadKind(string kind) => kind switch
    {
        "r" => DatabaseObjectKind.Table,
        "v" => DatabaseObjectKind.View,
        "m" => DatabaseObjectKind.MaterializedView,
        "f" => DatabaseObjectKind.ForeignTable,
        "p" => DatabaseObjectKind.PartitionedTable,
        _ => throw new DatabaseBrowserException("unsupported_object", "Этот тип объекта PostgreSQL пока не поддерживается.")
    };

    private static void ValidateTable(DatabaseTableRef table)
    {
        ArgumentNullException.ThrowIfNull(table);
        PostgresBrowserSql.QuoteIdentifier(table.Schema);
        PostgresBrowserSql.QuoteIdentifier(table.Name);
        if (!Enum.IsDefined(table.Kind))
            throw new ArgumentException("Неизвестный тип объекта базы.", nameof(table));
    }

    internal static DatabaseBrowserException Sanitize(Exception exception)
    {
        if (exception is PostgresException postgres)
            return postgres.SqlState switch
            {
                "28P01" or "28000" => new("authentication_failed", "Сервер отклонил подключение. Проверьте имя пользователя, пароль и правила доступа PostgreSQL."),
                "3D000" => new("database_unavailable", "Указанная база данных не найдена на сервере."),
                "42501" => new("permission_denied", "Недостаточно прав для чтения выбранного объекта."),
                "42P01" or "42703" or "42809" => new("object_changed", "Структура объекта изменилась на сервере. Обновите список таблиц."),
                "57014" or "55P03" => new("operation_timed_out", "Чтение отменено сервером или превысило время ожидания. Попробуйте меньшую страницу."),
                _ => new("server_error", "PostgreSQL не смог выполнить операцию чтения. Проверьте доступность объекта и права пользователя.")
            };
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
                return new("tls_failed", "Не удалось проверить защищённое подключение. Проверьте имя сервера, сертификат и выбранный режим SSL.");
            if (current is TimeoutException)
                return new("operation_timed_out", "Подключение или чтение превысило предел 15 секунд.");
        }
        return new("connection_failed", "Не удалось подключиться или прочитать данные. Проверьте сервер, порт, имя базы и режим SSL.");
    }

    private static void ThrowSanitized(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DatabaseBrowserException or ObjectDisposedException)
            return;
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        throw Sanitize(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // An admitted edit finishes before its data source is disposed. Queued edits
            // recheck disposal under the gate and cannot start a write transaction.
            await _editGate.WaitAsync().ConfigureAwait(false);
            try
            {
                InvalidateEditSnapshots();
                InvalidateInsertConfirmation();
                _credential.Clear();
                await _dataSource.DisposeAsync().ConfigureAwait(false);
            }
            finally { _editGate.Release(); }
        }
    }
}
