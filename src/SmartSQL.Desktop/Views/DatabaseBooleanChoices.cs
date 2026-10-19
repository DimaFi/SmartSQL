using SmartSQL.Application;

namespace SmartSQL.Desktop.Views;

/// <summary>
/// Presentation-only choices for provider-confirmed native Boolean columns.
/// Does not infer a Boolean from an unsupported native representation.
/// All submitted values still pass through DatabaseEditValueValidation.Normalize.
/// </summary>
public sealed record DatabaseBooleanChoice(string Label, string? InputValue)
{
    public override string ToString() => Label;
}

public static class DatabaseBooleanChoices
{
    public static readonly DatabaseBooleanChoice Yes = new("Да", "true");
    public static readonly DatabaseBooleanChoice No = new("Нет", "false");
    public static readonly DatabaseBooleanChoice Null = new("Не задано (NULL)", null);

    private static readonly IReadOnlyList<DatabaseBooleanChoice> Required = [Yes, No];
    private static readonly IReadOnlyList<DatabaseBooleanChoice> Optional = [Yes, No, Null];

    public static IReadOnlyList<DatabaseBooleanChoice> ForColumn(EditableDatabaseColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (column.Kind != DatabaseEditValueKind.Boolean)
            throw new ArgumentException("Поле не является логическим.", nameof(column));
        return column.IsNullable ? Optional : Required;
    }

    public static DatabaseBooleanChoice? FromStored(string? nativeText)
    {
        if (nativeText is null)
            return Null;
        return nativeText.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "да" => Yes,
            "false" or "0" or "нет" => No,
            _ => null
        };
    }

    public static string? NormalizeChoice(EditableDatabaseColumn column, DatabaseBooleanChoice? choice)
    {
        if (choice is null || !ForColumn(column).Contains(choice))
            throw new ArgumentException("Выберите допустимое значение Да, Нет или NULL.", nameof(choice));
        return DatabaseEditValueValidation.Normalize(column, choice.InputValue);
    }
}
