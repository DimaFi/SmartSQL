using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using SQLitePCL;

namespace SmartSQL.Infrastructure;

/// <summary>Independent native SQL parsing and authorization; never inherits table editing permission.</summary>
public sealed class NativeSqliteReadOnlySqlExecutor : ReadOnlySqlExecutorBase
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _path;
    static NativeSqliteReadOnlySqlExecutor() => Batteries_V2.Init();
    private NativeSqliteReadOnlySqlExecutor(string path) => _path = path;

    public static async Task<NativeSqliteReadOnlySqlExecutor> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Укажите абсолютный путь SQLite-файла.", nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new DatabaseBrowserException("database_unavailable", "SQLite-файл отсутствует. Новый файл не создаётся.");
        var executor = new NativeSqliteReadOnlySqlExecutor(Path.GetFullPath(path));
        try { await executor.ExecuteAsync(new("SELECT 1", 1), cancellationToken).ConfigureAwait(false); return executor; }
        catch { await executor.DisposeAsync().ConfigureAwait(false); throw; }
    }

    protected override Task<SqlReadResult> ExecuteCoreAsync(SqlReadRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Execute(request, cancellationToken), cancellationToken);

    private SqlReadResult Execute(SqlReadRequest request, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1
        }.ToString());
        connection.Open();
        connection.EnableExtensions(false);
        var db = connection.Handle!;
        using var interrupt = cancellationToken.Register(() => raw.sqlite3_interrupt(db));
        using (var settings = connection.CreateCommand())
        {
            settings.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY";
            settings.ExecuteNonQuery();
        }
        if (raw.sqlite3_db_config(db, raw.SQLITE_DBCONFIG_DEFENSIVE, 1, out var defensive) != raw.SQLITE_OK || defensive != 1)
            throw new DatabaseBrowserException("query_policy_unavailable", "SQLite не подтвердил защитный режим; SQL не выполнен.");
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var encoding = new SqliteCommand("PRAGMA main.encoding", connection, transaction))
            if ((string?)encoding.ExecuteScalar() != "UTF-8")
                throw new DatabaseBrowserException("query_encoding_unsupported", "SQL-вкладки первого среза поддерживают UTF-8 SQLite. Для UTF-16 используйте просмотр таблиц; файл не изменён.");
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "sqlite_schema", "sqlite_master" };
        using (var tables = new SqliteCommand("SELECT hex(CAST(name AS BLOB)) FROM pragma_table_list WHERE schema='main' AND type IN ('table','view')", connection, transaction))
        using (var reader = tables.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (allowed.Count > 10_002) throw new DatabaseBrowserException("catalog_too_large", "SQL-проверка ограничена 10 000 обычными объектами.");
                allowed.Add(StrictUtf8.GetString(Convert.FromHexString(reader.GetString(0))));
            }
        }
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_LENGTH, SqlReadLimits.MaxCellCharacters * 4);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_SQL_LENGTH, SqlReadLimits.MaxSqlBytes);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_COLUMN, SqlReadLimits.MaxColumns);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_EXPR_DEPTH, 100);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_COMPOUND_SELECT, 100);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_VDBE_OP, 250_000);
        raw.sqlite3_limit(db, raw.SQLITE_LIMIT_WORKER_THREADS, 0);
        var policy = new Policy(allowed);
        Check(raw.sqlite3_set_authorizer(db, (delegate_authorizer)Authorize, policy), cancellationToken);
        raw.sqlite3_progress_handler(db, 1000, _ => cancellationToken.IsCancellationRequested ? 1 : 0, null);
        try
        {
            using var statement = PrepareOne(db, request.Sql, cancellationToken);
            var count = raw.sqlite3_column_count(statement);
            if (raw.sqlite3_stmt_readonly(statement) != 1 || count is < 1 or > SqlReadLimits.MaxColumns || raw.sqlite3_bind_parameter_count(statement) != 0)
                throw new DatabaseBrowserException("query_unsupported", "Поддерживается одна команда чтения с 1–600 столбцами без bind-параметров.");
            var columns = new List<SqlReadColumn>();
            for (var index = 0; index < count; index++)
                columns.Add(new(index, DecodeZero(raw.sqlite3_column_name(statement, index)) ?? string.Empty,
                    DecodeZero(raw.sqlite3_column_decltype(statement, index))));
            var rows = new List<SqlReadRow>();
            var total = 0;
            var truncated = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = raw.sqlite3_step(statement);
                if (status == raw.SQLITE_DONE) break;
                Check(status, cancellationToken, allowRow: true);
                if (rows.Count == request.RowLimit) { truncated = true; break; }
                var cells = new SqlReadCell[count];
                for (var index = 0; index < count; index++)
                {
                    var type = raw.sqlite3_column_type(statement, index);
                    string? value;
                    string storage;
                    switch (type)
                    {
                        case raw.SQLITE_NULL: value = null; storage = "null"; break;
                        case raw.SQLITE_INTEGER: value = raw.sqlite3_column_int64(statement, index).ToString(CultureInfo.InvariantCulture); storage = "integer"; break;
                        case raw.SQLITE_FLOAT: value = raw.sqlite3_column_double(statement, index).ToString("R", CultureInfo.InvariantCulture); storage = "real"; break;
                        case raw.SQLITE_BLOB:
                            var blob = raw.sqlite3_column_blob(statement, index);
                            if (blob.Length > (SqlReadLimits.MaxCellCharacters - 2) / 2) throw CellTooLarge();
                            value = "0x" + Convert.ToHexString(blob); storage = "blob"; break;
                        case raw.SQLITE_TEXT:
                            // Force UTF-8 first, then use the explicit byte length: embedded NUL is data.
                            var text = raw.sqlite3_column_text(statement, index);
                            var length = raw.sqlite3_column_bytes(statement, index);
                            if (length > SqlReadLimits.MaxCellCharacters * 4L) throw CellTooLarge();
                            value = StrictUtf8.GetString(Bytes(text, length)); storage = "text"; break;
                        default: throw new DatabaseBrowserException("query_value_unsupported", "SQLite вернул неподдерживаемый класс хранения.");
                    }
                    if (value?.Length > SqlReadLimits.MaxCellCharacters) throw CellTooLarge();
                    total += value?.Length ?? 0;
                    if (total > SqlReadLimits.MaxResultCharacters)
                        throw new DatabaseBrowserException("query_result_too_large", "Результат превышает 16 млн символов. Уменьшите лимит; данные не обрезаны.");
                    cells[index] = new(value, storage);
                }
                rows.Add(new(cells));
            }
            return new(columns, rows, truncated,
            [
                new("sql_read_only", "Отдельное ReadOnly-соединение, native authorizer и одна команда без параметров; результат не предоставляет права редактирования."),
                new("sqlite_sql_types", "INTEGER показан точно, REAL — обратимым double, BLOB — hex. NULL, пустота и NUL различаются; для выражений объявленный тип может отсутствовать."),
                new("sqlite_sql_policy", "Разрешены подтверждённые обычные main-таблицы/представления и ограниченный список встроенных функций. PRAGMA, virtual/shadow, подключение файлов, ручные транзакции и неизвестные функции запрещены. SQLite не всегда сообщает схему при чтении без столбцов: COUNT(*) по неподтверждённому имени CTE и подобные выражения пока отклоняются; выбирайте его явный столбец."),
                new("sqlite_sql_limits", "Лимиты строк, значений и 15 секунд ограничивают выполнение, но не являются общей квотой памяти или ОС-песочницей. При WAL возможны служебные SHM/WAL; миграция и checkpoint не выполняются.")
            ]);
        }
        finally
        {
            raw.sqlite3_progress_handler(db, 0, null, null);
            raw.sqlite3_set_authorizer(db, (delegate_authorizer)null!, null);
            transaction.Rollback();
        }
    }

    private static sqlite3_stmt PrepareOne(sqlite3 db, string sql, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> tail = StrictUtf8.GetBytes(sql);
        sqlite3_stmt? selected = null;
        try
        {
            while (!tail.IsEmpty)
            {
                var previousLength = tail.Length;
                var status = raw.sqlite3_prepare_v3(db, tail, 4 /* SQLITE_PREPARE_NO_VTAB */, out var next, out tail);
                if (status != raw.SQLITE_OK) { next?.Dispose(); Check(status, cancellationToken); }
                if (tail.Length >= previousLength) { next?.Dispose(); throw new DatabaseBrowserException("query_unsupported", "SQLite не смог разобрать SQL полностью."); }
                if (next is null || next.IsInvalid) { next?.Dispose(); continue; }
                if (selected is not null)
                {
                    next.Dispose();
                    throw new DatabaseBrowserException("query_multiple_statements", "Выполняется ровно одна SQL-команда. Разделите запросы по вкладкам.");
                }
                selected = next;
            }
            return selected ?? throw new DatabaseBrowserException("query_empty", "SQL не содержит команды чтения.");
        }
        catch { selected?.Dispose(); throw; }
    }

    private sealed record Policy(HashSet<string> Tables);
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "abs", "avg", "count", "sum", "total", "min", "max", "length", "octet_length", "substr", "substring",
        "trim", "ltrim", "rtrim", "lower", "upper", "coalesce", "ifnull", "nullif", "typeof", "hex", "quote", "unicode", "char",
        "instr", "replace", "round", "date", "time", "datetime", "julianday", "strftime", "unixepoch", "timediff",
        "printf", "format", "concat", "concat_ws", "iif", "like", "glob", "likelihood", "likely", "unlikely",
        "row_number", "rank", "dense_rank", "percent_rank", "cume_dist", "ntile", "lag", "lead", "first_value", "last_value", "nth_value",
        "group_concat", "string_agg", "sqlite_version"
    };
    private static int Authorize(object state, int action, utf8z first, utf8z second, utf8z database, utf8z source)
    {
        try
        {
            return action switch
            {
                raw.SQLITE_SELECT or raw.SQLITE_RECURSIVE => raw.SQLITE_OK,
                raw.SQLITE_READ when DecodeZero(database) == "main" && ((Policy)state).Tables.Contains(DecodeZero(first) ?? string.Empty) => raw.SQLITE_OK,
                // SQLite's columnless READ callback omits the schema. Still require the exact
                // name of a confirmed ordinary main object; unknown/CTE names remain denied.
                raw.SQLITE_READ when DecodeZero(database) is null && DecodeZero(second) == ""
                    && ((Policy)state).Tables.Contains(DecodeZero(first) ?? string.Empty) => raw.SQLITE_OK,
                raw.SQLITE_FUNCTION when Functions.Contains(DecodeZero(second) ?? string.Empty) => raw.SQLITE_OK,
                _ => raw.SQLITE_DENY
            };
        }
        catch { return raw.SQLITE_DENY; } // Exceptions must never cross the native callback boundary.
    }

    private static ReadOnlySpan<byte> Bytes(utf8z value, int length) =>
        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value.GetPinnableReference()), length);
    private static string? DecodeZero(utf8z value)
    {
        ref readonly var first = ref value.GetPinnableReference();
        if (Unsafe.IsNullRef(ref Unsafe.AsRef(in first))) return null;
        var length = 0;
        while (Unsafe.Add(ref Unsafe.AsRef(in first), length) != 0)
            if (++length > SqlReadLimits.MaxSqlBytes) throw new DatabaseBrowserException("query_metadata_too_large", "Имя или тип столбца превышает допустимый объём.");
        return StrictUtf8.GetString(Bytes(value, length));
    }
    private static DatabaseBrowserException CellTooLarge() => new("cell_too_large", "Значение превышает 1 млн символов. Данные не обрезаны.");
    private static void Check(int status, CancellationToken cancellationToken, bool allowRow = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (status == raw.SQLITE_OK || allowRow && status == raw.SQLITE_ROW) return;
        throw new DatabaseBrowserException(status == raw.SQLITE_AUTH ? "query_policy_denied" : "query_unsupported",
            "SQLite отклонил запрос. Поддерживается одна команда чтения обычных объектов main, без параметров и запрещённых функций.");
    }
    protected override DatabaseBrowserException Sanitize(Exception exception) => exception is DecoderFallbackException
        ? new("query_text_invalid", "Результат или метаданные содержат некорректный UTF-8. Замена символов не выполняется.")
        : base.Sanitize(exception);
}
