using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;
using SQLitePCL;

namespace SmartSQL.Infrastructure;

public sealed record ManagedProjectUpgrade(int PreviousVersion, int CurrentVersion, string BackupPath, string BackupSha256);

public sealed partial class SqliteWorkspace
{
    private sealed class ManagedValidationBudget : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly int _previousLimit;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        public ManagedValidationBudget(SqliteConnection connection)
        {
            _connection = connection;
            _previousLimit = raw.sqlite3_limit(connection.Handle, raw.SQLITE_LIMIT_LENGTH, 32 * 1024 * 1024);
            raw.sqlite3_progress_handler(connection.Handle, 1000, _ => _elapsed.Elapsed.TotalSeconds >= 30 ? 1 : 0, null);
        }
        public void Dispose()
        {
            raw.sqlite3_progress_handler(_connection.Handle, 0, null, null);
            raw.sqlite3_limit(_connection.Handle, raw.SQLITE_LIMIT_LENGTH, _previousLimit);
        }
    }

    private sealed record ManagedCell(string Kind, byte[]? Bytes)
    {
        public string? Text(Encoding encoding, bool nullable = false)
        {
            if (nullable && Kind == "null") return null;
            if (Kind != "text") throw new InvalidOperationException(InvalidProjectMessage);
            try { return encoding.GetString(Bytes!); }
            catch (DecoderFallbackException exception) { throw new InvalidOperationException("Проект содержит некорректную кодировку текста; данные не преобразованы.", exception); }
        }
        public int Position(Encoding encoding)
        {
            if (Kind != "integer" || !int.TryParse(encoding.GetString(Bytes!), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
                throw new InvalidOperationException(InvalidProjectMessage);
            return position;
        }
    }

    private static string QuoteManaged(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static Encoding ManagedEncoding(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA encoding";
        return (command.ExecuteScalar() as string) switch
        {
            "UTF-8" => new UTF8Encoding(false, true),
            "UTF-16le" => new UnicodeEncoding(false, false, true),
            "UTF-16be" => new UnicodeEncoding(true, false, true),
            _ => throw new InvalidOperationException(InvalidProjectMessage)
        };
    }

    private static IEnumerable<ManagedCell[]> ReadManagedRows(SqliteConnection connection, SqliteTransaction transaction,
        string table, IReadOnlyList<string> columns)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {string.Join(",", columns.Select(c => $"typeof({QuoteManaged(c)}), CAST({QuoteManaged(c)} AS BLOB)"))} FROM {QuoteManaged(table)} ORDER BY rowid";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new ManagedCell[columns.Count];
            long rowBytes = 0;
            for (var index = 0; index < columns.Count; index++)
            {
                var kind = reader.GetString(index * 2);
                byte[]? bytes = null;
                if (kind != "null")
                {
                    // GetBytes/GetStream performs table-column metadata lookup even for
                    // CAST expressions in this driver; use the direct BLOB accessor.
                    bytes = reader.GetFieldValue<byte[]>(index * 2 + 1);
                    rowBytes += bytes.Length;
                    if (bytes.Length > 4 * 1024 * 1024 || rowBytes > 32 * 1024 * 1024)
                        throw new InvalidOperationException("Проверка проекта ограничена 4 МиБ на значение и 32 МиБ на блок строки. Данные не обрезаны и файл не обновлён.");
                }
                row[index] = new(kind, bytes);
            }
            yield return row;
        }
    }

    private static Guid ManagedId(ManagedCell cell, Encoding encoding)
    {
        var text = cell.Text(encoding)!;
        if (!Guid.TryParseExact(text, "N", out var id) || text != id.ToString("N"))
            throw new InvalidOperationException(InvalidProjectMessage);
        return id;
    }

    private static string ManagedName(ManagedCell cell, Encoding encoding)
    {
        var name = cell.Text(encoding)!;
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0'))
            throw new InvalidOperationException(InvalidProjectMessage);
        return name;
    }

    private static void ValidateManagedContents(SqliteConnection connection, int version, SqliteTransaction transaction)
    {
        using var integrity = connection.CreateCommand();
        integrity.Transaction = transaction;
        integrity.CommandText = "PRAGMA integrity_check";
        using (var reader = integrity.ExecuteReader())
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidOperationException("Проверка целостности проекта не пройдена. Файл не обновлён.");
        var encoding = ManagedEncoding(connection, transaction);
        var tables = new Dictionary<Guid, (string Name, List<FieldDefinition> Fields)>();
        var fields = new Dictionary<Guid, (Guid Table, FieldDefinition Field)>();
        long metadataBytes = 0;
        void CountMetadata(ManagedCell[] row)
        {
            metadataBytes += row.Sum(c => (long)(c.Bytes?.Length ?? 0));
            if (metadataBytes > 32 * 1024 * 1024)
                throw new InvalidOperationException("Служебные сведения проекта превышают лимит проверки 32 МиБ. Файл не обновлён.");
        }
        foreach (var row in ReadManagedRows(connection, transaction, "smartsql_tables", ["id", "name"]))
        {
            CountMetadata(row);
            if (tables.Count >= 10_000 || !tables.TryAdd(ManagedId(row[0], encoding), (ManagedName(row[1], encoding), [])))
                throw new InvalidOperationException(InvalidProjectMessage);
        }
        var hasTypes = ReadManagedColumns(connection, "smartsql_fields", transaction).Any(c => c.Name == "field_type");
        foreach (var row in ReadManagedRows(connection, transaction, "smartsql_fields", hasTypes
                     ? ["id", "table_id", "name", "position", "field_type"] : ["id", "table_id", "name", "position"]))
        {
            CountMetadata(row);
            var id = ManagedId(row[0], encoding);
            var owner = ManagedId(row[1], encoding);
            var type = FieldType.Text;
            if (hasTypes && (!Enum.TryParse(row[4].Text(encoding), out type) || !Enum.IsDefined(type)))
                throw new InvalidOperationException(InvalidProjectMessage);
            var field = new FieldDefinition(id, ManagedName(row[2], encoding), row[3].Position(encoding), type);
            if (fields.Count >= 100_000 || !tables.TryGetValue(owner, out var table) || !fields.TryAdd(id, (owner, field))
                || table.Fields.Any(f => f.Position == field.Position || string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(InvalidProjectMessage);
            table.Fields.Add(field);
        }
        var owned = new HashSet<string>(StringComparer.Ordinal) { "smartsql_tables", "smartsql_fields" };
        if (ReadManagedColumns(connection, "smartsql_saved_filters", transaction).Count > 0)
        {
            owned.UnionWith(["smartsql_saved_filters", "smartsql_saved_filter_items"]);
            var filters = new Dictionary<Guid, (Guid Table, HashSet<int> Positions)>();
            foreach (var row in ReadManagedRows(connection, transaction, "smartsql_saved_filters", ["id", "table_id", "name"]))
            {
                CountMetadata(row);
                var owner = ManagedId(row[1], encoding);
                _ = ManagedName(row[2], encoding);
                if (filters.Count >= 100_000 || !tables.ContainsKey(owner) || !filters.TryAdd(ManagedId(row[0], encoding), (owner, [])))
                    throw new InvalidOperationException(InvalidProjectMessage);
            }
            foreach (var row in ReadManagedRows(connection, transaction, "smartsql_saved_filter_items", ["saved_filter_id", "position", "field_id", "operator", "value"]))
            {
                CountMetadata(row);
                if (!filters.TryGetValue(ManagedId(row[0], encoding), out var filter)
                    || !filter.Positions.Add(row[1].Position(encoding)) || filter.Positions.Count > 20
                    || !fields.TryGetValue(ManagedId(row[2], encoding), out var field) || field.Table != filter.Table
                    || !Enum.TryParse<FilterOperator>(row[3].Text(encoding), out var operation) || !Enum.IsDefined(operation))
                    throw new InvalidOperationException(InvalidProjectMessage);
                var value = row[4].Text(encoding, nullable: true);
                if (operation is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty) continue;
                if (operation == FilterOperator.Contains && field.Field.Type != FieldType.Text
                    || operation is FilterOperator.GreaterThan or FilterOperator.LessThan && field.Field.Type == FieldType.Boolean)
                    throw new InvalidOperationException(InvalidProjectMessage);
                try { if (FieldValue.Normalize(field.Field.Type, value) is null) throw new FormatException(); }
                catch (FormatException exception) { throw new InvalidOperationException(InvalidProjectMessage, exception); }
            }
            if (filters.Values.Any(f => f.Positions.Count == 0)) throw new InvalidOperationException(InvalidProjectMessage);
        }
        ValidateManagedForeignKeys(connection, transaction, "smartsql_tables", []);
        ValidateManagedForeignKeys(connection, transaction, "smartsql_fields", [("table_id", "smartsql_tables", "id")], allowMissing: true);
        ValidateManagedIndexes(connection, transaction, "smartsql_tables", [("id", "pk"), ("name", "u")]);
        ValidateManagedIndexes(connection, transaction, "smartsql_fields", [("id", "pk"), ("table_id,name", "u"), ("table_id,position", "u")], legacyFields: true);
        ValidateManagedMetadataDefaults(connection, transaction, "smartsql_tables");
        ValidateManagedMetadataDefaults(connection, transaction, "smartsql_fields");
        if (owned.Contains("smartsql_saved_filters"))
        {
            ValidateManagedForeignKeys(connection, transaction, "smartsql_saved_filters", [("table_id", "smartsql_tables", "id")]);
            ValidateManagedForeignKeys(connection, transaction, "smartsql_saved_filter_items",
                [("saved_filter_id", "smartsql_saved_filters", "id"), ("field_id", "smartsql_fields", "id")]);
            ValidateManagedIndexes(connection, transaction, "smartsql_saved_filters", [("id", "pk"), ("table_id,name", "u")]);
            ValidateManagedIndexes(connection, transaction, "smartsql_saved_filter_items", [("saved_filter_id,position", "pk")]);
            ValidateManagedMetadataDefaults(connection, transaction, "smartsql_saved_filters");
            ValidateManagedMetadataDefaults(connection, transaction, "smartsql_saved_filter_items");
        }
        foreach (var (id, table) in tables)
        {
            var physical = TableName(id);
            owned.Add(physical);
            ValidateManagedIndexes(connection, transaction, physical, [("id", "pk")]);
            var columns = ReadPhysicalColumns(connection, transaction, physical);
            var expected = table.Fields.ToDictionary(f => ColumnName(f.Id), StringComparer.Ordinal);
            if (table.Fields.Count == 0 || columns.Count != expected.Count + 1 || !columns.Any(c => c == new ManagedColumn("id", "TEXT", false, 1))
                || columns.Where(c => c.Name != "id").Any(c => c.Type != "TEXT" || c.NotNull || c.Key != 0 || !expected.ContainsKey(c.Name)))
                throw new InvalidOperationException(InvalidProjectMessage);
            using (var definition = connection.CreateCommand())
            {
                definition.Transaction = transaction;
                definition.CommandText = "SELECT sql FROM sqlite_schema WHERE type='table' AND name=$name";
                definition.Parameters.AddWithValue("$name", physical);
                var sql = definition.ExecuteScalar() as string ?? throw new InvalidOperationException(InvalidProjectMessage);
                var canonical = $"CREATE TABLE {physical} ({string.Join(",", columns.Select(c => c.Name == "id" ? "id TEXT PRIMARY KEY" : c.Name + " TEXT"))})";
                static string Compact(string text) => Regex.Replace(text, "[\\s\"`\\[\\]]+", "", RegexOptions.CultureInvariant).TrimEnd(';').ToUpperInvariant();
                if (Compact(sql) != Compact(canonical)) throw new InvalidOperationException(InvalidProjectMessage);
            }
            foreach (var chunk in table.Fields.Chunk(200))
            {
                foreach (var row in ReadManagedRows(connection, transaction, physical, ["id", .. chunk.Select(f => ColumnName(f.Id))]))
                {
                    _ = ManagedId(row[0], encoding);
                    for (var index = 0; index < chunk.Length; index++)
                    {
                        try { FieldValue.ValidateStored(chunk[index].Type, row[index + 1].Text(encoding, nullable: true)); }
                        catch (FormatException exception) { throw new InvalidOperationException(InvalidProjectMessage, exception); }
                    }
                }
            }
        }
        using var catalog = connection.CreateCommand();
        catalog.Transaction = transaction;
        catalog.CommandText = "SELECT type,name,tbl_name FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%'";
        using (var reader = catalog.ExecuteReader())
            while (reader.Read())
                if (reader.GetString(0) != "table" && reader.GetString(0) != "index" || !owned.Contains(reader.GetString(2))
                    || reader.GetString(0) == "table" && !owned.Contains(reader.GetString(1)))
                    throw new InvalidOperationException(InvalidProjectMessage);
        catalog.CommandText = "PRAGMA foreign_key_check";
        using (var reader = catalog.ExecuteReader())
            if (reader.Read()) throw new InvalidOperationException(InvalidProjectMessage);
    }

    private static void ValidateManagedMetadataDefaults(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sql FROM sqlite_schema WHERE type='table' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        var sql = command.ExecuteScalar() as string ?? throw new InvalidOperationException(InvalidProjectMessage);
        if (Regex.IsMatch(sql, @"\b(CHECK|GENERATED|COLLATE|STRICT|WITHOUT|AUTOINCREMENT|DEFERRABLE)\b|\bON\s+CONFLICT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException(InvalidProjectMessage);
        command.CommandText = "SELECT name,dflt_value FROM pragma_table_xinfo($table) WHERE dflt_value IS NOT NULL";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (table != "smartsql_fields" || reader.GetString(0) != "field_type" || reader.GetString(1) != "'Text'")
                throw new InvalidOperationException(InvalidProjectMessage);
    }

    private static void ValidateManagedIndexes(SqliteConnection connection, SqliteTransaction transaction, string table,
        (string Columns, string Origin)[] expected, bool legacyFields = false)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name,\"unique\",origin,partial FROM pragma_index_list($table)";
        command.Parameters.AddWithValue("$table", table);
        var indexes = new List<(string Name, string Origin)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                if (indexes.Count >= expected.Length || reader.GetInt32(1) != 1 || reader.GetInt32(3) != 0)
                    throw new InvalidOperationException(InvalidProjectMessage);
                indexes.Add((reader.GetString(0), reader.GetString(2)));
            }
        var actual = new HashSet<(string Columns, string Origin)>();
        foreach (var index in indexes)
        {
            command.Parameters.Clear();
            command.CommandText = "SELECT name,coll,desc,cid FROM pragma_index_xinfo($index) WHERE key=1 ORDER BY seqno";
            command.Parameters.AddWithValue("$index", index.Name);
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                if (columns.Count >= 2 || reader.IsDBNull(0) || reader.GetString(1) != "BINARY" || reader.GetInt32(2) != 0 || reader.GetInt32(3) < 0)
                    throw new InvalidOperationException(InvalidProjectMessage);
                columns.Add(reader.GetString(0));
            }
            if (!actual.Add((string.Join(",", columns), index.Origin))) throw new InvalidOperationException(InvalidProjectMessage);
        }
        if (!(legacyFields && actual.SetEquals([("id", "pk")])) && !actual.SetEquals(expected))
            throw new InvalidOperationException(InvalidProjectMessage);
    }

    private static void ValidateManagedForeignKeys(SqliteConnection connection, SqliteTransaction transaction, string table,
        (string Column, string Parent, string Target)[] expected, bool allowMissing = false)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT \"from\",\"table\",\"to\",on_update,on_delete,match FROM pragma_foreign_key_list($table)";
        command.Parameters.AddWithValue("$table", table);
        var actual = new HashSet<(string Column, string Parent, string Target)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (actual.Count >= expected.Length || reader.GetString(3) != "NO ACTION" || reader.GetString(4) != "CASCADE"
                || reader.GetString(5) != "NONE" || !actual.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2))))
                throw new InvalidOperationException(InvalidProjectMessage);
        }
        if (!(allowMissing && actual.Count == 0) && !actual.SetEquals(expected))
            throw new InvalidOperationException(InvalidProjectMessage);
    }

    private static IReadOnlyList<ManagedColumn> ReadPhysicalColumns(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo($table) ORDER BY cid";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        var columns = new List<ManagedColumn>();
        while (reader.Read())
        {
            if (columns.Count >= 2000 || reader.GetInt32(4) != 0) throw new InvalidOperationException(InvalidProjectMessage);
            columns.Add(new(reader.GetString(0), reader.GetString(1).ToUpperInvariant(), reader.GetInt32(2) != 0, reader.GetInt32(3)));
        }
        return columns;
    }

    private static string ManagedFingerprint(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(byte[] bytes)
        {
            digest.AppendData(BitConverter.GetBytes(bytes.Length));
            digest.AppendData(bytes);
        }
        Append(BitConverter.GetBytes(ReadManagedVersion(connection, transaction)));
        var names = new List<string> { "sqlite_schema" };
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE BINARY";
            using var reader = command.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
        }
        foreach (var table in names)
        {
            Append(Encoding.UTF8.GetBytes(table));
            var columns = table == "sqlite_schema" ? new[] { "type", "name", "tbl_name", "sql" }
                : ReadPhysicalColumns(connection, transaction, table).Select(c => c.Name).ToArray();
            foreach (var chunk in columns.Chunk(200))
            {
                Append(Encoding.UTF8.GetBytes(string.Join(",", chunk)));
                foreach (var row in ReadManagedRows(connection, transaction, table, chunk))
                    foreach (var cell in row) { Append(Encoding.ASCII.GetBytes(cell.Kind)); Append(cell.Bytes ?? []); }
                Append([]);
            }
        }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private (string Path, string Sha256) CreateVerifiedUpgradeBackup(SqliteConnection locked, SqliteTransaction transaction, int version)
    {
        var expected = ManagedFingerprint(locked, transaction);
        var backup = Path.Combine(Path.GetDirectoryName(FilePath)!, $"{Path.GetFileNameWithoutExtension(FilePath)}.before-upgrade-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
        var temporary = backup + ".tmp";
        using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
            {
                source.Open(); destination.Open();
                var elapsed = Stopwatch.StartNew();
                using var backupHandle = raw.sqlite3_backup_init(destination.Handle, "main", source.Handle, "main");
                if (backupHandle is null || backupHandle.IsInvalid)
                    throw new InvalidOperationException("Не удалось начать защитное копирование; обновление отменено.");
                while (true)
                {
                    if (elapsed.Elapsed.TotalSeconds >= 30)
                        throw new InvalidOperationException("Защитное копирование превысило 30 секунд; обновление отменено.");
                    var status = raw.sqlite3_backup_step(backupHandle, 64);
                    if (status == raw.SQLITE_DONE) break;
                    if (status == raw.SQLITE_OK) continue;
                    if (status is raw.SQLITE_BUSY or raw.SQLITE_LOCKED) { Thread.Sleep(10); continue; }
                    throw new InvalidOperationException("Не удалось завершить защитное копирование; обновление отменено.");
                }
                if (raw.sqlite3_backup_finish(backupHandle) != raw.SQLITE_OK)
                    throw new InvalidOperationException("SQLite не подтвердил завершение защитной копии; обновление отменено.");
            }
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                copy.Open();
                using var budget = new ManagedValidationBudget(copy);
                using var snapshot = copy.BeginTransaction(deferred: true);
                if (ReadManagedVersion(copy, snapshot) != version) throw new InvalidOperationException("Версия защитной копии не совпала; обновление отменено.");
                ValidateManagedMetadata(copy, version, snapshot);
                ValidateManagedContents(copy, version, snapshot);
                if (ManagedFingerprint(copy, snapshot) != expected)
                    throw new InvalidOperationException("Содержимое защитной копии не совпало; обновление отменено.");
            }
            string sha;
            using (var stream = File.OpenRead(temporary)) sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            File.Move(temporary, backup, overwrite: false);
            return (backup, sha);
        }
        finally { File.Delete(temporary); }
    }
}
