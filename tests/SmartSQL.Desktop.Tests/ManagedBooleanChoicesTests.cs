using SmartSQL.Application;
using SmartSQL.Desktop.Views;
using SmartSQL.Domain;
using Xunit;

namespace SmartSQL.Desktop.Tests;

public sealed class ManagedBooleanChoicesTests
{
    [Theory]
    [InlineData("Да", "1")]
    [InlineData("Нет", "0")]
    public void ChoiceProducesExistingManagedBooleanRepresentation(string display, string expected)
    {
        var option = ManagedBooleanChoices.FromDisplay(display);

        Assert.NotNull(option);
        Assert.Equal(display, option.DisplayValue);
        Assert.Equal(expected, FieldValue.Normalize(FieldType.Boolean, option.DisplayValue));
    }

    [Fact]
    public void EmptyValueIsNullAndNotFalse()
    {
        var option = ManagedBooleanChoices.FromDisplay(null);

        Assert.Same(ManagedBooleanChoices.NotSet, option);
        Assert.Null(option!.DisplayValue);
        Assert.Null(FieldValue.Normalize(FieldType.Boolean, option.DisplayValue));
        Assert.NotEqual(ManagedBooleanChoices.No, option);
    }

    [Fact]
    public void UnknownValueDoesNotBecomeNoOrNull()
    {
        Assert.Null(ManagedBooleanChoices.FromDisplay(""));
        Assert.Null(ManagedBooleanChoices.FromDisplay("true"));
        Assert.Null(ManagedBooleanChoices.FromDisplay("0"));
        Assert.Null(ManagedBooleanChoices.FromDisplay("несовместимое значение"));
    }

    [Fact]
    public void OptionsAreDistinctAndHumanReadable()
    {
        Assert.Equal(new[] { "Да", "Нет", "Не задано" }, ManagedBooleanChoices.Items.Select(x => x.Caption));
        Assert.Equal(new string?[] { "Да", "Нет", null }, ManagedBooleanChoices.Items.Select(x => x.DisplayValue));
        Assert.Equal(3, ManagedBooleanChoices.Items.Distinct().Count());
    }

    [Fact]
    public void ExistingEditableCellInvokesWriteOnlyOnActualChange()
    {
        var calls = new List<(string? Old, string? New)>();
        var cell = new EditableCell("Нет", (oldValue, newValue) => calls.Add((oldValue, newValue)), _ => { });

        cell.Value = ManagedBooleanChoices.No.DisplayValue;
        Assert.Empty(calls);

        cell.Value = ManagedBooleanChoices.Yes.DisplayValue;
        Assert.Single(calls);
        Assert.Equal(("Нет", "Да"), calls[0]);
        Assert.Equal("Да", cell.Value);

        cell.Value = ManagedBooleanChoices.NotSet.DisplayValue;
        Assert.Equal(2, calls.Count);
        Assert.Equal(("Да", (string?)null), calls[1]);
        Assert.Null(cell.Value);
    }

    [Fact]
    public void FailedSaveLeavesExistingCellValueUnchanged()
    {
        string? report = null;
        var cell = new EditableCell("Да",
            (_, _) => throw new InvalidOperationException("synthetic write failure"),
            message => report = message);

        cell.Value = "Нет";

        Assert.Equal("Да", cell.Value);
        Assert.Contains("synthetic write failure", report);
    }
}
