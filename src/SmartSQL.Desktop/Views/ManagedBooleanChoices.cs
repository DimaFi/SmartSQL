namespace SmartSQL.Desktop.Views;

/// <summary>
/// Local managed Boolean presentation choices. The editor operates on the same
/// display values already used by MainWindow; FieldValue performs the existing
/// server-side/workspace validation and normalization on save.
/// </summary>
public sealed record ManagedBooleanChoice(string Caption, string? DisplayValue)
{
    public override string ToString() => Caption;
}

public static class ManagedBooleanChoices
{
    public static readonly ManagedBooleanChoice Yes = new("Да", "Да");
    public static readonly ManagedBooleanChoice No = new("Нет", "Нет");
    public static readonly ManagedBooleanChoice NotSet = new("Не задано", null);

    public static IReadOnlyList<ManagedBooleanChoice> Items { get; } = [Yes, No, NotSet];

    // Do not silently reinterpret unknown persisted/display values as NULL or false.
    public static ManagedBooleanChoice? FromDisplay(string? displayValue) => displayValue switch
    {
        "Да" => Yes,
        "Нет" => No,
        null => NotSet,
        _ => null
    };
}
