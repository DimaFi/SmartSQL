using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure.Tests;

public sealed class ClipboardImportTests
{
    [Xunit.Fact]
    public void TypedClipboardImportAppendsAtomicallyAndCanBeCommittedOnlyOnce()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные",
        [
            new FieldInput("Название", FieldType.Text), new("Количество", FieldType.Number),
            new("Дата", FieldType.Date), new("Готово", FieldType.Boolean)
        ]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "Существующая запись" });
        var source = ClipboardTableTransfer.Inspect(table,
            "Название\tКоличество\tДата\tГотово\r\n  Текст 🌍  \t12,50\t12.10.2026\tДа\r\n=1+2\t-0,0000000000000000000000000001\t\tНет\r\n", hasHeader: true);
        var preview = source.Prepare(source.SuggestMapping());
        Xunit.Assert.Equal(1, workspace.CountRecords(table.Id));
        Xunit.Assert.Equal(2, preview.Commit(workspace));
        Xunit.Assert.Equal(3, workspace.CountRecords(table.Id));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        var text = Xunit.Assert.Single(workspace.GetRecords(table.Id), row => row.Values[table.Fields[0].Id] == "  Текст 🌍  ");
        Xunit.Assert.Equal("12.5", text.Values[table.Fields[1].Id]);
        Xunit.Assert.Equal("2026-10-12", text.Values[table.Fields[2].Id]);
        Xunit.Assert.Equal("1", text.Values[table.Fields[3].Id]);
        var literalFormula = Xunit.Assert.Single(workspace.GetRecords(table.Id), row => row.Values[table.Fields[0].Id] == "=1+2");
        Xunit.Assert.Equal("-0.0000000000000000000000000001", literalFormula.Values[table.Fields[1].Id]);
        Xunit.Assert.Null(literalFormula.Values[table.Fields[2].Id]);
        Xunit.Assert.Equal("0", literalFormula.Values[table.Fields[3].Id]);
    }

    [Xunit.Fact]
    public void InvalidLastRowDoesNotInsertAnyPreviousRows()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", [new FieldInput("Количество", FieldType.Number)]);
        var source = ClipboardTableTransfer.Inspect(table, "12\n15\n=20+1");
        var error = Xunit.Assert.Throws<FormatException>(() => source.Prepare(source.SuggestMapping()));
        Xunit.Assert.Contains("Строка 3", error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void MappedFieldsCanBeReorderedAndUnmappedFieldsStayEmpty()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди",
            [new FieldInput("Имя", FieldType.Text), new("Возраст", FieldType.Number), new("Комментарий", FieldType.Text)]);
        var source = ClipboardTableTransfer.Inspect(table, "20\tАнна\tвнешнее значение");
        var preview = source.Prepare(new Dictionary<int, Guid> { [0] = table.Fields[1].Id, [1] = table.Fields[0].Id });
        Xunit.Assert.Equal(["Столбец 3"], preview.SkippedColumns);
        Xunit.Assert.Equal(["Комментарий"], preview.EmptyFields);
        preview.Commit(workspace);
        var record = Xunit.Assert.Single(workspace.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", record.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("20", record.Values[table.Fields[1].Id]);
        Xunit.Assert.Null(record.Values[table.Fields[2].Id]);
    }

    [Xunit.Fact]
    public void RenamedOrAddedFieldsAfterPreviewPreventInsertion()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", ["Имя"]);
        var preview = Prepare(table, "Анна");
        workspace.UpdateSchema(table.Id, table.Name, new Dictionary<Guid, string> { [table.Fields[0].Id] = "ФИО" });
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        var current = Xunit.Assert.Single(workspace.GetTables());
        var next = Prepare(current, "Анна");
        workspace.AddField(table.Id, new("Возраст", FieldType.Number));
        Xunit.Assert.Throws<InvalidOperationException>(() => next.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void DeletedTableCannotBeReplacedWithTheSameNameDuringPreview()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", ["Имя"]);
        var preview = Prepare(table, "Анна");
        workspace.DeleteTable(table.Id);
        var replacement = workspace.CreateTable(table.Name, ["Имя"]);
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(replacement.Id));
    }

    [Xunit.Fact]
    public async Task ConcurrentConfirmationCannotInsertTheSamePreviewTwice()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", ["Имя"]);
        var preview = Prepare(table, "Анна");
        var confirmations = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try
            {
                return preview.Commit(workspace);
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }));
        var results = await Task.WhenAll(confirmations);
        Xunit.Assert.Equal(1, results.Sum());
        Xunit.Assert.Equal(1, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void DatabaseFailureRollsBackTheBatchAndAllowsRetryWithoutDuplicates()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", ["Имя"]);
        var preview = Prepare(table, "Сохранить\nЗапретить");
        using var connection = new SqliteConnection($"Data Source={workspace.FilePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TRIGGER block_clipboard_row BEFORE INSERT ON data_{table.Id:N} WHEN NEW.field_{table.Fields[0].Id:N} = 'Запретить' BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
        command.ExecuteNonQuery();
        Xunit.Assert.Throws<SqliteException>(() => preview.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
        command.CommandText = "DROP TRIGGER block_clipboard_row";
        command.ExecuteNonQuery();
        Xunit.Assert.Equal(2, preview.Commit(workspace));
        Xunit.Assert.Equal(2, workspace.CountRecords(table.Id));
    }

    private static ClipboardImportPreview Prepare(TableDefinition table, string content)
    {
        var source = ClipboardTableTransfer.Inspect(table, content);
        return source.Prepare(source.SuggestMapping());
    }

    private static SqliteWorkspace NewWorkspace() => SqliteWorkspace.CreateNew(Path.Combine(TestPaths.Root, "smartsql-tests", $"clipboard-{Guid.NewGuid():N}.db"));
}
