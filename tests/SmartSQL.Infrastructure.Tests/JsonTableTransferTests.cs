using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure.Tests;

public sealed class JsonTableTransferTests
{
    [Xunit.Fact]
    public void TypedRoundTripPreservesNullUnicodeAndWhitespaceInsideText()
    {
        var workspace = NewWorkspace();
        FieldInput[] fields =
        [
            new("Название", FieldType.Text), new("Цена", FieldType.Number),
            new("Дата", FieldType.Date), new("Доступен", FieldType.Boolean)
        ];
        var source = workspace.CreateTable("Источник", fields);
        workspace.AddRecord(source.Id, new Dictionary<Guid, string?>
        {
            [source.Fields[0].Id] = "  Текст\nс \"кавычками\" и 🌍  ",
            [source.Fields[1].Id] = "-0,0000000000000000000000000001",
            [source.Fields[2].Id] = "29.02.2024", [source.Fields[3].Id] = "Нет"
        });
        workspace.AddRecord(source.Id, new Dictionary<Guid, string?>());
        var json = Export(workspace, source.Id);
        using var document = JsonDocument.Parse(json);
        var serialized = document.RootElement.GetProperty("records")[1];
        Xunit.Assert.Equal(JsonValueKind.Number, serialized[1].ValueKind);
        Xunit.Assert.Equal(JsonValueKind.False, serialized[3].ValueKind);

        var target = workspace.CreateTable("Приёмник", fields);
        var preview = JsonTableTransfer.PrepareImport(workspace, target.Id, "\uFEFF" + json);
        Xunit.Assert.Equal(2, preview.RowCount);
        Xunit.Assert.Equal(0, workspace.CountRecords(target.Id));
        Xunit.Assert.Equal(2, preview.Commit(workspace));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        var records = workspace.GetRecords(target.Id);
        var text = Xunit.Assert.Single(records, row => row.Values[target.Fields[0].Id] is not null);
        Xunit.Assert.Equal("  Текст\nс \"кавычками\" и 🌍  ", text.Values[target.Fields[0].Id]);
        Xunit.Assert.Equal("-0.0000000000000000000000000001", text.Values[target.Fields[1].Id]);
        Xunit.Assert.Equal("2024-02-29", text.Values[target.Fields[2].Id]);
        Xunit.Assert.Equal("0", text.Values[target.Fields[3].Id]);
        Xunit.Assert.All(Xunit.Assert.Single(records, row => row.Id != text.Id).Values.Values, Xunit.Assert.Null);
    }

