using Microsoft.Data.Sqlite;

namespace SmartSQL.Infrastructure;

public sealed partial class SqliteWorkspace
{
    private const string InvalidProjectMessage = "Файл не является поддерживаемым проектом SmartSQL или его служебная структура повреждена. Для обычной SQLite-базы используйте «SQLite…».";

    /// <summary>Opens a managed project; never creates a missing file or adopts a native SQLite database.</summary>
    public static SqliteWorkspace OpenExisting(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл проекта SmartSQL не найден. Открытие не создаёт новую базу.", path);
        try { return new SqliteWorkspace(path, createMigrationBackup: true, usePooling: true); }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            throw new InvalidOperationException("Файл повреждён или не является базой SQLite. Исходный файл не преобразован.", exception);
        }
    }

    /// <summary>Initializes a private sibling file and publishes it without overwriting any destination.</summary>
    public static SqliteWorkspace CreateNew(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        if (File.Exists(path) || Directory.Exists(path) || HasSqliteSidecars(path))
            throw new IOException("Файл проекта или его служебные файлы уже существуют. Выберите новое имя либо «Открыть проект».");
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.create-{Guid.NewGuid():N}.tmp");
        // Only the uniquely named file owned by this operation is initialized or cleaned up.
        using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            _ = new SqliteWorkspace(temporary, createMigrationBackup: false, usePooling: false, initializeNew: true);
            if (HasSqliteSidecars(path))
                throw new IOException("По выбранному пути появились служебные файлы SQLite. Выберите другое имя проекта.");
            File.Move(temporary, path, overwrite: false);
            return new SqliteWorkspace(path, createMigrationBackup: false, usePooling: true);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                File.Delete(temporary + suffix);
        }
    }

    private static bool HasSqliteSidecars(string path) =>
        File.Exists(path + "-journal") || File.Exists(path + "-wal") || File.Exists(path + "-shm");

    private static int ReadManagedVersion(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(command.ExecuteScalar());
        if (version < 0 || version > CurrentSchemaVersion)
            throw new InvalidOperationException($"Эта база использует неподдерживаемую версию схемы SmartSQL ({version}). Обновите приложение; файл не преобразован.");
        return version;
    }

    private sealed record ManagedColumn(string Name, string Type, bool NotNull, int Key);

    private static void ValidateManagedMetadata(SqliteConnection connection, int version, SqliteTransaction? transaction = null)
    {
        ManagedColumn[] tables = [new("id", "TEXT", false, 1), new("name", "TEXT", true, 0)];
        ManagedColumn[] fields = [new("id", "TEXT", false, 1), new("table_id", "TEXT", true, 0),
            new("name", "TEXT", true, 0), new("position", "INTEGER", true, 0)];
        var hasFieldType = ReadManagedColumns(connection, "smartsql_fields", transaction).Any(c => c.Name == "field_type");
        if (hasFieldType)
            fields = [.. fields, new("field_type", "TEXT", true, 0)];
        else if (version >= 2)
            throw new InvalidOperationException(InvalidProjectMessage);
        RequireColumns("smartsql_tables", tables);
        RequireColumns("smartsql_fields", fields);
        // Genuine v1 files did not all declare the later field FK/unique constraints.
        // Do not require constraints that the existing migration never added.
        RequireUnique("smartsql_tables", ["name"]);

        var filterColumns = ReadManagedColumns(connection, "smartsql_saved_filters", transaction);
        var itemColumns = ReadManagedColumns(connection, "smartsql_saved_filter_items", transaction);
        if (version == CurrentSchemaVersion || filterColumns.Count > 0 || itemColumns.Count > 0)
        {
            RequireColumns("smartsql_saved_filters", [new("id", "TEXT", false, 1), new("table_id", "TEXT", true, 0), new("name", "TEXT", true, 0)]);
            RequireColumns("smartsql_saved_filter_items", [new("saved_filter_id", "TEXT", true, 1), new("position", "INTEGER", true, 2),
                new("field_id", "TEXT", true, 0), new("operator", "TEXT", true, 0), new("value", "TEXT", false, 0)]);
            RequireUnique("smartsql_saved_filters", ["table_id", "name"]);
        }
        using var guard = connection.CreateCommand();
        guard.Transaction = transaction;
        guard.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND tbl_name IN ('smartsql_tables','smartsql_fields','smartsql_saved_filters','smartsql_saved_filter_items')";
        if (Convert.ToInt32(guard.ExecuteScalar()) != 0)
            throw new InvalidOperationException(InvalidProjectMessage);

        void RequireColumns(string name, ManagedColumn[] expected)
        {
            if (!ReadManagedColumns(connection, name, transaction).SequenceEqual(expected))
                throw new InvalidOperationException(InvalidProjectMessage);
        }

        void RequireUnique(string name, string[] columns)
        {
            using var indexes = connection.CreateCommand();
            indexes.Transaction = transaction;
            indexes.CommandText = "SELECT name FROM pragma_index_list($table) WHERE \"unique\"=1 AND partial=0";
            indexes.Parameters.AddWithValue("$table", name);
            var names = new List<string>();
            using (var reader = indexes.ExecuteReader())
                while (reader.Read())
                    names.Add(reader.GetString(0));
            foreach (var index in names)
            {
                using var terms = connection.CreateCommand();
                terms.Transaction = transaction;
                terms.CommandText = "SELECT name FROM pragma_index_info($index) ORDER BY seqno";
                terms.Parameters.AddWithValue("$index", index);
                using var reader = terms.ExecuteReader();
                var actual = new List<string?>();
                while (reader.Read()) actual.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                if (actual.SequenceEqual(columns)) return;
            }
            throw new InvalidOperationException(InvalidProjectMessage);
        }
    }

    private static IReadOnlyList<ManagedColumn> ReadManagedColumns(SqliteConnection connection, string table, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT type FROM pragma_table_list WHERE schema='main' AND name=$table";
        command.Parameters.AddWithValue("$table", table);
        if (!string.Equals(command.ExecuteScalar() as string, "table", StringComparison.Ordinal))
            return [];
        command.CommandText = "SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo($table) ORDER BY cid";
        using var reader = command.ExecuteReader();
        var columns = new List<ManagedColumn>();
        while (reader.Read())
        {
            if (columns.Count >= 6 || reader.GetInt32(4) != 0)
                throw new InvalidOperationException(InvalidProjectMessage);
            columns.Add(new(reader.GetString(0), reader.GetString(1).ToUpperInvariant(), reader.GetInt32(2) != 0, reader.GetInt32(3)));
        }
        return columns;
    }

    private void InitializeManagedFile(bool initializeNew, bool createMigrationBackup)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var version = ReadManagedVersion(connection, transaction);
        if (!initializeNew)
            ValidateManagedMetadata(connection, version, transaction);
        if (version == CurrentSchemaVersion)
            return;
        if (!initializeNew && createMigrationBackup)
        {
            var backupPath = Path.Combine(Path.GetDirectoryName(FilePath)!,
                $"{Path.GetFileNameWithoutExtension(FilePath)}.before-upgrade-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
            CreateBackup(backupPath);
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smartsql_tables (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS smartsql_fields (
                id TEXT PRIMARY KEY,
                table_id TEXT NOT NULL REFERENCES smartsql_tables(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                position INTEGER NOT NULL,
                UNIQUE(table_id, name), UNIQUE(table_id, position)
            );
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('smartsql_fields') WHERE name='field_type'";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = "ALTER TABLE smartsql_fields ADD COLUMN field_type TEXT NOT NULL DEFAULT 'Text'";
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smartsql_saved_filters (
                id TEXT PRIMARY KEY, table_id TEXT NOT NULL REFERENCES smartsql_tables(id) ON DELETE CASCADE,
                name TEXT NOT NULL, UNIQUE(table_id,name)
            );
            CREATE TABLE IF NOT EXISTS smartsql_saved_filter_items (
                saved_filter_id TEXT NOT NULL REFERENCES smartsql_saved_filters(id) ON DELETE CASCADE,
                position INTEGER NOT NULL, field_id TEXT NOT NULL REFERENCES smartsql_fields(id) ON DELETE CASCADE,
                operator TEXT NOT NULL, value TEXT NULL, PRIMARY KEY(saved_filter_id,position)
            );
            PRAGMA user_version=3;
            """;
        command.ExecuteNonQuery();
        ValidateManagedMetadata(connection, CurrentSchemaVersion, transaction);
        transaction.Commit();
    }
}
