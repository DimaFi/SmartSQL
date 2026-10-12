using Microsoft.Data.Sqlite;
using System.Globalization;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed class SqliteWorkspace : IDataWorkspace
{
    private const int CurrentSchemaVersion = 3;
    private readonly bool _pooling;
    static SqliteWorkspace() => SQLitePCL.Batteries_V2.Init();

    public string FilePath { get; }
    public int SchemaVersion => CurrentSchemaVersion;

    public SqliteWorkspace(string filePath) : this(filePath, createMigrationBackup: true, usePooling: true)
    {
    }

    private SqliteWorkspace(string filePath, bool createMigrationBackup, bool usePooling)
    {
        _pooling = usePooling;
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var connection = OpenConnection();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException($"Эта база создана более новой версией SmartSQL (схема {version}). Обновите приложение.");
        if (version == CurrentSchemaVersion)
        {
            using var validate = connection.CreateCommand();
            validate.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('smartsql_tables', 'smartsql_fields', 'smartsql_saved_filters', 'smartsql_saved_filter_items')";
            if (Convert.ToInt32(validate.ExecuteScalar()) != 4)
                throw new InvalidOperationException("Выбранный файл не является базой SmartSQL или его структура повреждена.");
            validate.CommandText = "SELECT COUNT(*) FROM pragma_table_info('smartsql_fields') WHERE name = 'field_type'";
            if (Convert.ToInt32(validate.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Выбранный файл не является базой SmartSQL или его структура повреждена.");
            return;
        }

        using var inspect = connection.CreateCommand();
        inspect.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'smartsql_tables'";
        var isSmartSql = Convert.ToInt32(inspect.ExecuteScalar()) > 0;
        inspect.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        var hasUserTables = Convert.ToInt32(inspect.ExecuteScalar()) > 0;
        if (hasUserTables && !isSmartSql)
            throw new InvalidOperationException("Выбранный файл не является базой SmartSQL.");
        if (hasUserTables && createMigrationBackup)
        {
            var backupPath = Path.Combine(Path.GetDirectoryName(FilePath)!,
                $"{Path.GetFileNameWithoutExtension(FilePath)}.before-upgrade-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
            CreateBackup(backupPath);
        }

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smartsql_tables (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS smartsql_fields (
                id TEXT PRIMARY KEY,
                table_id TEXT NOT NULL REFERENCES smartsql_tables(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                position INTEGER NOT NULL,
                UNIQUE(table_id, name),
                UNIQUE(table_id, position)
            );
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('smartsql_fields') WHERE name = 'field_type'";
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = "ALTER TABLE smartsql_fields ADD COLUMN field_type TEXT NOT NULL DEFAULT 'Text'";
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS smartsql_saved_filters (
                id TEXT PRIMARY KEY,
                table_id TEXT NOT NULL REFERENCES smartsql_tables(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                UNIQUE(table_id, name)
            );
            CREATE TABLE IF NOT EXISTS smartsql_saved_filter_items (
                saved_filter_id TEXT NOT NULL REFERENCES smartsql_saved_filters(id) ON DELETE CASCADE,
                position INTEGER NOT NULL,
                field_id TEXT NOT NULL REFERENCES smartsql_fields(id) ON DELETE CASCADE,
                operator TEXT NOT NULL,
                value TEXT NULL,
                PRIMARY KEY(saved_filter_id, position)
            );
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void CreateBackup(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(fullPath, FilePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Путь копии должен отличаться от пути базы.", nameof(destinationPath));
        if (File.Exists(fullPath))
            throw new IOException("Файл резервной копии уже существует.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var source = OpenConnection();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    public RestorationResult RestoreBackup(string sourcePath)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourceFullPath))
            throw new FileNotFoundException("Файл резервной копии не найден.", sourceFullPath);
        if (string.Equals(sourceFullPath, FilePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Нельзя восстановить базу из неё самой.", nameof(sourcePath));

        var temporaryPath = Path.Combine(Path.GetDirectoryName(FilePath)!, $".{Path.GetFileName(FilePath)}.restore-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = sourceFullPath,
                       Mode = SqliteOpenMode.ReadOnly,
                       Pooling = false
                   }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporaryPath, Pooling = false }.ToString()))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            using (var inspect = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporaryPath, Pooling = false }.ToString()))
            {
                inspect.Open();
                using var command = inspect.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'smartsql_tables'";
                if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("Выбранный файл не является резервной копией SmartSQL.");
            }
            _ = new SqliteWorkspace(temporaryPath, createMigrationBackup: false, usePooling: false);
            var safetyBackup = CreateSafetyBackup("before-restore");
            using (var restored = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = temporaryPath,
                       Mode = SqliteOpenMode.ReadOnly,
                       Pooling = false
                   }.ToString()))
            using (var active = OpenConnection())
            {
                restored.Open();
                restored.BackupDatabase(active);
            }
            return new RestorationResult(safetyBackup);
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException("Не удалось прочитать резервную копию SQLite.", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public IReadOnlyList<TableDefinition> GetTables()
    {
        using var connection = OpenConnection();
        var tables = new List<TableDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM smartsql_tables ORDER BY name COLLATE NOCASE";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                tables.Add(new TableDefinition(Guid.Parse(reader.GetString(0)), reader.GetString(1), []));
        }

        for (var index = 0; index < tables.Count; index++)
        {
            var table = tables[index];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, position, field_type FROM smartsql_fields WHERE table_id = $tableId ORDER BY position";
            command.Parameters.AddWithValue("$tableId", table.Id.ToString("N"));
            using var reader = command.ExecuteReader();
            var fields = new List<FieldDefinition>();
            while (reader.Read())
            {
                if (!Enum.TryParse<FieldType>(reader.GetString(3), out var type) || !Enum.IsDefined(type))
                    throw new InvalidOperationException("База содержит неизвестный тип поля. Обновите приложение.");
                fields.Add(new FieldDefinition(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2), type));
            }
            tables[index] = table with { Fields = fields };
        }

        return tables;
    }

    public TableDefinition CreateTable(string name, IReadOnlyList<string> fieldNames) =>
        CreateTable(name, fieldNames.Select(field => new FieldInput(field, FieldType.Text)).ToArray());

    public TableDefinition CreateTable(string name, IReadOnlyList<FieldInput> fieldInputs) =>
        CreateTableWithRecords(name, fieldInputs, []);

    public TableDefinition CreateTableWithRecords(string name, IReadOnlyList<FieldInput> fieldInputs,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        name = name.Trim();
        var inputs = fieldInputs.Select(field => field with { Name = field.Name.Trim() }).ToArray();
        if (name.Length == 0 || inputs.Length == 0 || inputs.Any(field => field.Name.Length == 0))
            throw new ArgumentException("Укажите название таблицы и хотя бы одно поле.");
        if (inputs.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Length)
            throw new ArgumentException("Названия полей не должны повторяться.");
        if (inputs.Any(field => !Enum.IsDefined(field.Type)))
            throw new ArgumentException("Неизвестный тип поля.");
        if (rows.Any(row => row.Count != inputs.Length))
            throw new ArgumentException("Количество значений в строке не совпадает с количеством полей.", nameof(rows));

        var id = Guid.NewGuid();
        var fields = inputs.Select((field, position) => new FieldDefinition(Guid.NewGuid(), field.Name, position, field.Type)).ToArray();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO smartsql_tables(id, name) VALUES ($id, $name)";
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            command.Parameters.AddWithValue("$name", name);
            command.ExecuteNonQuery();
        }

        foreach (var field in fields)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO smartsql_fields(id, table_id, name, position, field_type) VALUES ($id, $tableId, $name, $position, $type)";
            command.Parameters.AddWithValue("$id", field.Id.ToString("N"));
            command.Parameters.AddWithValue("$tableId", id.ToString("N"));
            command.Parameters.AddWithValue("$name", field.Name);
            command.Parameters.AddWithValue("$position", field.Position);
            command.Parameters.AddWithValue("$type", field.Type.ToString());
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"CREATE TABLE {TableName(id)} (id TEXT PRIMARY KEY, {string.Join(", ", fields.Select(field => $"{ColumnName(field.Id)} TEXT"))})";
            command.ExecuteNonQuery();
        }

        if (rows.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {TableName(id)} (id, {string.Join(", ", fields.Select(field => ColumnName(field.Id)))}) VALUES ($id, {string.Join(", ", fields.Select((_, index) => $"$value{index}"))})";
            var idParameter = command.Parameters.Add("$id", SqliteType.Text);
            var valueParameters = fields.Select((_, index) => command.Parameters.Add($"$value{index}", SqliteType.Text)).ToArray();
            foreach (var row in rows)
            {
                idParameter.Value = Guid.NewGuid().ToString("N");
                for (var index = 0; index < fields.Length; index++)
                    valueParameters[index].Value = FieldValue.Normalize(fields[index].Type, row[index]) ?? (object)DBNull.Value;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return new TableDefinition(id, name, fields);
    }

    public FieldDefinition AddField(Guid tableId, FieldInput input)
    {
        var table = FindTable(tableId);
        var name = input.Name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Укажите название поля.", nameof(input));
        if (!Enum.IsDefined(input.Type))
            throw new ArgumentException("Неизвестный тип поля.", nameof(input));
        if (table.Fields.Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Поле с таким названием уже есть.", nameof(input));

        var field = new FieldDefinition(Guid.NewGuid(), name, table.Fields.Count, input.Type);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE {TableName(tableId)} ADD COLUMN {ColumnName(field.Id)} TEXT";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO smartsql_fields(id, table_id, name, position, field_type) VALUES ($id, $tableId, $name, $position, $type)";
        command.Parameters.AddWithValue("$id", field.Id.ToString("N"));
        command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
        command.Parameters.AddWithValue("$name", field.Name);
        command.Parameters.AddWithValue("$position", field.Position);
        command.Parameters.AddWithValue("$type", field.Type.ToString());
        command.ExecuteNonQuery();
        transaction.Commit();
        return field;
    }

    public TableDefinition UpdateSchema(Guid tableId, string tableName, IReadOnlyDictionary<Guid, string> fieldNames)
    {
        var table = FindTable(tableId);
        tableName = tableName.Trim();
        if (tableName.Length == 0)
            throw new ArgumentException("Укажите название таблицы.", nameof(tableName));
        if (fieldNames.Count != table.Fields.Count || table.Fields.Any(field => !fieldNames.ContainsKey(field.Id)) ||
            fieldNames.Keys.Any(id => table.Fields.All(field => field.Id != id)))
            throw new ArgumentException("Передайте названия всех существующих полей без изменения их состава.", nameof(fieldNames));
        var names = table.Fields.Select(field => fieldNames[field.Id].Trim()).ToArray();
        if (names.Any(name => name.Length == 0))
            throw new ArgumentException("Названия полей не могут быть пустыми.", nameof(fieldNames));
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Названия полей не должны повторяться.", nameof(fieldNames));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE smartsql_tables SET name = $name WHERE id = $id";
            command.Parameters.AddWithValue("$name", tableName);
            command.Parameters.AddWithValue("$id", tableId.ToString("N"));
            command.ExecuteNonQuery();
        }
        for (var index = 0; index < table.Fields.Count; index++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE smartsql_fields SET name = $name WHERE id = $id";
            command.Parameters.AddWithValue("$name", names[index]);
            command.Parameters.AddWithValue("$id", table.Fields[index].Id.ToString("N"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return table with
        {
            Name = tableName,
            Fields = table.Fields.Select((field, index) => field with { Name = names[index] }).ToArray()
        };
    }

    public IReadOnlyList<DataRecord> GetRecords(Guid tableId) => GetRecords(tableId, 0, int.MaxValue);

    public IReadOnlyList<DataRecord> GetRecords(Guid tableId, int offset, int limit) =>
        QueryRecords(tableId, offset, limit, null, null, true);

    public IReadOnlyList<DataRecord> QueryRecords(Guid tableId, int offset, int limit, string? search, Guid? sortFieldId, bool descending) =>
        QueryRecords(tableId, offset, limit, search, sortFieldId, descending, []);

    public IReadOnlyList<DataRecord> QueryRecords(Guid tableId, int offset, int limit, string? search, Guid? sortFieldId, bool descending, IReadOnlyList<RecordFilter> filters)
    {
        if (offset < 0 || limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Смещение и размер страницы должны быть положительными.");
        var table = FindTable(tableId);
        if (sortFieldId.HasValue && !table.Fields.Any(field => field.Id == sortFieldId.Value))
            throw new ArgumentException("Поле сортировки не найдено.", nameof(sortFieldId));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var where = BuildWhereClause(table, search, filters, command);
        var sortType = sortFieldId.HasValue ? table.Fields.Single(field => field.Id == sortFieldId.Value).Type : FieldType.Text;
        var collation = sortType == FieldType.Number ? "SMARTSQL_NUMBER" : "UNICODE_NOCASE";
        var order = sortFieldId.HasValue
            ? $"{ColumnName(sortFieldId.Value)} COLLATE {collation} {(descending ? "DESC" : "ASC")}, rowid DESC"
            : "rowid DESC";
        command.CommandText = $"SELECT id, {string.Join(", ", table.Fields.Select(field => ColumnName(field.Id)))} FROM {TableName(tableId)} {where} ORDER BY {order} LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var records = new List<DataRecord>();
        while (reader.Read())
        {
            var values = new Dictionary<Guid, string?>();
            for (var index = 0; index < table.Fields.Count; index++)
                values[table.Fields[index].Id] = reader.IsDBNull(index + 1) ? null : reader.GetString(index + 1);
            records.Add(new DataRecord(Guid.Parse(reader.GetString(0)), values));
        }
        return records;
    }

    public int CountRecords(Guid tableId) => CountRecords(tableId, null);

    public int CountRecords(Guid tableId, string? search) => CountRecords(tableId, search, []);

    public int CountRecords(Guid tableId, string? search, IReadOnlyList<RecordFilter> filters)
    {
        var table = FindTable(tableId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {TableName(tableId)} {BuildWhereClause(table, search, filters, command)}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<SavedFilterDefinition> GetSavedFilters(Guid tableId)
    {
        var table = FindTable(tableId);
        using var connection = OpenConnection();
        var saved = new List<SavedFilterDefinition>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM smartsql_saved_filters WHERE table_id = $tableId ORDER BY name COLLATE UNICODE_NOCASE";
            command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
            using var reader = command.ExecuteReader();
            while (reader.Read())
                saved.Add(new SavedFilterDefinition(Guid.Parse(reader.GetString(0)), tableId, reader.GetString(1), []));
        }
        for (var index = 0; index < saved.Count; index++)
        {
            var definition = saved[index];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT field_id, operator, value FROM smartsql_saved_filter_items WHERE saved_filter_id = $id ORDER BY position";
            command.Parameters.AddWithValue("$id", definition.Id.ToString("N"));
            using var reader = command.ExecuteReader();
            var filters = new List<RecordFilter>();
            while (reader.Read())
            {
                var fieldId = Guid.Parse(reader.GetString(0));
                if (table.Fields.All(field => field.Id != fieldId) ||
                    !Enum.TryParse<FilterOperator>(reader.GetString(1), out var operation) || !Enum.IsDefined(operation))
                    throw new InvalidOperationException("Сохранённый фильтр содержит неизвестное поле или операцию.");
                filters.Add(new RecordFilter(fieldId, operation, reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
            saved[index] = definition with { Filters = filters };
        }
        return saved;
    }

    public SavedFilterDefinition SaveFilter(Guid tableId, string name, IReadOnlyList<RecordFilter> filters)
    {
        var table = FindTable(tableId);
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Укажите название фильтра.", nameof(name));
        if (filters.Count == 0)
            throw new ArgumentException("Добавьте хотя бы одно условие фильтра.", nameof(filters));
        using var connection = OpenConnection();
        using (var validation = connection.CreateCommand())
            BuildWhereClause(table, null, filters, validation);

        using var transaction = connection.BeginTransaction();
        Guid id;
        bool exists;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM smartsql_saved_filters WHERE table_id = $tableId AND name = $name COLLATE UNICODE_NOCASE";
            find.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
            find.Parameters.AddWithValue("$name", name);
            var existing = find.ExecuteScalar() as string;
            exists = existing is not null;
            id = existing is null ? Guid.NewGuid() : Guid.Parse(existing);
        }
        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = exists
                ? "UPDATE smartsql_saved_filters SET name = $name WHERE id = $id AND table_id = $tableId"
                : "INSERT INTO smartsql_saved_filters(id, table_id, name) VALUES ($id, $tableId, $name)";
            metadata.Parameters.AddWithValue("$id", id.ToString("N"));
            metadata.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
            metadata.Parameters.AddWithValue("$name", name);
            metadata.ExecuteNonQuery();
            metadata.CommandText = "DELETE FROM smartsql_saved_filter_items WHERE saved_filter_id = $id";
            metadata.Parameters.Clear();
            metadata.Parameters.AddWithValue("$id", id.ToString("N"));
            metadata.ExecuteNonQuery();
        }
        for (var index = 0; index < filters.Count; index++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO smartsql_saved_filter_items(saved_filter_id, position, field_id, operator, value) VALUES ($id, $position, $fieldId, $operator, $value)";
            command.Parameters.AddWithValue("$id", id.ToString("N"));
            command.Parameters.AddWithValue("$position", index);
            command.Parameters.AddWithValue("$fieldId", filters[index].FieldId.ToString("N"));
            command.Parameters.AddWithValue("$operator", filters[index].Operator.ToString());
            command.Parameters.AddWithValue("$value", filters[index].Value ?? (object)DBNull.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return new SavedFilterDefinition(id, tableId, name, filters.ToArray());
    }

    public void DeleteSavedFilter(Guid tableId, Guid savedFilterId)
    {
        FindTable(tableId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM smartsql_saved_filters WHERE id = $id AND table_id = $tableId";
        command.Parameters.AddWithValue("$id", savedFilterId.ToString("N"));
        command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
            throw new ArgumentException("Сохранённый фильтр не найден.", nameof(savedFilterId));
    }

    public DeletionImpact GetFieldDeletionImpact(Guid tableId, Guid fieldId)
    {
        var table = FindTable(tableId);
        var field = table.Fields.FirstOrDefault(item => item.Id == fieldId)
            ?? throw new ArgumentException("Поле не найдено.", nameof(fieldId));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(DISTINCT saved_filter_id) FROM smartsql_saved_filter_items WHERE field_id = $fieldId";
        command.Parameters.AddWithValue("$fieldId", fieldId.ToString("N"));
        var filters = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = $"SELECT COUNT(*) FROM {TableName(tableId)} WHERE {ColumnName(fieldId)} IS NOT NULL";
        command.Parameters.Clear();
        var records = Convert.ToInt32(command.ExecuteScalar());
        return new DeletionImpact(field.Name, records, filters);
    }

    public DestructiveChangeResult DeleteField(Guid tableId, Guid fieldId)
    {
        var table = FindTable(tableId);
        if (table.Fields.Count <= 1)
            throw new InvalidOperationException("Нельзя удалить последнее поле. Удалите таблицу целиком.");
        if (table.Fields.All(field => field.Id != fieldId))
            throw new ArgumentException("Поле не найдено.", nameof(fieldId));
        var backupPath = CreateSafetyBackup("before-delete-field");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE {TableName(tableId)} DROP COLUMN {ColumnName(fieldId)}";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM smartsql_saved_filters WHERE id IN (SELECT saved_filter_id FROM smartsql_saved_filter_items WHERE field_id = $fieldId)";
        command.Parameters.AddWithValue("$fieldId", fieldId.ToString("N"));
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM smartsql_fields WHERE id = $fieldId AND table_id = $tableId";
        command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
            throw new ArgumentException("Поле не найдено.", nameof(fieldId));
        transaction.Commit();
        return new DestructiveChangeResult(backupPath);
    }

    public DeletionImpact GetTableDeletionImpact(Guid tableId)
    {
        var table = FindTable(tableId);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {TableName(tableId)}";
        var records = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM smartsql_saved_filters WHERE table_id = $tableId";
        command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
        var filters = Convert.ToInt32(command.ExecuteScalar());
        return new DeletionImpact(table.Name, records, filters);
    }

    public DestructiveChangeResult DeleteTable(Guid tableId)
    {
        FindTable(tableId);
        var backupPath = CreateSafetyBackup("before-delete-table");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE {TableName(tableId)}";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM smartsql_tables WHERE id = $tableId";
        command.Parameters.AddWithValue("$tableId", tableId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
            throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        transaction.Commit();
        return new DestructiveChangeResult(backupPath);
    }

    public DataRecord AddRecord(Guid tableId, IReadOnlyDictionary<Guid, string?> values)
    {
        var table = FindTable(tableId);
        if (values.Keys.Except(table.Fields.Select(field => field.Id)).Any())
            throw new ArgumentException("Запись содержит неизвестное поле.");
        var id = Guid.NewGuid();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var fieldIds = table.Fields.Select(field => field.Id).ToArray();
        command.CommandText = $"INSERT INTO {TableName(tableId)} (id, {string.Join(", ", fieldIds.Select(ColumnName))}) VALUES ($id, {string.Join(", ", fieldIds.Select((_, index) => $"$value{index}"))})";
        command.Parameters.AddWithValue("$id", id.ToString("N"));
        var normalized = new Dictionary<Guid, string?>();
        for (var index = 0; index < fieldIds.Length; index++)
        {
            normalized[fieldIds[index]] = FieldValue.Normalize(table.Fields[index].Type, values.GetValueOrDefault(fieldIds[index]));
            command.Parameters.AddWithValue($"$value{index}", normalized[fieldIds[index]] ?? (object)DBNull.Value);
        }
        command.ExecuteNonQuery();
        return new DataRecord(id, normalized);
    }

    public void AddRecords(Guid tableId, IReadOnlyList<IReadOnlyDictionary<Guid, string?>> rows)
    {
        var table = FindTable(tableId);
        var fieldIds = table.Fields.Select(field => field.Id).ToArray();
        foreach (var row in rows)
        {
            if (row.Keys.Except(fieldIds).Any())
                throw new ArgumentException("Запись содержит неизвестное поле.", nameof(rows));
        }
        if (rows.Count == 0)
            return;

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {TableName(tableId)} (id, {string.Join(", ", fieldIds.Select(ColumnName))}) VALUES ($id, {string.Join(", ", fieldIds.Select((_, index) => $"$value{index}"))})";
        var idParameter = command.Parameters.Add("$id", SqliteType.Text);
        var valueParameters = fieldIds.Select((_, index) => command.Parameters.Add($"$value{index}", SqliteType.Text)).ToArray();
        foreach (var row in rows)
        {
            idParameter.Value = Guid.NewGuid().ToString("N");
            for (var index = 0; index < fieldIds.Length; index++)
                valueParameters[index].Value = FieldValue.Normalize(table.Fields[index].Type, row.GetValueOrDefault(fieldIds[index])) ?? (object)DBNull.Value;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void UpdateCell(Guid tableId, Guid recordId, Guid fieldId, string? value)
    {
        var table = FindTable(tableId);
        if (!table.Fields.Any(field => field.Id == fieldId))
            throw new ArgumentException("Поле не найдено.", nameof(fieldId));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {TableName(tableId)} SET {ColumnName(fieldId)} = $value WHERE id = $id";
        command.Parameters.AddWithValue("$value", FieldValue.Normalize(table.Fields.Single(field => field.Id == fieldId).Type, value) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", recordId.ToString("N"));
        if (command.ExecuteNonQuery() != 1)
            throw new ArgumentException("Запись не найдена.", nameof(recordId));
    }

    public void DeleteRecords(Guid tableId, IReadOnlyCollection<Guid> recordIds)
    {
        FindTable(tableId);
        if (recordIds.Count == 0)
            return;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {TableName(tableId)} WHERE id = $id";
        var parameter = command.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in recordIds)
        {
            parameter.Value = id.ToString("N");
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private TableDefinition FindTable(Guid id) =>
        GetTables().FirstOrDefault(table => table.Id == id)
        ?? throw new ArgumentException("Таблица не найдена.", nameof(id));

    private string CreateSafetyBackup(string reason)
    {
        var path = Path.Combine(Path.GetDirectoryName(FilePath)!,
            $"{Path.GetFileNameWithoutExtension(FilePath)}.{reason}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.db");
        CreateBackup(path);
        return path;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = _pooling }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON";
        command.ExecuteNonQuery();
        connection.CreateCollation("UNICODE_NOCASE", (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left, right));
        connection.CreateCollation("SMARTSQL_NUMBER", (left, right) =>
        {
            var leftValid = decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber);
            var rightValid = decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber);
            return leftValid && rightValid
                ? leftNumber.CompareTo(rightNumber)
                : StringComparer.Ordinal.Compare(left, right);
        });
        connection.CreateFunction<string?, string, bool>("unicode_contains", (value, query) =>
            value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
        return connection;
    }

    private static string BuildWhereClause(TableDefinition table, string? search, IReadOnlyList<RecordFilter> filters, SqliteCommand command)
    {
        if (filters.Count > 20)
            throw new ArgumentException("Допускается не более 20 условий фильтрации.", nameof(filters));
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            clauses.Add($"({string.Join(" OR ", table.Fields.Select(field => $"unicode_contains({ColumnName(field.Id)}, $search)"))})");
            command.Parameters.AddWithValue("$search", search.Trim());
        }
        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            var field = table.Fields.FirstOrDefault(item => item.Id == filter.FieldId)
                ?? throw new ArgumentException("Поле фильтра не найдено.", nameof(filters));
            var column = ColumnName(field.Id);
            if (filter.Operator == FilterOperator.IsEmpty)
            {
                clauses.Add($"{column} IS NULL");
                continue;
            }
            if (filter.Operator == FilterOperator.IsNotEmpty)
            {
                clauses.Add($"{column} IS NOT NULL");
                continue;
            }
            if (filter.Operator == FilterOperator.Contains && field.Type != FieldType.Text ||
                filter.Operator is FilterOperator.GreaterThan or FilterOperator.LessThan && field.Type == FieldType.Boolean)
                throw new ArgumentException("Операция не подходит для типа поля.", nameof(filters));
            var value = FieldValue.Normalize(field.Type, filter.Value)
                ?? throw new ArgumentException("Для условия фильтра нужно значение.", nameof(filters));
            var parameter = $"$filter{index}";
            command.Parameters.AddWithValue(parameter, value);
            var collation = field.Type switch
            {
                FieldType.Number => "SMARTSQL_NUMBER",
                FieldType.Text => "UNICODE_NOCASE",
                _ => "BINARY"
            };
            clauses.Add(filter.Operator switch
            {
                FilterOperator.Equals => $"{column} COLLATE {collation} = {parameter}",
                FilterOperator.Contains => $"unicode_contains({column}, {parameter})",
                FilterOperator.GreaterThan => $"{column} COLLATE {collation} > {parameter}",
                FilterOperator.LessThan => $"{column} COLLATE {collation} < {parameter}",
                _ => throw new ArgumentException("Неизвестная операция фильтра.", nameof(filters))
            });
        }
        return clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
    }

    private static string TableName(Guid id) => $"data_{id:N}";
    private static string ColumnName(Guid id) => $"field_{id:N}";
}
