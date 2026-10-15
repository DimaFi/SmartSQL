using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class NativeSqliteDatabaseBrowser
{
    private readonly Dictionary<Guid, NativeEditSnapshot> _editSnapshots = [];
    private string? _backupDirectory;
    public bool EditingEnabled => _backupDirectory is not null;
    private static readonly TimeSpan NativeEditLifetime = TimeSpan.FromMinutes(15);
    private sealed record NativeEditMetadata(string Fingerprint, IReadOnlyList<DatabaseColumn> Columns,
        DatabaseEditCapabilities Capabilities);
    private sealed record NativeEditSnapshot(DatabaseTableRef Table, NativeEditMetadata Metadata,
        NativeRawValue[] Values, DateTimeOffset CreatedAt);
    private sealed record NativeRawValue(string Storage, object? Value);
    private sealed record NativeSchemaObject(string Type, string Name, string Table, long RootPage, string? Sql);

    public static async Task<NativeSqliteDatabaseBrowser> OpenEditableAsync(string absolutePath,
        string backupDirectory, CancellationToken cancellationToken = default)
    {
        var backup = ValidateBackupDirectory(absolutePath, backupDirectory);
        var browser = await OpenAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        browser._backupDirectory = backup;
        return browser;
    }

    public Task<DatabaseEditCapabilities> GetEditCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        Validate(table);
        return ReadAsync(context =>
        {
            var relation = Relation(context, table);
            var columns = Columns(context, relation);
            if (!EditingEnabled)
                return new DatabaseEditCapabilities(table, false, columns.Select(column => new EditableDatabaseColumn(
                    column.Name, column.Ordinal, column.NativeType, DatabaseEditValueKind.Text, false, column.IsNullable,
                    ReadOnlyReason: "Подключение открыто только для чтения.")).ToArray(),
                    [new("edit_not_allowed", "Редактирование включается отдельно для текущего подключения. Файл открыт только для чтения.")]);
            return ReadEditMetadata(context, relation, columns).Capabilities;
        }, cancellationToken);
    }

    private void InvalidateEditSnapshots() => _editSnapshots.Clear();

    private NativeEditMetadata ReadEditMetadata(Context context, NativeRelation relation,
        IReadOnlyList<DatabaseColumn> columns)
    {
        var (schema, fingerprint) = ReadSchema(context);
        var target = schema.SingleOrDefault(item => item.Type == "table" && item.Name == relation.Table.Name);
        string? reason = !EditingEnabled ? "Подключение открыто только для чтения. Редактирование включается отдельно для текущего подключения."
            : relation.Table.Kind != DatabaseObjectKind.Table || target is null ? "Изменяется только обычная таблица main."
            : columns.Count == 0 || columns.Count > 600 ? "Число столбцов не подходит для ограниченного редактирования."
            : null;
        if (reason is null && (schema.Any(item => item.Type == "trigger" && item.Table.Equals(relation.Table.Name, StringComparison.OrdinalIgnoreCase))
            || Regex.IsMatch(target!.Sql ?? "", @"\b(CHECK|COLLATE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            reason = "Триггеры, CHECK и явно заданные COLLATE пока исключают безопасное изменение этой таблицы.";
        using (var command = context.Command("SELECT count(*) FROM pragma_table_xinfo($table,'main') WHERE hidden<>0"))
        {
            command.Parameters.AddWithValue("$table", relation.Table.Name);
            if (reason is null && (long)command.ExecuteScalar()! != 0)
                reason = "Вычисляемые и скрытые поля пока исключают редактирование таблицы.";
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new List<(string Name, bool Unique, bool Partial)>();
        using (var command = context.Command("SELECT name,\"unique\",partial FROM pragma_index_list($table,'main')"))
        {
            command.Parameters.AddWithValue("$table", relation.Table.Name);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                context.Token.ThrowIfCancellationRequested();
                if (indexes.Count >= 10_000) throw new DatabaseBrowserException("edit_not_allowed", "Слишком много индексов для ограниченной проверки.");
                indexes.Add((reader.GetString(0), reader.GetInt64(1) != 0, reader.GetInt64(2) != 0));
            }
        }
        foreach (var index in indexes)
        {
            if (index.Partial) reason ??= "Частичные индексы пока исключают редактирование таблицы.";
            using var command = context.Command("SELECT cid,name,coll,\"key\" FROM pragma_index_xinfo($index,'main')");
            command.Parameters.AddWithValue("$index", index.Name);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                context.Token.ThrowIfCancellationRequested();
                if (reader.GetInt64(3) == 0) continue;
                if (reader.GetInt64(0) < 0 || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.GetString(2) != "BINARY")
                    reason ??= "Индекс с выражением или collation, отличной от BINARY, пока исключает редактирование.";
                if (index.Unique && !reader.IsDBNull(1)) unique.Add(reader.GetString(1));
            }
        }
        if (reason is null)
        {
            foreach (var item in schema.Where(item => item.Type == "table"))
            {
                context.Token.ThrowIfCancellationRequested();
                using var command = context.Command("SELECT \"table\" FROM pragma_foreign_key_list($table,'main')");
                command.Parameters.AddWithValue("$table", item.Name);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    if (item.Name == relation.Table.Name || reader.GetString(0).Equals(relation.Table.Name, StringComparison.OrdinalIgnoreCase))
                        reason = "Любые входящие и исходящие внешние ключи пока исключают редактирование таблицы.";
            }
        }
        var keys = columns.Where(column => column.PrimaryKeyOrder is not null).OrderBy(column => column.PrimaryKeyOrder).ToArray();
        if (keys.Length == 0 || keys.Any(column => column.IsNullable || QueryKind(column.NativeType) is not (DatabaseFilterValueKind.Integer or DatabaseFilterValueKind.Text)))
            reason ??= "Нужен полный первичный ключ с гарантированным NOT NULL из поддерживаемых INTEGER/TEXT; скрытый rowid не подставляется.";

        var editable = new List<EditableDatabaseColumn>();
        foreach (var column in columns)
        {
            context.Token.ThrowIfCancellationRequested();
            var kind = QueryKind(column.NativeType);
            string? columnReason = column.PrimaryKeyOrder is not null ? "Первичный ключ защищён от изменения."
                : unique.Contains(column.Name) ? "Поле уникального индекса защищено от изменения."
                : kind == DatabaseFilterValueKind.Unsupported ? "Этот нативный тип пока доступен только для чтения."
                : null;
            if (reason is null && kind != DatabaseFilterValueKind.Unsupported)
            {
                try { ValidateFilterColumn(context, relation.Table, column, kind); }
                catch (DatabaseBrowserException exception) when (exception.Code is "filter_storage_incompatible" or "unsupported_text_encoding" or "cell_too_large")
                {
                    columnReason = "В столбце обнаружено несовместимое или неподдерживаемое значение SQLite; преобразование не выполняется.";
                    if (column.PrimaryKeyOrder is not null) reason = "Тип значения первичного ключа не подтверждён для всех строк.";
                }
            }
            var editKind = kind switch
            {
                DatabaseFilterValueKind.Integer => DatabaseEditValueKind.Integer,
                DatabaseFilterValueKind.Number => DatabaseEditValueKind.Decimal,
                DatabaseFilterValueKind.Boolean => DatabaseEditValueKind.Boolean,
                DatabaseFilterValueKind.Date => DatabaseEditValueKind.Date,
                _ => DatabaseEditValueKind.Text
            };
            editable.Add(new(column.Name, column.Ordinal, column.NativeType, editKind,
                reason is null && columnReason is null, column.IsNullable, ReadOnlyReason: columnReason ?? reason));
        }
        var canUpdate = reason is null && editable.Any(column => column.CanEdit);
        if (!canUpdate)
        {
            reason ??= "Нет полей, которые можно безопасно изменить в этом срезе.";
            editable = editable.Select(column => column with { CanEdit = false, ReadOnlyReason = column.ReadOnlyReason ?? reason }).ToList();
        }
        var diagnostics = new List<DatabaseDiagnostic>
        {
            new("sqlite_single_cell_edit", "Разрешено изменение только одной поддерживаемой ячейки. INSERT/DELETE не поддерживаются. Перед записью обязательна проверенная копия SQLite в отдельном каталоге на D:."),
            new("sqlite_edit_handles", "Разрешение строки одноразовое и действует до 15 минут, до следующего чтения страницы/каталога/структуры/возможностей либо закрытия подключения."),
            new("sqlite_raw_row_guard", "Перед записью проверяется структура и полная исходная строка, включая классы хранения и байты. SQLite не предоставляет версию строки: изменение с возвратом к полностью тем же значениям не обнаруживается.")
        };
        if (reason is not null) diagnostics.Add(new("edit_not_allowed", reason));
        return new(fingerprint, columns, new(relation.Table, canUpdate, editable, diagnostics));
    }

    private static (IReadOnlyList<NativeSchemaObject> Objects, string Fingerprint) ReadSchema(Context context, bool includeVersion = true)
    {
        var objects = new List<NativeSchemaObject>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var total = 0;
        void Add(string? value)
        {
            if (value is null) { hash.AppendData(BitConverter.GetBytes(-1)); return; }
            total += value.Length;
            if (value.Length > MaxCell || total > MaxPage)
                throw new DatabaseBrowserException("edit_not_allowed", "Структура слишком велика для ограниченной проверки редактирования.");
            var bytes = StrictUtf8.GetBytes(value);
            hash.AppendData(BitConverter.GetBytes(bytes.Length));
            hash.AppendData(bytes);
        }
        if (includeVersion)
        {
            using var version = context.Command("PRAGMA main.schema_version");
            Add(Convert.ToString(version.ExecuteScalar(), CultureInfo.InvariantCulture));
        }
        using var command = context.Command("SELECT type,name,tbl_name,rootpage,sql FROM main.sqlite_schema ORDER BY type COLLATE BINARY,name COLLATE BINARY");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            if (objects.Count >= 10_000) throw new DatabaseBrowserException("edit_not_allowed", "Структура превышает 10 000 объектов.");
            var item = new NativeSchemaObject(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4));
            objects.Add(item);
            Add(item.Type); Add(item.Name); Add(item.Table); Add(item.RootPage.ToString(CultureInfo.InvariantCulture)); Add(item.Sql);
        }
        return (objects, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static NativeRawValue[] ReadRawRow(SqliteDataReader reader, int columns)
    {
        var values = new NativeRawValue[columns];
        long total = 0;
        for (var index = 0; index < columns; index++)
        {
            var storage = reader.GetString(index * 3);
            var bytes = reader.IsDBNull(index * 3 + 1) ? 0 : reader.GetInt64(index * 3 + 1);
            total += bytes;
            if (bytes > MaxCell * 4L || storage == "blob" && bytes > (MaxCell - 2) / 2 || total > MaxPage * 4L)
                throw new DatabaseBrowserException("edit_conflict", "Строка слишком велика для безопасного сравнения; обновите страницу.");
            object? value = storage switch
            {
                "null" => null,
                "integer" => reader.GetInt64(index * 3 + 2),
                "real" => reader.GetDouble(index * 3 + 2),
                "text" or "blob" => (byte[])reader.GetValue(index * 3 + 2),
                _ => throw new DatabaseBrowserException("edit_not_allowed", "Неизвестный класс хранения SQLite.")
            };
            values[index] = new(storage, value);
        }
        return values;
    }

    private static bool SameRaw(NativeRawValue left, NativeRawValue right) => left.Storage == right.Storage &&
        ((left.Value, right.Value) switch
        {
            (null, null) => true,
            (byte[] first, byte[] second) => first.AsSpan().SequenceEqual(second),
            (double first, double second) => BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second),
            (long first, long second) => first == second,
            _ => false
        });
}
