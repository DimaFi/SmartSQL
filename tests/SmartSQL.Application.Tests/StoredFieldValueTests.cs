using SmartSQL.Domain;

namespace SmartSQL.Application.Tests;

public sealed class StoredFieldValueTests
{
    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Text, "")]
    [Xunit.InlineData(FieldType.Text, "  ")]
    [Xunit.InlineData(FieldType.Number, "1.25e2")]
    [Xunit.InlineData(FieldType.Number, "0.0000000000000000000000000001")]
    [Xunit.InlineData(FieldType.Date, "2024-02-29")]
    [Xunit.InlineData(FieldType.Boolean, "0")]
    [Xunit.InlineData(FieldType.Boolean, "1")]
    public void ExistingRepresentationsAreAcceptedWithoutNormalization(FieldType type, string value)
    {
        FieldValue.ValidateStored(type, value);
        if (type == FieldType.Text && string.IsNullOrWhiteSpace(value))
            Xunit.Assert.Null(FieldValue.Normalize(type, value));
        if (type == FieldType.Number && value.Contains('e'))
            Xunit.Assert.Throws<FormatException>(() => FieldValue.Normalize(type, value));
    }

    [Xunit.Theory]
    [Xunit.InlineData(FieldType.Number, "1e-29")]
    [Xunit.InlineData(FieldType.Number, "1.00000000000000000000000000001")]
    [Xunit.InlineData(FieldType.Number, "")]
    [Xunit.InlineData(FieldType.Date, "2023-02-29")]
    [Xunit.InlineData(FieldType.Date, "29.02.2024")]
    [Xunit.InlineData(FieldType.Boolean, "true")]
    [Xunit.InlineData(FieldType.Boolean, "")]
    public void IncompatibleStoredValuesAreRejected(FieldType type, string value) =>
        Xunit.Assert.Throws<FormatException>(() => FieldValue.ValidateStored(type, value));

    [Xunit.Fact]
    public void NullRemainsValidForEveryDeclaredType()
    {
        foreach (var type in Enum.GetValues<FieldType>()) FieldValue.ValidateStored(type, null);
        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => FieldValue.ValidateStored((FieldType)999, null));
    }
}
