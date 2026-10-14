using System.Text.Json;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Application.Tests;

public sealed class DatabaseEditingTests
{
    private static readonly DatabaseTableRef Table = new("данные клиентов", "заказы.with.dots", DatabaseObjectKind.Table);

    private static EditableDatabaseColumn Column(DatabaseEditValueKind kind, bool nullable = true,
        int? maxTextCharacters = null) => new("значение", 0, "provider-native-type", kind, true, nullable, maxTextCharacters);

    [Fact]
    public void TextPreservesWhitespaceEmptyAndNullAsDifferentValues()
    {
        var column = Column(DatabaseEditValueKind.Text);
        string[] values = ["", " ", "  текст\t\r\n", "%_\\'\" 🌍", "e\u0301"];
        foreach (var value in values)
            Assert.Equal(value, DatabaseEditValueValidation.Normalize(column, value));
        Assert.Null(DatabaseEditValueValidation.Normalize(column, null));
        Assert.Equal(string.Empty, DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Text, nullable: false), string.Empty));
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Text, nullable: false), null));
    }

    [Fact]
    public void NativeTextLimitCountsUnicodeScalarsAndNeverTruncates()
    {
        var column = Column(DatabaseEditValueKind.Text, maxTextCharacters: 2);
        Assert.Equal("🌍🧪", DatabaseEditValueValidation.Normalize(column, "🌍🧪"));
        Assert.Equal("e\u0301", DatabaseEditValueValidation.Normalize(column, "e\u0301"));
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(column, "🌍🧪a"));
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Text, maxTextCharacters: 1), "e\u0301"));
    }

    [Fact]
    public void FiniteNumbersRetainAllDigitsSignsAndExponentWithoutBoundedParsing()
    {
        var decimalColumn = Column(DatabaseEditValueKind.Decimal);
        string[] exact = ["+0001234567890123456789012345678901234567890.000000000000000000000000000001",
            "-0.00000000000000000000000000000000000000000000000001", "1E+100000", ".25", "1."];
        foreach (var value in exact)
            Assert.Equal(value, DatabaseEditValueValidation.Normalize(decimalColumn, value));
        var longDigits = new string('9', DatabaseEditValueValidation.MaxDecimalCharacters);
        Assert.Equal(longDigits, DatabaseEditValueValidation.Normalize(decimalColumn, longDigits));
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(decimalColumn, longDigits + "9"));

        var largeInteger = "+00012345678901234567890123456789012345678901234567890";
        Assert.Equal(largeInteger, DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Integer), largeInteger));
        Assert.Equal("-0", DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Integer), "-0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,25")]
    [InlineData("1 000")]
    [InlineData("1_000")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("+Inf")]
    [InlineData("−1")]
    [InlineData("١٢")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e2e3")]
    [InlineData(".")]
    [InlineData("--1")]
    [InlineData("0x10")]
    public void DecimalRejectsInputsThatCouldCauseImplicitLossOrReinterpretation(string value) =>
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Decimal), value));

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("１２")]
    [InlineData("−1")]
    public void IntegerRejectsNonAsciiOrNonIntegerSyntax(string value) =>
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Integer), value));

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("2024-02-29")]
    [InlineData("9999-12-31")]
    public void DateAcceptsOnlyExistingExactIsoDates(string value) =>
        Assert.Equal(value, DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Date), value));

    [Theory]
    [InlineData("2023-02-29")]
    [InlineData("2024-02-30")]
    [InlineData("2024-2-01")]
    [InlineData("01.02.2024")]
    [InlineData("2024-01-01T00:00:00")]
    [InlineData(" 2024-01-01")]
    [InlineData("0000-01-01")]
    [InlineData("２０２４-01-01")]
    public void DateRejectsAmbiguousInvalidOrTimeBearingInputs(string value) =>
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Date), value));

    [Theory]
    [InlineData("Да", "true")]
    [InlineData("ДА", "true")]
    [InlineData(" true ", "true")]
    [InlineData("1", "true")]
    [InlineData("Нет", "false")]
    [InlineData("нет", "false")]
    [InlineData("FALSE", "false")]
    [InlineData("0", "false")]
    public void BooleanNormalizesExplicitRussianAndInvariantChoices(string value, string expected) =>
        Assert.Equal(expected, DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Boolean), value));

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("00")]
    public void BooleanDoesNotInferMissingOrOtherValues(string value) =>
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Boolean), value));

    [Fact]
    public void AllKindsRejectNulMalformedUnicodeAndOverlongInputsBeforeProviderWork()
    {
        string[] malformed = ["a\0b", new string((char)0xD800, 1), new string((char)0xDC00, 1),
            new string(new[] { (char)0xD800, 'a' }), new string(new[] { 'a', (char)0xDC00 })];
        foreach (var kind in Enum.GetValues<DatabaseEditValueKind>())
        foreach (var value in malformed)
            Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(kind), value));

        var boundary = new string('a', DatabaseEditValueValidation.MaxValueCharacters);
        Assert.Equal(boundary, DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Text), boundary));
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(DatabaseEditValueKind.Text), boundary + "a"));
        Assert.Throws<ArgumentException>(() => new DatabaseCellUpdate(Table, Guid.NewGuid(), "значение", boundary + "a").Validate());
    }

    [Fact]
    public void ReadOnlyAndUnknownColumnKindsCannotBypassValidationWithNull()
    {
        var readOnly = Column(DatabaseEditValueKind.Text) with { CanEdit = false, ReadOnlyReason = "Ключевые поля доступны только для чтения." };
        var error = Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(readOnly, null));
        Assert.Contains("Ключевые поля", error.Message);
        Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column((DatabaseEditValueKind)999), null));
        foreach (var kind in Enum.GetValues<DatabaseEditValueKind>())
        {
            Assert.Null(DatabaseEditValueValidation.Normalize(Column(kind), null));
            Assert.Throws<ArgumentException>(() => DatabaseEditValueValidation.Normalize(Column(kind, nullable: false), null));
        }
    }

    [Fact]
    public void RequestsRequireTransientHandleAndNativeIdentifiersWithoutInventingSqlRules()
    {
        var request = new DatabaseCellUpdate(Table, Guid.NewGuid(), "Ключ 🌍.with dots \"quotes\"", " ");
        request.Validate();
        (request with { ColumnName = " " }).Validate(); // Quoted whitespace names are legitimate native identifiers.
        Assert.Throws<ArgumentException>(() => (request with { EditHandle = Guid.Empty }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { ColumnName = string.Empty }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { ColumnName = "a\0b" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { ColumnName = new string((char)0xD800, 1) }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Table = Table with { Schema = string.Empty } }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Table = Table with { Name = "a\0b" } }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Table = Table with { Kind = (DatabaseObjectKind)999 } }).Validate());
    }

    [Fact]
    public void DisplayRowsStayBackwardCompatibleAndEditCapabilitiesAreNotSerialized()
    {
        var ordinary = new DatabaseDisplayRow(["exact", null, string.Empty]);
        Assert.Null(ordinary.EditHandle);
        ordinary.Deconstruct(out var values);
        Assert.Same(ordinary.Values, values);
        var editable = ordinary with { EditHandle = Guid.NewGuid() };
        Assert.DoesNotContain(editable.EditHandle.Value.ToString(), editable.ToString());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(editable));
        Assert.False(json.RootElement.TryGetProperty(nameof(DatabaseDisplayRow.EditHandle), out _));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty(nameof(DatabaseDisplayRow.Values))[1].ValueKind);
        Assert.Equal(string.Empty, json.RootElement.GetProperty(nameof(DatabaseDisplayRow.Values))[2].GetString());
        Assert.Equal(ordinary.Values, JsonSerializer.Deserialize<DatabaseDisplayRow>(JsonSerializer.Serialize(editable))!.Values);
        Assert.True(typeof(IDatabaseBrowser).IsAssignableFrom(typeof(IDatabaseEditor)));
        Assert.Equal(typeof(bool), typeof(IDatabaseEditor).GetProperty(nameof(IDatabaseEditor.EditingEnabled))!.PropertyType);
    }

    [Fact]
    public void UpdateRequestPrintingDoesNotExposeHandleOrCellContent()
    {
        var request = new DatabaseCellUpdate(Table, Guid.NewGuid(), "note", "private cell content");
        Assert.DoesNotContain(request.EditHandle.ToString(), request.ToString());
        Assert.DoesNotContain(request.Value!, request.ToString());
    }
}
