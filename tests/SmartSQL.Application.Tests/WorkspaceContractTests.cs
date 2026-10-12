using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class WorkspaceContractTests
{
    [Xunit.Fact]
    public void ContractIsProviderIndependent()
    {
        Xunit.Assert.DoesNotContain(typeof(IDataWorkspace).GetMethods(), method =>
            method.ReturnType.FullName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Number, "12,50", "12.5")]
    [Xunit.InlineData(FieldType.Date, "21.09.2026", "2026-09-21")]
    [Xunit.InlineData(FieldType.Boolean, "Да", "1")]
    [Xunit.InlineData(FieldType.Boolean, "нет", "0")]
    public void TypedValuesAreNormalized(FieldType type, string input, string expected) =>
        Xunit.Assert.Equal(expected, FieldValue.Normalize(type, input));

    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Number, "двенадцать")]
    [Xunit.InlineData(FieldType.Date, "31.02.2026")]
    [Xunit.InlineData(FieldType.Boolean, "возможно")]
    public void InvalidTypedValuesAreRejected(FieldType type, string input) =>
        Xunit.Assert.Throws<FormatException>(() => FieldValue.Normalize(type, input));

    [Xunit.Fact]
    public void RussianRequestBecomesTypedStructuredFilters()
    {
        var documents = Guid.NewGuid();
        var course = Guid.NewGuid();
        var table = new TableDefinition(Guid.NewGuid(), "Практика",
        [
            new FieldDefinition(documents, "Документы", 0, FieldType.Boolean),
            new FieldDefinition(course, "Курс", 1, FieldType.Number)
        ]);
        var interpreted = NaturalLanguageQuery.Interpret(table,
            "Покажи записи, где Документы = Нет и Курс = 3");

        Xunit.Assert.Equal(2, interpreted.Filters.Count);
        Xunit.Assert.Equal(new RecordFilter(documents, FilterOperator.Equals, "Нет"), interpreted.Filters[0]);
        Xunit.Assert.Equal(new RecordFilter(course, FilterOperator.Equals, "3"), interpreted.Filters[1]);
        Xunit.Assert.Throws<FormatException>(() => NaturalLanguageQuery.Interpret(table, "удали все записи"));
    }

    [Xunit.Fact]
    public void NewTableCsvPreviewValidatesAllRowsBeforeCommit()
    {
        var source = CsvTransfer.InspectNewTable("Имя;Дата\nАнна;21.09.2026\nМария;31.02.2026\n");
        Xunit.Assert.Equal(2, source.RowCount);
        var fields = new[] { new FieldInput("Имя", FieldType.Text), new FieldInput("Дата", FieldType.Date) };

        var error = Xunit.Assert.Throws<FormatException>(() => source.PrepareNewTable("Практика", fields));
        Xunit.Assert.Contains("Строка 3", error.Message);
        Xunit.Assert.Throws<ArgumentException>(() => source.PrepareNewTable("Практика",
            [new FieldInput("Имя", FieldType.Text), new FieldInput("имя", FieldType.Text)]));
    }
}
