using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class ClipboardTableTransferTests
{
    [Xunit.Fact]
    public void ExcelQuotedTabsMultilineCellsAndEscapedQuotesArePreserved()
    {
        var table = Table(new FieldInput("Имя", FieldType.Text), new FieldInput("Описание", FieldType.Text), new FieldInput("Пусто", FieldType.Text));
        var source = ClipboardTableTransfer.Inspect(table,
            "\uFEFFИмя\tОписание\tПусто\r\nАнна\t\"строка 1\r\nстрока 2\t\"\"цитата\"\"\"\t\r\n", hasHeader: true);
        Xunit.Assert.Equal(1, source.RowCount);
        Xunit.Assert.Equal(3, source.ColumnCount);
        Xunit.Assert.Equal("строка 1\r\nстрока 2\t\"цитата\"", source.SampleRows[0][1]);
        Xunit.Assert.Equal(string.Empty, source.SampleRows[0][2]);
        Xunit.Assert.Equal(3, source.SuggestMapping().Count);
        var preview = source.Prepare(source.SuggestMapping());
        Xunit.Assert.Empty(preview.SkippedColumns);
        Xunit.Assert.Empty(preview.EmptyFields);
    }

    [Xunit.Fact]
    public void DefaultWithoutHeaderRetainsTheFirstDataRowAndSuggestsPositionMapping()
    {
        var table = Table(new FieldInput("Название", FieldType.Text), new FieldInput("Количество", FieldType.Number));
        var source = ClipboardTableTransfer.Inspect(table, "Первая запись\t12,5\nВторая\t0\n");
        Xunit.Assert.False(source.HasHeader);
        Xunit.Assert.Equal(2, source.RowCount);
        Xunit.Assert.Equal("Первая запись", source.SampleRows[0][0]);
        Xunit.Assert.Equal(table.Fields[0].Id, source.SuggestMapping()[0]);
        Xunit.Assert.Equal(table.Fields[1].Id, source.SuggestMapping()[1]);
        Xunit.Assert.Equal(2, source.Prepare(source.SuggestMapping()).RowCount);
    }

    [Xunit.Fact]
    public void ExplicitHeadersSuggestNamesRatherThanPositionsAndExposeOmissions()
    {
        var table = Table(new FieldInput("Имя", FieldType.Text), new FieldInput("Возраст", FieldType.Number), new FieldInput("Дата", FieldType.Date));
        var source = ClipboardTableTransfer.Inspect(table, " возраст \tИМЯ\tПримечание\n20\tАнна\tне импортировать", hasHeader: true);
        var mapping = source.SuggestMapping();
        Xunit.Assert.Equal(table.Fields[1].Id, mapping[0]);
        Xunit.Assert.Equal(table.Fields[0].Id, mapping[1]);
        var preview = source.Prepare(mapping);
        Xunit.Assert.Equal(["Примечание"], preview.SkippedColumns);
        Xunit.Assert.Equal(["Дата"], preview.EmptyFields);
        Xunit.Assert.Equal(["Возраст", "Имя", null], preview.TargetFields);
    }

    [Xunit.Theory]
    [Xunit.InlineData("a\tb\na")]
    [Xunit.InlineData("a\nb\tc")]
    [Xunit.InlineData("\"незакрыто")]
    [Xunit.InlineData("\"ячейка\"продолжение")]
    [Xunit.InlineData("")]
    [Xunit.InlineData("\uFEFF")]
    public void MalformedClipboardDataFailsBeforeItCanBePrepared(string content)
    {
        Xunit.Assert.Throws<FormatException>(() => ClipboardTableTransfer.Inspect(Table(new FieldInput("Имя", FieldType.Text)), content));
    }

    [Xunit.Fact]
    public void QuotesInsideUnquotedTextAndFormulaStringsStayLiteral()
    {
        var table = Table(new FieldInput("Текст", FieldType.Text));
        var source = ClipboardTableTransfer.Inspect(table, "ООО \"Ромашка\"\n=IF(A1=\"x\";1;0)\n");
        Xunit.Assert.Equal("ООО \"Ромашка\"", source.SampleRows[0][0]);
        Xunit.Assert.Equal("=IF(A1=\"x\";1;0)", source.SampleRows[1][0]);
        Xunit.Assert.Equal(2, source.Prepare(source.SuggestMapping()).RowCount);
    }

    [Xunit.Theory]
    [Xunit.InlineData("   ")]
    [Xunit.InlineData("\"\t\"")]
    [Xunit.InlineData("\"\r\n\"")]
    public void WhitespaceOnlyNonemptyTextCannotBeSilentlyConvertedToNull(string content)
    {
        var table = Table(new FieldInput("Текст", FieldType.Text));
        var source = ClipboardTableTransfer.Inspect(table, content);
        var error = Xunit.Assert.Throws<FormatException>(() => source.Prepare(source.SuggestMapping()));
        Xunit.Assert.Contains("Строка 1, поле «Текст»", error.Message);
        Xunit.Assert.Contains("без изменения", error.Message);
        var empty = ClipboardTableTransfer.Inspect(table, "\"\"");
        Xunit.Assert.Equal(1, empty.Prepare(empty.SuggestMapping()).RowCount);
    }

    [Xunit.Fact]
    public void DuplicateOrUnknownMappingIsRejected()
    {
        var table = Table(new FieldInput("Имя", FieldType.Text), new FieldInput("Возраст", FieldType.Number));
        var source = ClipboardTableTransfer.Inspect(table, "Анна\t20");
        Xunit.Assert.Throws<ArgumentException>(() => source.Prepare(new Dictionary<int, Guid>()));
        Xunit.Assert.Throws<ArgumentException>(() => source.Prepare(new Dictionary<int, Guid> { [0] = table.Fields[0].Id, [1] = table.Fields[0].Id }));
        Xunit.Assert.Throws<ArgumentException>(() => source.Prepare(new Dictionary<int, Guid> { [2] = table.Fields[0].Id }));
        Xunit.Assert.Throws<ArgumentException>(() => source.Prepare(new Dictionary<int, Guid> { [0] = Guid.NewGuid() }));
    }

    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Number, "=1+2")]
    [Xunit.InlineData(FieldType.Date, "=TODAY()")]
    [Xunit.InlineData(FieldType.Boolean, "=TRUE()")]
    [Xunit.InlineData(FieldType.Number, "0.123456789012345678901234567891")]
    public void FormulaLikeOrInexactValuesInTypedFieldsAreRejectedAtTheirSourceRow(FieldType type, string value)
    {
        var table = Table(new FieldInput("Значение", type));
        var source = ClipboardTableTransfer.Inspect(table, "Значение\n\n" + value, hasHeader: true);
        var error = Xunit.Assert.Throws<FormatException>(() => source.Prepare(source.SuggestMapping()));
        Xunit.Assert.Contains("Строка 3, поле «Значение»", error.Message);
    }

    [Xunit.Theory]
    [Xunit.InlineData(false)]
    [Xunit.InlineData(true)]
    public void RowLimitAllowsExactlyOneHundredThousandDataRows(bool hasHeader)
    {
        var table = Table(new FieldInput("Имя", FieldType.Text));
        var content = (hasHeader ? "Имя\n" : string.Empty) + string.Concat(Enumerable.Repeat("значение\n", ClipboardTableTransfer.MaxRows));
        var source = ClipboardTableTransfer.Inspect(table, content, hasHeader);
        Xunit.Assert.Equal(100_000, source.RowCount);
        Xunit.Assert.Equal(5, source.SampleRows.Count);
        Xunit.Assert.Throws<FormatException>(() => ClipboardTableTransfer.Inspect(table, content + "ещё", hasHeader));
    }

    [Xunit.Fact]
    public void OversizedClipboardTextIsRejectedWithoutParsing()
    {
        var table = Table(new FieldInput("Имя", FieldType.Text));
        Xunit.Assert.Throws<FormatException>(() => ClipboardTableTransfer.Inspect(table, new string('a', ClipboardTableTransfer.MaxCharacters + 1)));
    }

    [Xunit.Fact]
    public void WideOrDenseRangesAreRejectedInsteadOfAllocatingUnboundedCellArrays()
    {
        var table = Table(new FieldInput("Имя", FieldType.Text));
        var wide = new string('\t', ClipboardTableTransfer.MaxColumns);
        Xunit.Assert.Throws<FormatException>(() => ClipboardTableTransfer.Inspect(table, wide));
        var row = new string('\t', 999) + "\n";
        var dense = string.Concat(Enumerable.Repeat(row, ClipboardTableTransfer.MaxCells / 1000 + 1));
        var error = Xunit.Assert.Throws<FormatException>(() => ClipboardTableTransfer.Inspect(table, dense));
        Xunit.Assert.Contains("ячеек", error.Message);
    }

    [Xunit.Fact]
    public void SourceRetainsItsSchemaWhenTheCallersFieldArrayIsChanged()
    {
        var field = new FieldDefinition(Guid.NewGuid(), "Имя", 0, FieldType.Text);
        var fields = new[] { field };
        var table = new TableDefinition(Guid.NewGuid(), "Данные", fields);
        var source = ClipboardTableTransfer.Inspect(table, "Имя\n=1+2", hasHeader: true);
        fields[0] = field with { Type = FieldType.Number };
        Xunit.Assert.Equal(field.Id, source.SuggestMapping()[0]);
        Xunit.Assert.Equal(1, source.Prepare(source.SuggestMapping()).RowCount);
    }

    private static TableDefinition Table(params FieldInput[] fields) => new(Guid.NewGuid(), "Данные",
        fields.Select((field, index) => new FieldDefinition(Guid.NewGuid(), field.Name, index, field.Type)).ToArray());
}
