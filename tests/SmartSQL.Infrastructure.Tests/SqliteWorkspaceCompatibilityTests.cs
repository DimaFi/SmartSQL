using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SmartSQL.Domain;
using SmartSQL.Infrastructure;

namespace SmartSQL.Infrastructure.Tests;

public sealed class SqliteWorkspaceCompatibilityTests
{
    [Xunit.Fact]
    public void CurrentProjectWithPositionGapReopensWithDataAndWithoutUpgradeBackup()
    {
        var fixture = CreateCurrentProject(includeSavedFilter: true);
        Execute(fixture.Path, "UPDATE smartsql_fields SET position = 3 WHERE position = 1");

        var reopened = SqliteWorkspace.OpenExisting(fixture.Path);

        var table = Xunit.Assert.Single(reopened.GetTables());
        Xunit.Assert.Equal([0, 3], table.Fields.Select(field => field.Position).ToArray());
        var record = Xunit.Assert.Single(reopened.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", record.Values[table.Fields[0].Id]);
        Xunit.Assert.Single(reopened.GetSavedFilters(table.Id));
        Xunit.Assert.Empty(UpgradeBackups(fixture.Path));
    }

    [Xunit.Theory]
    [Xunit.InlineData("table")]
    [Xunit.InlineData("field")]
    public void MalformedMetadataGuidIsRejectedWithoutChangingProject(string target)
    {
        var fixture = CreateCurrentProject();
        Execute(fixture.Path, target == "table"
            ? "UPDATE smartsql_tables SET id = 'not-a-guid'"
            : "UPDATE smartsql_fields SET id = 'not-a-guid' WHERE position = 0");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Fact]
    public void OrphanFieldIsRejectedWithoutChangingProject()
    {
        var fixture = CreateCurrentProject();
        Execute(fixture.Path, $"UPDATE smartsql_fields SET table_id = '{Guid.NewGuid():N}' WHERE position = 0");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Theory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void DuplicateOrNegativeFieldPositionsAreRejectedWithoutChangingProject(bool negative)
    {
        var fixture = negative ? CreateCurrentProject() : CreateVersionOneProjectWithDuplicatePositions();
        if (negative)
            Execute(fixture.Path, "UPDATE smartsql_fields SET position = -1 WHERE position = 1");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Fact]
    public void UnknownFieldTypeIsRejectedWithoutChangingProject()
    {
        var fixture = CreateCurrentProject();
        Execute(fixture.Path, "UPDATE smartsql_fields SET field_type = 'Currency' WHERE position = 0");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Theory]
    [Xunit.InlineData("number")]
    [Xunit.InlineData("blob")]
    [Xunit.InlineData("utf8")]
    public void InvalidPhysicalValueIsRejectedWithoutChangingProject(string invalidValue)
    {
        var fixture = CreateCurrentProject();
        var table = $"data_{fixture.TableId:N}";
        Execute(fixture.Path, invalidValue switch
        {
            "number" => $"UPDATE {table} SET field_{fixture.FieldIds[1]:N} = 'not-a-number'",
            "blob" => $"UPDATE {table} SET field_{fixture.FieldIds[1]:N} = x'3132'",
            "utf8" => $"UPDATE {table} SET field_{fixture.FieldIds[0]:N} = CAST(x'C328' AS TEXT)",
            _ => throw new ArgumentOutOfRangeException(nameof(invalidValue))
        });

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Theory]
    [Xunit.InlineData("missing-table")]
    [Xunit.InlineData("missing-field")]
    [Xunit.InlineData("extra-field")]
    public void MismatchedPhysicalTableIsRejectedWithoutChangingProject(string mismatch)
    {
        var fixture = CreateCurrentProject();
        var tableName = $"data_{fixture.TableId:N}";
        var fieldName = $"field_{fixture.FieldIds[0]:N}";
        Execute(fixture.Path, mismatch switch
        {
            "missing-table" => $"DROP TABLE {tableName}",
            "missing-field" => $"ALTER TABLE {tableName} DROP COLUMN {fieldName}",
            "extra-field" => $"ALTER TABLE {tableName} ADD COLUMN unexpected TEXT",
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        });

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Theory]
    [Xunit.InlineData("extra-index")]
    [Xunit.InlineData("nocase")]
    [Xunit.InlineData("replace")]
    public void ChangedMetadataIndexSemanticsAreRejectedWithoutChangingProject(string change)
    {
        var fixture = CreateCurrentProject();
        if (change == "extra-index")
        {
            Execute(fixture.Path, "CREATE INDEX unexpected_table_name_index ON smartsql_tables(name)");
        }
        else
        {
            var nameDefinition = change switch
            {
                "nocase" => "name TEXT NOT NULL COLLATE NOCASE UNIQUE",
                "replace" => "name TEXT NOT NULL UNIQUE ON CONFLICT REPLACE",
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            Execute(fixture.Path, $"""
                PRAGMA legacy_alter_table = ON;
                BEGIN;
                ALTER TABLE smartsql_tables RENAME TO old_smartsql_tables;
                CREATE TABLE smartsql_tables (id TEXT PRIMARY KEY, {nameDefinition});
                INSERT INTO smartsql_tables SELECT id,name FROM old_smartsql_tables;
                DROP TABLE old_smartsql_tables;
                COMMIT;
                """);
        }

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Fact]
    public void MalformedPhysicalRecordGuidIsRejectedWithoutChangingProject()
    {
        var fixture = CreateCurrentProject();
        Execute(fixture.Path, $"UPDATE data_{fixture.TableId:N} SET id = 'not-a-guid'");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Theory]
    [Xunit.InlineData("filter-table")]
    [Xunit.InlineData("item-field")]
    [Xunit.InlineData("operator")]
    public void MalformedSavedFilterIsRejectedWithoutChangingProject(string malformedPart)
    {
        var fixture = CreateCurrentProject(includeSavedFilter: true);
        Execute(fixture.Path, malformedPart switch
        {
            "filter-table" => $"UPDATE smartsql_saved_filters SET table_id = '{Guid.NewGuid():N}'",
            "item-field" => $"UPDATE smartsql_saved_filter_items SET field_id = '{Guid.NewGuid():N}'",
            "operator" => "UPDATE smartsql_saved_filter_items SET operator = 'StartsWith'",
            _ => throw new ArgumentOutOfRangeException(nameof(malformedPart))
        });

        AssertRejectedWithoutChanges(fixture.Path);
    }

    [Xunit.Fact]
    public void ValidVersionOneProjectUpgradesAfterCreatingVerifiedStandaloneCopy()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "legacy.db");
        var tableId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        Execute(path, $"""
            CREATE TABLE smartsql_tables (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE smartsql_fields (id TEXT PRIMARY KEY, table_id TEXT NOT NULL, name TEXT NOT NULL, position INTEGER NOT NULL);
            INSERT INTO smartsql_tables VALUES ('{tableId:N}', 'Люди');
            INSERT INTO smartsql_fields VALUES ('{fieldId:N}', '{tableId:N}', 'Имя', 0);
            CREATE TABLE data_{tableId:N} (id TEXT PRIMARY KEY, field_{fieldId:N} TEXT);
            INSERT INTO data_{tableId:N} VALUES ('{recordId:N}', 'Анна');
            PRAGMA user_version = 1;
            """);

        var upgraded = SqliteWorkspace.OpenExisting(path);

        var table = Xunit.Assert.Single(upgraded.GetTables());
        Xunit.Assert.Equal(FieldType.Text, Xunit.Assert.Single(table.Fields).Type);
        var record = Xunit.Assert.Single(upgraded.GetRecords(table.Id));
        Xunit.Assert.Equal(recordId, record.Id);
        Xunit.Assert.Equal("Анна", record.Values[fieldId]);
        Xunit.Assert.Equal(3, ReadUserVersion(path));

        var backup = Xunit.Assert.Single(UpgradeBackups(path));
        var report = Xunit.Assert.IsType<ManagedProjectUpgrade>(upgraded.Upgrade);
        Xunit.Assert.Equal(1, report.PreviousVersion);
        Xunit.Assert.Equal(3, report.CurrentVersion);
        Xunit.Assert.Equal(Path.GetFullPath(backup), report.BackupPath);
        ClearPool(path);
        Xunit.Assert.NotEqual(Hash(path), Hash(backup));
        Xunit.Assert.Equal(Hash(backup).ToLowerInvariant(), report.BackupSha256);
        Xunit.Assert.Equal(1, ReadUserVersion(backup));
        Xunit.Assert.Equal("ok", ScalarText(backup, "PRAGMA quick_check"));
        Xunit.Assert.Equal("0", ScalarText(backup,
            "SELECT COUNT(*) FROM pragma_table_info('smartsql_fields') WHERE name='field_type'"));
        Xunit.Assert.Equal("Анна", ScalarText(backup,
            $"SELECT field_{fieldId:N} FROM data_{tableId:N} WHERE id='{recordId:N}'"));
    }

    [Xunit.Fact]
    public void VersionOneUpgradeCopiesCommittedWalRecordIntoStandaloneBackup()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "legacy-wal.db");
        var tableId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var firstRecordId = Guid.NewGuid();
        var walRecordId = Guid.NewGuid();
        Execute(path, $"""
            CREATE TABLE smartsql_tables (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE smartsql_fields (id TEXT PRIMARY KEY, table_id TEXT NOT NULL, name TEXT NOT NULL, position INTEGER NOT NULL);
            INSERT INTO smartsql_tables VALUES ('{tableId:N}', 'Люди');
            INSERT INTO smartsql_fields VALUES ('{fieldId:N}', '{tableId:N}', 'Имя', 0);
            CREATE TABLE data_{tableId:N} (id TEXT PRIMARY KEY, field_{fieldId:N} TEXT);
            INSERT INTO data_{tableId:N} VALUES ('{firstRecordId:N}', 'Анна');
            PRAGMA user_version = 1;
            """);
        using var wal = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        wal.Open();
        using (var command = wal.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL";
            Xunit.Assert.Equal("wal", Convert.ToString(command.ExecuteScalar())?.ToLowerInvariant());
            command.CommandText = "PRAGMA wal_autocheckpoint = 0";
            command.ExecuteNonQuery();
            command.CommandText = $"INSERT INTO data_{tableId:N} VALUES ('{walRecordId:N}', 'Мария')";
            command.ExecuteNonQuery();
        }
        Xunit.Assert.True(File.Exists(path + "-wal"));

