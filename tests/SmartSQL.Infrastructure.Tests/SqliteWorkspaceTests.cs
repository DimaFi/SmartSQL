using SmartSQL.Infrastructure;
using SmartSQL.Application;
using SmartSQL.Domain;
using Microsoft.Data.Sqlite;

namespace SmartSQL.Infrastructure.Tests;

public sealed class SqliteWorkspaceTests
{
    [Xunit.Fact]
    public void TableAndRecordSurviveReopen()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["ФИО", "Группа"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Анна Иванова",
            [table.Fields[1].Id] = "3 курс"
        });

        var reopened = SqliteWorkspace.OpenExisting(file);
        var loadedTable = Xunit.Assert.Single(reopened.GetTables());
        Xunit.Assert.Equal("Студенты", loadedTable.Name);
        var record = Xunit.Assert.Single(reopened.GetRecords(loadedTable.Id));
        Xunit.Assert.Equal("Анна Иванова", record.Values[loadedTable.Fields[0].Id]);
        Xunit.Assert.Equal("3 курс", record.Values[loadedTable.Fields[1].Id]);
    }

    [Xunit.Fact]
    public void CsvCanCreateTableAndRowsAtomically()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var source = CsvTransfer.InspectNewTable("Имя;Курс\nАнна;3\nИлья;4\n");
        var preview = source.PrepareNewTable("Студенты",
            [new FieldInput("Имя", FieldType.Text), new FieldInput("Курс", FieldType.Number)]);

        var table = preview.Commit(workspace);
        Xunit.Assert.Equal(2, workspace.CountRecords(table.Id));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));

        Xunit.Assert.Throws<FormatException>(() => workspace.CreateTableWithRecords("Неполная",
            [new FieldInput("Курс", FieldType.Number)],
            [new string?[] { "3" }, new string?[] { "ошибка" }]));
        Xunit.Assert.DoesNotContain(workspace.GetTables(), item => item.Name == "Неполная");
    }

    [Xunit.Fact]
    public void JsonRoundTripPreservesTypedValuesAndRejectsSchemaMismatch()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var fields = new[]
        {
            new FieldInput("Имя", FieldType.Text),
            new FieldInput("Курс", FieldType.Number),
            new FieldInput("Дата", FieldType.Date),
            new FieldInput("Документы", FieldType.Boolean)
        };
        var source = workspace.CreateTable("Источник", fields);
        workspace.AddRecord(source.Id, new Dictionary<Guid, string?>
        {
            [source.Fields[0].Id] = "Анна",
            [source.Fields[1].Id] = "3,5",
            [source.Fields[2].Id] = "21.09.2026",
            [source.Fields[3].Id] = "Да"
        });
        using var output = new MemoryStream();
        Xunit.Assert.Equal(1, JsonTableTransfer.Export(workspace, source.Id, output));
        var json = System.Text.Encoding.UTF8.GetString(output.ToArray());
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Xunit.Assert.Equal("Курс", document.RootElement.GetProperty("fields")[1].GetProperty("name").GetString());
        Xunit.Assert.Contains("3.5", json);

        var target = workspace.CreateTable("Приёмник", fields);
        var preview = JsonTableTransfer.PrepareImport(workspace, target.Id, json);
        Xunit.Assert.Equal(1, preview.Commit(workspace));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        var record = Xunit.Assert.Single(workspace.GetRecords(target.Id));
        Xunit.Assert.Equal("3.5", record.Values[target.Fields[1].Id]);
        Xunit.Assert.Equal("2026-09-21", record.Values[target.Fields[2].Id]);
        Xunit.Assert.Equal("1", record.Values[target.Fields[3].Id]);

        var incompatible = workspace.CreateTable("Несовместимая", [new FieldInput("Имя", FieldType.Text)]);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, incompatible.Id, json));
        Xunit.Assert.Equal(0, workspace.CountRecords(incompatible.Id));
    }

    [Xunit.Fact]
    public void JsonRejectsInvalidValueBeforeImport()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Данные", [new FieldInput("Курс", FieldType.Number)]);
        const string json = """
            {"format":"SmartSQL.Table.v1","table":"Данные","fields":[{"name":"Курс","type":"Number"}],"records":[[3],["ошибка"]]}
            """;
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id, json));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void NamesCannotInjectSql()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        workspace.CreateTable("'; DROP TABLE smartsql_tables; --", ["Имя; DELETE FROM smartsql_fields"]);
        Xunit.Assert.Single(workspace.GetTables());
    }

    [Xunit.Fact]
    public void BackupIsReadableAndDoesNotOverwriteExistingFile()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var workspace = SqliteWorkspace.CreateNew(Path.Combine(directory, "main.db"));
        var table = workspace.CreateTable("Студенты", ["ФИО"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Анна" });
        var backupPath = Path.Combine(directory, "backup.db");
        workspace.CreateBackup(backupPath);

        var backup = SqliteWorkspace.OpenExisting(backupPath);
        Xunit.Assert.Equal("Анна", Xunit.Assert.Single(backup.GetRecords(table.Id)).Values[table.Fields[0].Id]);
        Xunit.Assert.Throws<IOException>(() => workspace.CreateBackup(backupPath));
    }

    [Xunit.Fact]
    public void UpgradesOldDatabaseWithBackup()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "legacy.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        workspace.CreateTable("Студенты", ["Имя"]);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 0";
            command.ExecuteNonQuery();
        }

        var upgraded = SqliteWorkspace.OpenExisting(path);
        Xunit.Assert.Single(upgraded.GetTables());
        Xunit.Assert.Single(Directory.GetFiles(directory, "legacy.before-upgrade-*.db"));
    }

    [Xunit.Fact]
    public void RecordsCanBePagedEditedAndDeleted()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Практики", ["Студент"]);
        var first = workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Анна" });
        var second = workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Мария" });

        Xunit.Assert.Equal(2, workspace.CountRecords(table.Id));
        Xunit.Assert.Equal(second.Id, Xunit.Assert.Single(workspace.GetRecords(table.Id, 0, 1)).Id);
        workspace.UpdateCell(table.Id, first.Id, table.Fields[0].Id, "Анна Иванова");
        Xunit.Assert.Equal("Анна Иванова", Xunit.Assert.Single(workspace.GetRecords(table.Id, 1, 1)).Values[table.Fields[0].Id]);
        workspace.DeleteRecords(table.Id, [second.Id]);
        Xunit.Assert.Equal(first.Id, Xunit.Assert.Single(workspace.GetRecords(table.Id)).Id);
    }

    [Xunit.Fact]
    public void SearchAndSortWorkWithCyrillicText()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["ФИО"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Яна" });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Анна" });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Антон" });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Мария" });

        Xunit.Assert.Equal(2, workspace.CountRecords(table.Id, "АН"));
        var sorted = workspace.QueryRecords(table.Id, 0, 10, null, table.Fields[0].Id, false);
        Xunit.Assert.Equal(["Анна", "Антон", "Мария", "Яна"],
            sorted.Select(record => record.Values[table.Fields[0].Id]!).ToArray());
    }

    [Xunit.Fact]
    public void RejectsUnrelatedOrNewerDatabaseWithoutChangingIt()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var unrelated = Path.Combine(directory, "unrelated.db");
        using (var connection = new SqliteConnection($"Data Source={unrelated}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE unrelated (id INTEGER)";
            command.ExecuteNonQuery();
        }
        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(unrelated));

        var newer = Path.Combine(directory, "newer.db");
        SqliteWorkspace.CreateNew(newer);
        using (var connection = new SqliteConnection($"Data Source={newer}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 999";
            command.ExecuteNonQuery();
        }
        Xunit.Assert.Throws<InvalidOperationException>(() => SqliteWorkspace.OpenExisting(newer));
    }

    [Xunit.Fact]
    public void CsvImportMapsHeadersAndCommitsAllRows()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["ФИО", "Группа"]);
        using var reader = new StringReader("\uFEFFГруппа;ФИО\r\n3 курс;\"Анна; Иванова\"\r\n4 курс;\"Мария\nПетрова\"\r\n");
        Xunit.Assert.Equal(2, CsvTransfer.Import(workspace, table.Id, reader));
        var records = workspace.GetRecords(table.Id);
        Xunit.Assert.Equal("Анна; Иванова", records[1].Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("Мария\nПетрова", records[0].Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("4 курс", records[0].Values[table.Fields[1].Id]);
    }

    [Xunit.Fact]
    public void CsvImportRejectsBadRowsWithoutPartialWrite()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["ФИО", "Группа"]);
        using var reader = new StringReader("ФИО,Группа\nАнна,3\nМария\n");
        Xunit.Assert.Throws<FormatException>(() => CsvTransfer.Import(workspace, table.Id, reader));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void CsvExportCanProtectExcelFromFormulaCells()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["ФИО"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "=1+1" });
        using var writer = new StringWriter();
        Xunit.Assert.Equal(1, CsvTransfer.Export(workspace, table.Id, writer, spreadsheetSafe: true));
        Xunit.Assert.Contains("'=1+1", writer.ToString());
    }

    [Xunit.Fact]
    public void TypedFieldsValidatePersistAndSortNumbers()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Практики",
            [new FieldInput("Баллы", FieldType.Number), new FieldInput("Дата", FieldType.Date), new FieldInput("Документы", FieldType.Boolean)]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "10,5", [table.Fields[1].Id] = "21.09.2026", [table.Fields[2].Id] = "Да"
        });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "2" });
        var reopened = SqliteWorkspace.OpenExisting(file);
        var loaded = Xunit.Assert.Single(reopened.GetTables());
        Xunit.Assert.Equal(FieldType.Date, loaded.Fields[1].Type);
        var sorted = reopened.QueryRecords(table.Id, 0, 10, null, table.Fields[0].Id, false);
        Xunit.Assert.Equal("2", sorted[0].Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("10.5", sorted[1].Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("2026-09-21", sorted[1].Values[table.Fields[1].Id]);
        Xunit.Assert.Equal("1", sorted[1].Values[table.Fields[2].Id]);
        Xunit.Assert.Throws<FormatException>(() => reopened.UpdateCell(table.Id, sorted[0].Id, table.Fields[1].Id, "31.02.2026"));
        Xunit.Assert.Equal("2", reopened.GetRecords(table.Id)[0].Values[table.Fields[0].Id]);
    }

    [Xunit.Fact]
    public void VersionOneDatabaseMigratesFieldsToTextAndCreatesBackup()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "old.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE smartsql_tables (id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE);
                CREATE TABLE smartsql_fields (id TEXT PRIMARY KEY, table_id TEXT NOT NULL, name TEXT NOT NULL, position INTEGER NOT NULL);
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO smartsql_tables VALUES ('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'Люди'); INSERT INTO smartsql_fields VALUES ('bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'Имя', 0)";
            command.ExecuteNonQuery();
        }
        var workspace = SqliteWorkspace.OpenExisting(path);
        Xunit.Assert.Equal(3, workspace.SchemaVersion);
        Xunit.Assert.Equal(FieldType.Text, Xunit.Assert.Single(workspace.GetTables()[0].Fields).Type);
        Xunit.Assert.Single(Directory.GetFiles(directory, "old.before-upgrade-*.db"));
    }

    [Xunit.Fact]
    public void AddingTypedFieldPreservesExistingRecords()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var record = workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Анна" });
        var date = workspace.AddField(table.Id, new FieldInput("Дата", FieldType.Date));
        workspace.UpdateCell(table.Id, record.Id, date.Id, "21.09.2026");

        var reopened = SqliteWorkspace.OpenExisting(file);
        Xunit.Assert.Equal(2, reopened.GetTables()[0].Fields.Count);
        var loaded = Xunit.Assert.Single(reopened.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", loaded.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("2026-09-21", loaded.Values[date.Id]);
        Xunit.Assert.Throws<ArgumentException>(() => reopened.AddField(table.Id, new FieldInput("дата", FieldType.Text)));
    }

    [Xunit.Fact]
    public void InvalidTypedCsvRowRollsBackEntireImport()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Оценки", [new FieldInput("Балл", FieldType.Number)]);
        using var reader = new StringReader("Балл\n5\nне число\n");
        Xunit.Assert.Throws<FormatException>(() => CsvTransfer.Import(workspace, table.Id, reader));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void FieldFiltersCombineWithSearchAndCompareTypedValues()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Практики",
            [new FieldInput("Студент", FieldType.Text), new FieldInput("Балл", FieldType.Number), new FieldInput("Дата", FieldType.Date)]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Анна", [table.Fields[1].Id] = "10", [table.Fields[2].Id] = "2026-09-21"
        });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Антон", [table.Fields[1].Id] = "2", [table.Fields[2].Id] = "2026-09-20"
        });
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Мария" });

        RecordFilter[] filters =
        [
            new(table.Fields[1].Id, FilterOperator.GreaterThan, "9"),
            new(table.Fields[2].Id, FilterOperator.IsNotEmpty, null)
        ];
        Xunit.Assert.Equal(1, workspace.CountRecords(table.Id, "АН", filters));
        var result = Xunit.Assert.Single(workspace.QueryRecords(table.Id, 0, 10, "АН", null, false, filters));
        Xunit.Assert.Equal("Анна", result.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal(1, workspace.CountRecords(table.Id, null,
            [new RecordFilter(table.Fields[2].Id, FilterOperator.LessThan, "21.09.2026")]));
        Xunit.Assert.Throws<ArgumentException>(() => workspace.CountRecords(table.Id, null,
            [new RecordFilter(table.Fields[1].Id, FilterOperator.Contains, "1")]));
    }

    [Xunit.Fact]
    public void CsvPreviewDoesNotWriteUntilCommitted()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Люди", ["Имя"]);
        using var reader = new StringReader("Имя\nАнна\nМария\n");
        var preview = CsvTransfer.PrepareImport(workspace, table.Id, reader);
        Xunit.Assert.Equal(2, preview.RowCount);
        Xunit.Assert.Equal("Анна", preview.SampleRows[0][0]);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
        Xunit.Assert.Equal(2, preview.Commit(workspace));
        Xunit.Assert.Equal(2, workspace.CountRecords(table.Id));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
    }

    [Xunit.Fact]
    public void CsvPreviewReportsTypedErrorBeforeWrite()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Оценки", [new FieldInput("Балл", FieldType.Number)]);
        using var reader = new StringReader("Балл\n5\nошибка\n");
        var error = Xunit.Assert.Throws<FormatException>(() => CsvTransfer.PrepareImport(workspace, table.Id, reader));
        Xunit.Assert.Contains("Строка 3", error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void CsvColumnsCanBeMappedInDifferentOrderWithExplicitOmissions()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Студенты", ["Имя", "Группа", "Телефон"]);
        var source = CsvTransfer.Inspect(table, "Курс;Студент;Лишнее\n3;Анна;не импортировать\n");
        var preview = source.Prepare(new Dictionary<int, Guid>
        {
            [0] = table.Fields[1].Id,
            [1] = table.Fields[0].Id
        });
        Xunit.Assert.Equal(';', source.Delimiter);
        Xunit.Assert.Equal(["Лишнее"], preview.SkippedColumns);
        Xunit.Assert.Equal(["Телефон"], preview.EmptyFields);
        Xunit.Assert.Equal(["Группа", "Имя", null], preview.TargetFields);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
        preview.Commit(workspace);
        var record = Xunit.Assert.Single(workspace.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", record.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("3", record.Values[table.Fields[1].Id]);
        Xunit.Assert.Null(record.Values[table.Fields[2].Id]);
    }

    [Xunit.Fact]
    public void CsvMappingRejectsDuplicateTargetField()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var source = CsvTransfer.Inspect(table, "Первый,Второй\nАнна,Мария\n", ',');
        Xunit.Assert.Throws<ArgumentException>(() => source.Prepare(new Dictionary<int, Guid>
        {
            [0] = table.Fields[0].Id,
            [1] = table.Fields[0].Id
        }));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void TableAndFieldsCanBeRenamedWithoutChangingRecordsOrTypes()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Люди",
            [new FieldInput("Имя", FieldType.Text), new FieldInput("Возраст", FieldType.Number)]);
        var record = workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Анна", [table.Fields[1].Id] = "20"
        });
        var renamed = workspace.UpdateSchema(table.Id, "Студенты", new Dictionary<Guid, string>
        {
            [table.Fields[0].Id] = "ФИО", [table.Fields[1].Id] = "Полных лет"
        });
        Xunit.Assert.Equal("Студенты", renamed.Name);
        Xunit.Assert.Equal(["ФИО", "Полных лет"], renamed.Fields.Select(field => field.Name).ToArray());
        Xunit.Assert.Equal(FieldType.Number, renamed.Fields[1].Type);
        var loaded = Xunit.Assert.Single(SqliteWorkspace.OpenExisting(file).GetRecords(table.Id));
        Xunit.Assert.Equal(record.Id, loaded.Id);
        Xunit.Assert.Equal("Анна", loaded.Values[table.Fields[0].Id]);
        Xunit.Assert.Throws<ArgumentException>(() => workspace.UpdateSchema(table.Id, "Студенты",
            new Dictionary<Guid, string> { [table.Fields[0].Id] = "Одинаково", [table.Fields[1].Id] = "одинаково" }));
    }

    [Xunit.Fact]
    public void SavedFiltersPersistCanBeReplacedAndDeleted()
    {
        var file = Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db");
        var workspace = SqliteWorkspace.CreateNew(file);
        var table = workspace.CreateTable("Люди", [new FieldInput("Возраст", FieldType.Number)]);
        var saved = workspace.SaveFilter(table.Id, "Совершеннолетние",
            [new RecordFilter(table.Fields[0].Id, FilterOperator.GreaterThan, "17")]);
        var reopened = SqliteWorkspace.OpenExisting(file);
        var loaded = Xunit.Assert.Single(reopened.GetSavedFilters(table.Id));
        Xunit.Assert.Equal(saved.Id, loaded.Id);
        Xunit.Assert.Equal("17", loaded.Filters[0].Value);

        var replaced = reopened.SaveFilter(table.Id, "совершеннолетние",
            [new RecordFilter(table.Fields[0].Id, FilterOperator.GreaterThan, "18")]);
        Xunit.Assert.Equal(saved.Id, replaced.Id);
        Xunit.Assert.Equal("18", Xunit.Assert.Single(reopened.GetSavedFilters(table.Id)).Filters[0].Value);
        reopened.DeleteSavedFilter(table.Id, saved.Id);
        Xunit.Assert.Empty(reopened.GetSavedFilters(table.Id));
        Xunit.Assert.Throws<ArgumentException>(() => reopened.DeleteSavedFilter(table.Id, saved.Id));
    }

    [Xunit.Fact]
    public void VersionTwoDatabaseMigratesSavedFilterStorageWithBackup()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "version2.db");
        var workspace = SqliteWorkspace.CreateNew(path);
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE smartsql_saved_filter_items; DROP TABLE smartsql_saved_filters; PRAGMA user_version = 2";
            command.ExecuteNonQuery();
        }
        var upgraded = SqliteWorkspace.OpenExisting(path);
        Xunit.Assert.Equal(3, upgraded.SchemaVersion);
        Xunit.Assert.Single(Directory.GetFiles(directory, "version2.before-upgrade-*.db"));
    }

    [Xunit.Fact]
    public void FieldDeletionCreatesBackupAndRemovesAffectedSavedFilters()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var workspace = SqliteWorkspace.CreateNew(Path.Combine(directory, "main.db"));
        var table = workspace.CreateTable("Люди", ["Имя", "Телефон"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?>
        {
            [table.Fields[0].Id] = "Анна", [table.Fields[1].Id] = "+70000000000"
        });
        workspace.SaveFilter(table.Id, "С телефоном",
            [new RecordFilter(table.Fields[1].Id, FilterOperator.IsNotEmpty, null)]);
        var impact = workspace.GetFieldDeletionImpact(table.Id, table.Fields[1].Id);
        Xunit.Assert.Equal(1, impact.RecordCount);
        Xunit.Assert.Equal(1, impact.AffectedSavedFilterCount);

        var result = workspace.DeleteField(table.Id, table.Fields[1].Id);
        Xunit.Assert.True(File.Exists(result.BackupPath));
        Xunit.Assert.Empty(workspace.GetSavedFilters(table.Id));
        Xunit.Assert.Single(workspace.GetTables()[0].Fields);
        var record = Xunit.Assert.Single(workspace.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", record.Values[table.Fields[0].Id]);
        var backup = SqliteWorkspace.OpenExisting(result.BackupPath);
        Xunit.Assert.Equal(2, backup.GetTables()[0].Fields.Count);
    }

    [Xunit.Fact]
    public void TableDeletionCreatesRecoverableBackupAndRemovesTable()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var workspace = SqliteWorkspace.CreateNew(Path.Combine(directory, "main.db"));
        var table = workspace.CreateTable("Люди", ["Имя"]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Анна" });
        var impact = workspace.GetTableDeletionImpact(table.Id);
        Xunit.Assert.Equal(1, impact.RecordCount);
        var result = workspace.DeleteTable(table.Id);
        Xunit.Assert.Empty(workspace.GetTables());
        Xunit.Assert.Single(SqliteWorkspace.OpenExisting(result.BackupPath).GetRecords(table.Id));
    }

    [Xunit.Fact]
    public void RestoreReplacesDatabaseAfterValidationAndPreservesCurrentSafetyCopy()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var active = SqliteWorkspace.CreateNew(Path.Combine(directory, "active.db"));
        var oldTable = active.CreateTable("Текущая", ["Значение"]);
        active.AddRecord(oldTable.Id, new Dictionary<Guid, string?> { [oldTable.Fields[0].Id] = "до" });
        var source = SqliteWorkspace.CreateNew(Path.Combine(directory, "backup.db"));
        var restoredTable = source.CreateTable("Из копии", ["Значение"]);
        source.AddRecord(restoredTable.Id, new Dictionary<Guid, string?> { [restoredTable.Fields[0].Id] = "после" });

        var result = active.RestoreBackup(source.FilePath);
        Xunit.Assert.True(File.Exists(result.SafetyBackupPath));
        Xunit.Assert.Equal("Из копии", Xunit.Assert.Single(active.GetTables()).Name);
        Xunit.Assert.Equal("после", Xunit.Assert.Single(active.GetRecords(restoredTable.Id)).Values[restoredTable.Fields[0].Id]);
        var safety = SqliteWorkspace.OpenExisting(result.SafetyBackupPath);
        Xunit.Assert.Equal("Текущая", Xunit.Assert.Single(safety.GetTables()).Name);
        Xunit.Assert.Equal("Из копии", Xunit.Assert.Single(source.GetTables()).Name);
    }

    [Xunit.Fact]
    public void RestoreRejectsUnrelatedDatabaseWithoutChangingCurrentData()
    {
        var directory = Path.Combine(TestPaths.Root, "smartsql-tests", Guid.NewGuid().ToString("N"));
        var active = SqliteWorkspace.CreateNew(Path.Combine(directory, "active.db"));
        var table = active.CreateTable("Люди", ["Имя"]);
        var unrelatedPath = Path.Combine(directory, "other.db");
        using (var connection = new SqliteConnection($"Data Source={unrelatedPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE other(id INTEGER)";
            command.ExecuteNonQuery();
        }
        Xunit.Assert.Throws<InvalidOperationException>(() => active.RestoreBackup(unrelatedPath));
        Xunit.Assert.Equal(table.Id, Xunit.Assert.Single(active.GetTables()).Id);
        Xunit.Assert.Empty(Directory.GetFiles(directory, "active.before-restore-*.db"));
    }
}