    [Xunit.Theory]
    [Xunit.InlineData("1e-28", "0.0000000000000000000000000001")]
    [Xunit.InlineData("1.25E+3", "1250")]
    [Xunit.InlineData("-2.5e-3", "-0.0025")]
    [Xunit.InlineData("7.9228162514264337593543950335e28", "79228162514264337593543950335")]
    [Xunit.InlineData("0e-100", "0")]
    public void JsonExponentNumbersAreImportedExactly(string input, string expected)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Числа", [new FieldInput("Значение", FieldType.Number)]);
        var preview = JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Значение\",\"type\":\"Number\"}]", $"[[{input}]]"));
        preview.Commit(workspace);
        Xunit.Assert.Equal(expected, Xunit.Assert.Single(workspace.GetRecords(table.Id)).Values[table.Fields[0].Id]);
        using var roundTrip = JsonDocument.Parse(Export(workspace, table.Id));
        Xunit.Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture),
            roundTrip.RootElement.GetProperty("records")[0][0].GetDecimal());
    }

    [Xunit.Theory]
    [Xunit.InlineData("1e-29")]
    [Xunit.InlineData("1e99")]
    [Xunit.InlineData("79228162514264337593543950336")]
    [Xunit.InlineData("0.123456789012345678901234567891")]
    [Xunit.InlineData("7.9228162514264337593543950336")]
    public void NumbersThatWouldOverflowUnderflowOrRoundAreRejectedBeforeWrite(string input)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Числа", [new FieldInput("Значение", FieldType.Number)]);
        var error = Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Значение\",\"type\":\"Number\"}]", $"[[1],[{input}]]")));
        Xunit.Assert.Contains("Запись 2", error.Message);
        Xunit.Assert.Contains("Значение", error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Theory]
    [Xunit.InlineData("0.0000000000000000000000000001")]
    [Xunit.InlineData("-0,0000000000000000000000000001")]
    [Xunit.InlineData("+001.23000")]
    [Xunit.InlineData("79228162514264337593543950335")]
    public void NumberNormalizationCanBeRepeatedWithoutChangingOrRejectingTheValue(string input)
    {
        var normalized = FieldValue.Normalize(FieldType.Number, input);
        Xunit.Assert.Equal(normalized, FieldValue.Normalize(FieldType.Number, normalized));
        Xunit.Assert.DoesNotContain('E', normalized!);
        Xunit.Assert.DoesNotContain('e', normalized!);
    }

    [Xunit.Theory]
    [Xunit.InlineData("1e-3")]
    [Xunit.InlineData("0.00000000000000000000000000001")]
    [Xunit.InlineData("0.123456789012345678901234567891")]
    public void OrdinaryNumberInputDoesNotAcceptExponentOrSilentlyLosePrecision(string input)
    {
        Xunit.Assert.Throws<FormatException>(() => FieldValue.Normalize(FieldType.Number, input));
    }

    [Xunit.Fact]
    public void ExportCanReadSmallNumbersSavedByEarlierVersions()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Числа", [new FieldInput("Значение", FieldType.Number)]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = "1" });
        using var connection = new SqliteConnection($"Data Source={workspace.FilePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE data_{table.Id:N} SET field_{table.Fields[0].Id:N} = '1E-28'";
        command.ExecuteNonQuery();
        using var document = JsonDocument.Parse(Export(workspace, table.Id));
        Xunit.Assert.Equal(0.0000000000000000000000000001m,
            document.RootElement.GetProperty("records")[0][0].GetDecimal());
    }

    [Xunit.Fact]
    public void FieldsAreMappedByNameAndTypeRatherThanSourcePosition()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди",
            [new FieldInput("Имя", FieldType.Text), new FieldInput("Возраст", FieldType.Number)]);
        var preview = JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"возраст\",\"type\":\"Number\"},{\"name\":\"имя\",\"type\":\"Text\"}]", "[[20,\"Анна\"]]"));
        Xunit.Assert.Equal(["возраст", "имя"], preview.Headers);
        preview.Commit(workspace);
        var record = Xunit.Assert.Single(workspace.GetRecords(table.Id));
        Xunit.Assert.Equal("Анна", record.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("20", record.Values[table.Fields[1].Id]);
    }

    [Xunit.Fact]
    public void SupportedTypedStringValuesRemainCompatible()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные",
            [new FieldInput("Число", FieldType.Number), new FieldInput("Дата", FieldType.Date), new FieldInput("Флаг", FieldType.Boolean)]);
        var preview = JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Число\",\"type\":\"Number\"},{\"name\":\"Дата\",\"type\":\"Date\"},{\"name\":\"Флаг\",\"type\":\"Boolean\"}]",
                "[[\"3,5\",\"12.10.2026\",\"Да\"]]"));
        preview.Commit(workspace);
        var record = Xunit.Assert.Single(workspace.GetRecords(table.Id));
        Xunit.Assert.Equal("3.5", record.Values[table.Fields[0].Id]);
        Xunit.Assert.Equal("2026-10-12", record.Values[table.Fields[1].Id]);
        Xunit.Assert.Equal("1", record.Values[table.Fields[2].Id]);
    }

    [Xunit.Theory]
    [Xunit.InlineData("[[\"Анна\"],[]]")]
    [Xunit.InlineData("[[\"Анна\"],[\"Мария\",\"Лишнее\"]]")]
    [Xunit.InlineData("[[\"Анна\"],{\"Имя\":\"Мария\"}]")]
    public void MalformedRecordShapesDoNotImportEvenTheValidRows(string records)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var error = Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Имя\",\"type\":\"Text\"}]", records)));
        Xunit.Assert.Contains("Запись 2", error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Theory]
    [Xunit.InlineData("[{\"name\":\"Имя\",\"type\":\"Text\",\"type\":\"Number\"}]")]
    [Xunit.InlineData("[{\"name\":\"Имя\",\"type\":\"Text\",\"required\":true}]")]
    [Xunit.InlineData("[{\"name\":\"Имя\",\"type\":\"0\"}]")]
    [Xunit.InlineData("[{\"name\":\"Имя\",\"type\":\"text\"}]")]
    [Xunit.InlineData("[{\"name\":\"Имя\",\"type\":\"Number\"}]")]
    [Xunit.InlineData("[{\"name\":\"Другое\",\"type\":\"Text\"}]")]
    [Xunit.InlineData("[]")]
    public void IncompatibleFieldDescriptionsAreRejected(string fields)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id, Document(fields, "[]")));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Theory]
    [Xunit.InlineData(",\"records\":[[\"перезапись\"]]")]
    [Xunit.InlineData(",\"format\":\"SmartSQL.Table.v1\"")]
    [Xunit.InlineData(",\"relationships\":[]")]
    [Xunit.InlineData(",\"Table\":\"Люди\"")]
    public void DuplicateAndUnknownRootPropertiesAreRejected(string additionalProperty)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Имя\",\"type\":\"Text\"}]", "[[\"Анна\"]]", additionalProperty)));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void DuplicateFieldNamesIgnoringCaseAreRejected()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя", "Фамилия"]);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Имя\",\"type\":\"Text\"},{\"name\":\"имя\",\"type\":\"Text\"}]", "[]")));
    }

    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Text, "true")]
    [Xunit.InlineData(FieldType.Text, "12")]
    [Xunit.InlineData(FieldType.Text, "[]")]
    [Xunit.InlineData(FieldType.Text, "\"\"")]
    [Xunit.InlineData(FieldType.Text, "\"   \"")]
    [Xunit.InlineData(FieldType.Number, "true")]
    [Xunit.InlineData(FieldType.Number, "\"1e-3\"")]
    [Xunit.InlineData(FieldType.Boolean, "1")]
    [Xunit.InlineData(FieldType.Boolean, "\"возможно\"")]
    [Xunit.InlineData(FieldType.Date, "20261012")]
    [Xunit.InlineData(FieldType.Date, "\"31.02.2026\"")]
    public void InvalidValuesAreRejectedWithTheirRowAndField(FieldType type, string value)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", [new FieldInput("Значение", type)]);
        var error = Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document($"[{{\"name\":\"Значение\",\"type\":\"{type}\"}}]", $"[[null],[{value}]]")));
        Xunit.Assert.Contains("Запись 2, поле «Значение»", error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Theory]
    [Xunit.InlineData("")]
    [Xunit.InlineData("  ")]
    [Xunit.InlineData("{")]
    [Xunit.InlineData("{\"format\":\"SmartSQL.Table.v2\",\"fields\":[],\"records\":[]}")]
    [Xunit.InlineData("[]")]
    public void InvalidDocumentsProduceFormatErrors(string content)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id, content));
    }

    [Xunit.Fact]
    public void SchemaChangesAfterPreviewPreventAnyImport()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var content = Document("[{\"name\":\"Имя\",\"type\":\"Text\"}]", "[[\"Анна\"]]");
        var renamedPreview = JsonTableTransfer.PrepareImport(workspace, table.Id, content);
        workspace.UpdateSchema(table.Id, table.Name, new Dictionary<Guid, string> { [table.Fields[0].Id] = "ФИО" });
        Xunit.Assert.Throws<InvalidOperationException>(() => renamedPreview.Commit(workspace));
        var addedPreview = JsonTableTransfer.PrepareImport(workspace, table.Id, content.Replace("Имя", "ФИО"));
        workspace.AddField(table.Id, new FieldInput("Возраст", FieldType.Number));
        Xunit.Assert.Throws<InvalidOperationException>(() => addedPreview.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Fact]
    public void DeletedTableAfterPreviewCannotBeReplacedByAnotherTableWithTheSameName()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var preview = JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document("[{\"name\":\"Имя\",\"type\":\"Text\"}]", "[[\"Анна\"]]"));
        workspace.DeleteTable(table.Id);
        var replacement = workspace.CreateTable("Люди", ["Имя"]);
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(replacement.Id));
    }

    [Xunit.Fact]
    public void ExportIncludesRowsBeyondTheFirstPage()
    {
        var workspace = NewWorkspace();
        var source = workspace.CreateTable("Источник", ["Имя"]);
        workspace.AddRecords(source.Id, Enumerable.Range(0, 503)
            .Select(index => (IReadOnlyDictionary<Guid, string?>)new Dictionary<Guid, string?>
            {
                [source.Fields[0].Id] = $"строка {index}"
            }).ToArray());
        var target = workspace.CreateTable("Приёмник", ["Имя"]);
        var preview = JsonTableTransfer.PrepareImport(workspace, target.Id, Export(workspace, source.Id));
        Xunit.Assert.Equal(503, preview.Commit(workspace));
        Xunit.Assert.Equal(503, workspace.GetRecords(target.Id).Select(row => row.Values[target.Fields[0].Id]).Distinct().Count());
    }

    [Xunit.Fact]
    public void RowLimitAllowsTheBoundaryAndRejectsTheNextRowBeforeWrite()
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        var records = "[" + string.Join(',', Enumerable.Repeat("[null]", 100_000)) + "]";
        var fields = "[{\"name\":\"Имя\",\"type\":\"Text\"}]";
        var preview = JsonTableTransfer.PrepareImport(workspace, table.Id, Document(fields, records));
        Xunit.Assert.Equal(100_000, preview.RowCount);
        Xunit.Assert.Equal(5, preview.SampleRows.Count);
        Xunit.Assert.Throws<FormatException>(() => JsonTableTransfer.PrepareImport(workspace, table.Id,
            Document(fields, records[..^1] + ",[null]]")));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    private static SqliteWorkspace NewWorkspace() => SqliteWorkspace.CreateNew(Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db"));

    private static string Document(string fields, string records, string additionalProperty = "") =>
        $"{{\"format\":\"SmartSQL.Table.v1\",\"fields\":{fields},\"records\":{records}{additionalProperty}}}";

    private static string Export(SqliteWorkspace workspace, Guid tableId)
    {
        using var output = new MemoryStream();
        var count = JsonTableTransfer.Export(workspace, tableId, output);
        Xunit.Assert.Equal(workspace.CountRecords(tableId), count);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
