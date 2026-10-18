using SmartSQL.Application;
using SmartSQL.Desktop.Views;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class DatabaseBooleanChoicesTests
{
    private static EditableDatabaseColumn Column(bool nullable = true, bool canEdit = true) =>
        new("approved", 0, "boolean", DatabaseEditValueKind.Boolean, canEdit, nullable);

    [Theory]
    [InlineData("true", "true")]
    [InlineData("TRUE", "true")]
    [InlineData("1", "true")]
    [InlineData("Да", "true")]
    [InlineData("false", "false")]
    [InlineData("FALSE", "false")]
    [InlineData("0", "false")]
    [InlineData("Нет", "false")]
    public void RecognizesOnlyVerifiedHumanOrNormalizedBooleanValues(string stored, string normalized)
    {
        var choice = DatabaseBooleanChoices.FromStored(stored);
        Assert.NotNull(choice);
        Assert.Equal(normalized, DatabaseBooleanChoices.NormalizeChoice(Column(), choice));
    }

    [Fact]
    public void NullableColumnHasThreeChoicesIncludingRealNull()
    {
        var choices = DatabaseBooleanChoices.ForColumn(Column());
        Assert.Equal(new[] { "Да", "Нет", "Не задано (NULL)" }, choices.Select(x => x.Label));
        Assert.Null(DatabaseBooleanChoices.NormalizeChoice(Column(), DatabaseBooleanChoices.Null));
        Assert.Same(DatabaseBooleanChoices.Null, DatabaseBooleanChoices.FromStored(null));
    }

    [Fact]
    public void RequiredColumnHasNoNullAndCannotBeSubmittedAsNull()
    {
        var required = Column(nullable: false);
        Assert.Equal(2, DatabaseBooleanChoices.ForColumn(required).Count);
        Assert.DoesNotContain(DatabaseBooleanChoices.Null, DatabaseBooleanChoices.ForColumn(required));
        Assert.Throws<ArgumentException>(() => DatabaseBooleanChoices.NormalizeChoice(required, DatabaseBooleanChoices.Null));
        Assert.Equal("false", DatabaseBooleanChoices.NormalizeChoice(required, DatabaseBooleanChoices.No));
    }

    [Theory]
    [InlineData("")]
    [InlineData("t")]
    [InlineData("f")]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("unexpected")]
    public void UnknownNativeRepresentationIsNotSilentlyConvertedToFalseOrNull(string raw) =>
        Assert.Null(DatabaseBooleanChoices.FromStored(raw));

    [Fact]
    public void MissingOrForeignChoiceCannotBeSubmitted()
    {
        Assert.Throws<ArgumentException>(() => DatabaseBooleanChoices.NormalizeChoice(Column(), null));
        Assert.Throws<ArgumentException>(() =>
            DatabaseBooleanChoices.NormalizeChoice(Column(), new DatabaseBooleanChoice("bad", "true")));
    }

    [Fact]
    public void ReadOnlyColumnsStayReadOnlyEvenWhenUiOffersBooleanDisplay()
    {
        Assert.Throws<ArgumentException>(() =>
            DatabaseBooleanChoices.NormalizeChoice(Column(canEdit: false), DatabaseBooleanChoices.Yes));
    }

    [Fact]
    public void NonBooleanColumnsCannotReceiveTheseOptions()
    {
        var text = Column() with { Kind = DatabaseEditValueKind.Text };
        Assert.Throws<ArgumentException>(() => DatabaseBooleanChoices.ForColumn(text));
    }
}