        var upgraded = SqliteWorkspace.OpenExisting(path);

        Xunit.Assert.Equal(2, upgraded.GetRecords(tableId).Count);
        var report = Xunit.Assert.IsType<ManagedProjectUpgrade>(upgraded.Upgrade);
        Xunit.Assert.Equal(1, report.PreviousVersion);
        Xunit.Assert.Equal(3, report.CurrentVersion);
        Xunit.Assert.Equal(Path.GetFullPath(report.BackupPath), report.BackupPath);
        Xunit.Assert.Equal(Hash(report.BackupPath).ToLowerInvariant(), report.BackupSha256);
        Xunit.Assert.Equal(1, ReadUserVersion(report.BackupPath));
        Xunit.Assert.Equal("Мария", ScalarText(report.BackupPath,
            $"SELECT field_{fieldId:N} FROM data_{tableId:N} WHERE id='{walRecordId:N}'"));
        Xunit.Assert.Equal("ok", ScalarText(report.BackupPath, "PRAGMA quick_check"));
    }

    [Xunit.Fact]
    public void FutureVersionIsRejectedWithoutChangingProject()
    {
        var fixture = CreateCurrentProject();
        Execute(fixture.Path, "PRAGMA user_version = 999");

        AssertRejectedWithoutChanges(fixture.Path);
    }

    private static ProjectFixture CreateCurrentProject(bool includeSavedFilter = false)
    {
        var path = Path.Combine(NewDirectory(), "project.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        var table = workspace.CreateTable("Люди",
            [new FieldInput("Имя", FieldType.Text), new FieldInput("Возраст", FieldType.Number)]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Анна",
            [table.Fields[1].Id] = "20"
        });
        if (includeSavedFilter)
            workspace.SaveFilter(table.Id, "Совершеннолетние",
                [new RecordFilter(table.Fields[1].Id, FilterOperator.GreaterThan, "17")]);
        ClearPool(path);
        return new(path, table.Id, table.Fields.Select(field => field.Id).ToArray());
    }

    private static ProjectFixture CreateVersionOneProjectWithDuplicatePositions()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "project.db");
        var tableId = Guid.NewGuid();
        var firstFieldId = Guid.NewGuid();
        var secondFieldId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        Execute(path, $"""
            CREATE TABLE smartsql_tables (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);
            CREATE TABLE smartsql_fields (
                id TEXT PRIMARY KEY, table_id TEXT NOT NULL, name TEXT NOT NULL,
                position INTEGER NOT NULL);
            INSERT INTO smartsql_tables VALUES ('{tableId:N}', 'Люди');
            INSERT INTO smartsql_fields VALUES
                ('{firstFieldId:N}', '{tableId:N}', 'Имя', 0),
                ('{secondFieldId:N}', '{tableId:N}', 'Возраст', 0);
            CREATE TABLE data_{tableId:N} (
                id TEXT PRIMARY KEY, field_{firstFieldId:N} TEXT, field_{secondFieldId:N} TEXT);
            INSERT INTO data_{tableId:N} VALUES ('{recordId:N}', 'Анна', '20');
            PRAGMA user_version = 1;
            """);
        return new(path, tableId, [firstFieldId, secondFieldId]);
    }

    private static void AssertRejectedWithoutChanges(string path)
    {
        ClearPool(path);
        var hash = Hash(path);
        var schema = ReadSchema(path);
        var version = ReadUserVersion(path);

        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(path));

        ClearPool(path);
        Xunit.Assert.Equal(hash, Hash(path));
        Xunit.Assert.Equal(schema, ReadSchema(path));
        Xunit.Assert.Equal(version, ReadUserVersion(path));
        Xunit.Assert.Empty(UpgradeBackups(path));
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(TestPaths.Root, "smartsql-compatibility-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string[] UpgradeBackups(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.before-upgrade-*.db");

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF";
        command.ExecuteNonQuery();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(string path) => int.Parse(ScalarText(path, "PRAGMA user_version"));

    private static string ScalarText(string path, string sql)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string ReadSchema(string path)
    {
        using var connection = OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_schema ORDER BY type, name";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index) ? "<null>" : reader.GetValue(index))));
        return string.Join("\n", rows);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void ClearPool(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = true
        }.ToString());
        SqliteConnection.ClearPool(connection);
    }

    private sealed record ProjectFixture(string Path, Guid TableId, Guid[] FieldIds);
}
