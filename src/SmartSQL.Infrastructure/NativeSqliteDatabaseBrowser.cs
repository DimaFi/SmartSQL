using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

/// <summary>Ordinary existing SQLite files, with no SmartSQL metadata or migrations.</summary>
public sealed partial class NativeSqliteDatabaseBrowser : IDatabaseEditor
{
    private const int MaxCell = 1_000_000;
    private const int MaxPage = 16_000_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;
    static NativeSqliteDatabaseBrowser() => SQLitePCL.Batteries_V2.Init();
    private NativeSqliteDatabaseBrowser(string path) => _path = path;

    public static async Task<NativeSqliteDatabaseBrowser> OpenAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath)) throw new ArgumentException("Укажите абсолютный путь SQLite-файла.", nameof(absolutePath));
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(absolutePath);
        if (!File.Exists(path)) throw new DatabaseBrowserException("database_unavailable", "SQLite-файл не найден. Новый файл не создаётся.");
        var browser = new NativeSqliteDatabaseBrowser(path);
        try
        {
            await browser.ReadAsync(context =>
            {
                using var command = context.Command("SELECT count(*) FROM main.sqlite_schema");
                command.ExecuteScalar();
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { await browser.DisposeAsync().ConfigureAwait(false); throw; }
        return browser;
    }

    public Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<DatabaseTableRef>>(context =>
    {
        using var command = context.Command("SELECT name,type FROM main.sqlite_schema WHERE type IN ('table','view') AND substr(name,1,7)<>'sqlite_' ORDER BY name COLLATE BINARY");
        using var reader = command.ExecuteReader();
        var tables = new List<DatabaseTableRef>();
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            if (tables.Count == 10_000) throw new DatabaseBrowserException("catalog_too_large", "Каталог превышает 10 000 объектов.");
            tables.Add(new("main", reader.GetString(0), reader.GetString(1) == "view" ? DatabaseObjectKind.View : DatabaseObjectKind.Table));
        }
        return tables;
    }, cancellationToken);

    public Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table, CancellationToken cancellationToken = default)
    {
        Validate(table);
        return ReadAsync<IReadOnlyList<DatabaseColumn>>(context => Columns(context, Relation(context, table)), cancellationToken);
    }

    public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize,
        CancellationToken cancellationToken = default) => ReadPageAsync(table, offset, pageSize, new(), cancellationToken);

    public Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize, DatabaseBrowseQuery query,
        CancellationToken cancellationToken = default)
    {
        Validate(table);
        DatabaseBrowseLimits.ValidatePageRequest(offset, pageSize);
        ArgumentNullException.ThrowIfNull(query);
        query = query.Snapshot();
        return ReadAsync(context =>
        {
            var relation = Relation(context, table);
            var columns = Columns(context, relation);
            if (columns.Count == 0) throw new DatabaseBrowserException("unsupported_object", "Нет доступных столбцов объекта.");
            if (columns.Count > 600) throw new DatabaseBrowserException("object_too_wide", "В объекте больше 600 столбцов. Страница с проверкой нативных значений превышает ограничения текущего обозревателя; структура доступна отдельно.");
            var filterValues = PrepareFilters(context, table, columns, query);
            var editing = EditingEnabled ? ReadEditMetadata(context, relation, columns) : null;
            using var command = context.Command(NativeSqliteBrowserSql.Page(table, columns, query));
            command.Parameters.AddWithValue("$limit", pageSize + 1);
            command.Parameters.AddWithValue("$offset", offset);
            if (!string.IsNullOrEmpty(query.SearchText)) command.Parameters.AddWithValue("$search", query.SearchText);
            foreach (var parameter in filterValues) command.Parameters.AddWithValue(parameter.Key, parameter.Value);
            using var reader = command.ExecuteReader();
            var rows = new List<DatabaseDisplayRow>();
            var characters = 0;
            var more = false;
            while (reader.Read())
            {
                context.Token.ThrowIfCancellationRequested();
                if (rows.Count == pageSize) { more = true; break; }
                var values = new string?[columns.Count];
                for (var i = 0; i < columns.Count; i++)
                {
                    var type = reader.GetString(i * 3);
                    var bytes = reader.IsDBNull(i * 3 + 1) ? 0 : reader.GetInt64(i * 3 + 1);
                    if (type == "blob" && bytes > (MaxCell - 2) / 2 || type == "text" && bytes > MaxCell * 4L)
                        throw new DatabaseBrowserException("cell_too_large", "Значение превышает допустимый объём. Данные не обрезаны.");
                    values[i] = type switch
                    {
                        "null" => null,
                        "integer" => reader.GetInt64(i * 3 + 2).ToString(CultureInfo.InvariantCulture),
                        "real" => reader.GetDouble(i * 3 + 2).ToString("R", CultureInfo.InvariantCulture),
                        "blob" => "0x" + Convert.ToHexString((byte[])reader.GetValue(i * 3 + 2)),
                        "text" => Decode((byte[])reader.GetValue(i * 3 + 2), context.TextEncoding),
                        _ => throw new DatabaseBrowserException("unsupported_value", "Неизвестный класс хранения SQLite.")
                    };
                    if (values[i]?.Length > MaxCell) throw new DatabaseBrowserException("cell_too_large", "Значение превышает 1 млн символов. Данные не обрезаны.");
                    characters += values[i]?.Length ?? 0;
                    if (characters > MaxPage) throw new DatabaseBrowserException("page_too_large", "Страница превышает 16 млн символов. Уменьшите размер страницы.");
                }
                Guid? editHandle = null;
                if (editing?.Capabilities.CanUpdate == true)
                {
                    editHandle = Guid.NewGuid();
                    _editSnapshots.Add(editHandle.Value, new(table, editing, ReadRawRow(reader, columns.Count), DateTimeOffset.UtcNow));
                }
                rows.Add(new(values) { EditHandle = editHandle });
            }
            var diagnostics = new List<DatabaseDiagnostic>
            {
                new("sqlite_storage_classes", "SQLite допускает разные классы хранения в одном столбце. Объявленный тип не гарантирует тип значения; целые числа точны, REAL показан как double с обратимым представлением, BLOB — 0x и hex."),
                new("live_pagination", "Каждая страница — отдельный снимок; изменения файла могут сдвигать строки."),
                new("sqlite_readonly_wal", "Страницы читаются без изменения данных, миграции и checkpoint. При WAL SQLite может использовать или создавать служебные -shm/-wal рядом с исходным файлом; это не резервная копия и не режим immutable. Разрешение записи проверяется отдельно.")
            };
            if (columns.All(c => c.PrimaryKeyOrder is null) || columns.Any(c => c.PrimaryKeyOrder is not null && c.IsNullable))
                diagnostics.Add(new("unstable_pagination", "Нет гарантированного уникального непустого ключа. Повторяющиеся значения не обеспечивают стабильный порядок страниц."));
            if (query.SortColumn is not null) diagnostics.Add(new("selected_native_sort", "Нативная сортировка SQLite: NULL сначала; INTEGER/REAL сравниваются как числа, затем TEXT по BINARY, затем BLOB по байтам. Смешанные классы хранения не преобразуются в один тип."));
            if (query.EffectiveFilters.Count > 0) diagnostics.Add(new("sqlite_typed_filters", "Условия объединены через И до пагинации. Для типизированных сравнений проверены все непустые значения затронутых столбцов в том же снимке; объявленный тип сам по себе не гарантирует совместимость."));
            if (!string.IsNullOrEmpty(query.SearchText)) diagnostics.Add(new("native_text_search", "Буквальный поиск по представлению SQLite; lower меняет регистр ASCII. REAL может иметь другое представление, чем отображаемый double."));
            if (editing is not null) diagnostics.AddRange(editing.Capabilities.Diagnostics);
            return new DatabasePage(table, columns, rows, offset, pageSize, more, diagnostics);
        }, cancellationToken);
    }

    private sealed record NativeRelation(DatabaseTableRef Table, bool WithoutRowId, bool Strict);
    private static NativeRelation Relation(Context context, DatabaseTableRef table)
    {
        using var command = context.Command("SELECT s.type, t.type, t.wr, t.strict FROM main.sqlite_schema s JOIN pragma_table_list t ON t.schema='main' AND t.name=s.name WHERE s.name=$name AND s.type IN ('table','view')");
        command.Parameters.AddWithValue("$name", table.Name);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new DatabaseBrowserException("object_unavailable", "Объект отсутствует. Обновите каталог.");
        var kind = reader.GetString(0) == "view" ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
        if (table.Kind != kind) throw new DatabaseBrowserException("object_changed", "Вид объекта изменился. Обновите каталог.");
        if (reader.GetString(1) is "virtual" or "shadow") throw new DatabaseBrowserException("unsupported_object", "Виртуальные таблицы и их служебные объекты пока не поддерживаются.");
        return new(table, reader.GetInt64(2) != 0, reader.GetInt64(3) != 0);
    }

    private static IReadOnlyList<DatabaseColumn> Columns(Context context, NativeRelation relation)
    {
        using var command = context.Command("SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo($name,'main') ORDER BY cid");
        command.Parameters.AddWithValue("$name", relation.Table.Name);
        using var reader = command.ExecuteReader();
        var columns = new List<DatabaseColumn>();
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            if (reader.GetInt64(4) == 1) continue;
            var key = reader.GetInt32(3);
            columns.Add(new(reader.GetString(0), columns.Count, reader.GetString(1), reader.GetInt64(2) == 0 && !(key > 0 && (relation.WithoutRowId || relation.Strict)), key == 0 ? null : key));
        }
        // INTEGER PRIMARY KEY is an alias for rowid only when SQLite did not build a separate PK index.
        var keys = columns.Where(c => c.PrimaryKeyOrder is not null).ToArray();
        if (keys.Length == 1 && keys[0].NativeType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
        {
            using var index = context.Command("SELECT count(*) FROM pragma_index_list($name,'main') WHERE origin='pk'");
            index.Parameters.AddWithValue("$name", relation.Table.Name);
            if ((long)index.ExecuteScalar()! == 0) columns[keys[0].Ordinal] = keys[0] with { IsNullable = false };
        }
        return columns;
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        try { return encoding.GetString(bytes); }
        catch (DecoderFallbackException) { throw new DatabaseBrowserException("unsupported_text_encoding", "TEXT содержит некорректную кодировку SQLite. Значение не было заменено или обрезано; используйте специализированный инструмент."); }
    }

    private sealed record Context(SqliteConnection Connection, SqliteTransaction Transaction, CancellationToken Token, Encoding TextEncoding)
    {
        internal SqliteCommand Command(string sql) => new(sql, Connection, Transaction) { CommandTimeout = 3 };
    }

    private async Task<T> ReadAsync<T>(Func<Context, T> operation, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var completed = false;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            InvalidateEditSnapshots();
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3 }.ToString());
                try
                {
                    connection.Open();
                    connection.EnableExtensions(false);
                    using var registration = timeout.Token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
                    using (var settings = connection.CreateCommand())
                    {
                        settings.CommandText = "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY";
                        settings.ExecuteNonQuery();
                    }
                    using var transaction = connection.BeginTransaction(deferred: true);
                    using var encodingCommand = new SqliteCommand("PRAGMA main.encoding", connection, transaction);
                    var encoding = (string?)encodingCommand.ExecuteScalar() switch
                    {
                        "UTF-8" => StrictUtf8,
                        "UTF-16le" => (Encoding)new UnicodeEncoding(false, false, true),
                        "UTF-16be" => new UnicodeEncoding(true, false, true),
                        _ => throw new DatabaseBrowserException("unsupported_text_encoding", "Неизвестная кодировка SQLite-файла.")
                    };
                    var result = operation(new(connection, transaction, timeout.Token, encoding));
                    timeout.Token.ThrowIfCancellationRequested();
                    transaction.Rollback();
                    return result;
                }
                catch (Exception exception) when (exception is SqliteException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
                    if (timeout.IsCancellationRequested) throw new DatabaseBrowserException("operation_timed_out", "Чтение SQLite превысило 15 секунд.");
                    throw new DatabaseBrowserException("database_unavailable", "SQLite не смог прочитать файл или объект. Проверьте формат, доступ, блокировки и структуру.");
                }
            }, token).ConfigureAwait(false);
            completed = true;
            return result;
        }
        finally
        {
            if (!completed) InvalidateEditSnapshots();
            _gate.Release();
        }
    }

    private static void Validate(DatabaseTableRef table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Schema != "main" || table.Kind is not (DatabaseObjectKind.Table or DatabaseObjectKind.View))
            throw new ArgumentException("Поддерживаются таблицы и представления main выбранного SQLite-файла.", nameof(table));
        NativeSqliteBrowserSql.Quote(table.Name);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        InvalidateEditSnapshots();
        _stop.Dispose();
        _gate.Release();
    }
}
