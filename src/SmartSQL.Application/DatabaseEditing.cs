using System.Globalization;
using System.Text.RegularExpressions;
using SmartSQL.Domain;

namespace SmartSQL.Application;

public enum DatabaseEditValueKind
{
    Text,
    Integer,
    Decimal,
    Date,
    Boolean
}

/// <summary>Provider-confirmed editing rules; unsupported, key and generated columns stay read-only.</summary>
public sealed record EditableDatabaseColumn(string Name, int Ordinal, string NativeType,
    DatabaseEditValueKind Kind, bool CanEdit, bool IsNullable,
    int? MaxTextCharacters = null, string? ReadOnlyReason = null);

public sealed record DatabaseEditCapabilities(DatabaseTableRef Table, bool CanUpdate,
    IReadOnlyList<EditableDatabaseColumn> Columns, IReadOnlyList<DatabaseDiagnostic> Diagnostics);

/// <summary>One cell addressed by a transient provider handle, never by its displayed position.</summary>
public sealed record DatabaseCellUpdate(DatabaseTableRef Table, Guid EditHandle, string ColumnName, string? Value)
{
    public override string ToString() => $"{nameof(DatabaseCellUpdate)} {{ Table = {Table}, ColumnName = {ColumnName} }}";

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Table);
        if (EditHandle == Guid.Empty)
            throw new ArgumentException("Выберите строку текущей страницы для изменения.", nameof(EditHandle));
        ValidateIdentifier(Table.Schema, nameof(Table.Schema));
        ValidateIdentifier(Table.Name, nameof(Table.Name));
        ValidateIdentifier(ColumnName, nameof(ColumnName));
        if (!Enum.IsDefined(Table.Kind))
            throw new ArgumentException("Неизвестный вид объекта базы данных.", nameof(Table));
        DatabaseEditValueValidation.ValidateText(Value, nameof(Value));
    }

    private static void ValidateIdentifier(string? value, string parameter)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Имя объекта базы данных не может быть пустым.", parameter);
        DatabaseEditValueValidation.ValidateText(value, parameter);
    }
}

/// <summary>The submitted handle is consumed on success. Reload the page before another edit.</summary>
public sealed record DatabaseUpdateResult(DatabaseDisplayRow Row, string? Message = null);

/// <summary>Lossless provider-neutral input validation. No SQL or bounded numeric conversion is performed.</summary>
public static class DatabaseEditValueValidation
{
    /// <summary>Maximum input length in UTF-16 code units, matching the bounded display-value contract.</summary>
    public const int MaxValueCharacters = 1_000_000;
    public const int MaxDecimalCharacters = 1024;
    private static readonly Regex FiniteDecimal = new(
        @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string? Normalize(EditableDatabaseColumn column, string? value)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (!column.CanEdit)
            throw new ArgumentException(column.ReadOnlyReason ?? "Этот столбец доступен только для чтения.", nameof(column));
        if (!Enum.IsDefined(column.Kind))
            throw new ArgumentException("Этот тип значения не поддерживает редактирование.", nameof(column));

        var characterCount = ValidateText(value, nameof(value));
        if (value is null)
        {
            if (!column.IsNullable)
                throw new ArgumentException("Этот столбец не допускает NULL. Укажите значение.", nameof(value));
            return null;
        }

        switch (column.Kind)
        {
            case DatabaseEditValueKind.Text:
                if (column.MaxTextCharacters is < 0)
                    throw new ArgumentException("Некорректное ограничение длины столбца.", nameof(column));
                if (column.MaxTextCharacters is { } maximum && characterCount > maximum)
                    throw new ArgumentException($"Текст ограничен {maximum} символами. Значение не было обрезано.", nameof(value));
                return value;

            case DatabaseEditValueKind.Integer:
                if (!IsInteger(value))
                    throw new ArgumentException("Укажите целое число: необязательный знак и цифры от 0 до 9.", nameof(value));
                return value;

            case DatabaseEditValueKind.Decimal:
                if (value.Length > MaxDecimalCharacters)
                    throw new ArgumentException($"Числовая запись ограничена {MaxDecimalCharacters} символами.", nameof(value));
                if (!FiniteDecimal.IsMatch(value))
                    throw new ArgumentException("Укажите конечное число с точкой; допустима экспонента e или E. Точность не изменяется.", nameof(value));
                return value;

            case DatabaseEditValueKind.Date:
                if (!IsIsoDate(value))
                    throw new ArgumentException("Укажите существующую дату в формате ГГГГ-ММ-ДД.", nameof(value));
                return value;

            case DatabaseEditValueKind.Boolean:
                var boolean = value.Trim();
                if (boolean.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || boolean.Equals("да", StringComparison.OrdinalIgnoreCase) || boolean == "1")
                    return "true";
                if (boolean.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || boolean.Equals("нет", StringComparison.OrdinalIgnoreCase) || boolean == "0")
                    return "false";
                throw new ArgumentException("Укажите Да или Нет, true или false, 1 или 0.", nameof(value));

            default:
                throw new ArgumentException("Этот тип значения не поддерживает редактирование.", nameof(column));
        }
    }

    // Counts Unicode scalar values: an astral character occupies two UTF-16 units but one varchar character.
    internal static int ValidateText(string? value, string parameter)
    {
        if (value is null)
            return 0;
        if (value.Length > MaxValueCharacters)
            throw new ArgumentException($"Значение ограничено {MaxValueCharacters} единицами UTF-16. Данные не были обрезаны.", parameter);
        var characters = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\0')
                throw new ArgumentException("Текст содержит недопустимый нулевой символ.", parameter);
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                index++;
            else if (char.IsSurrogate(value[index]))
                throw new ArgumentException("Текст содержит некорректный Unicode.", parameter);
            characters++;
        }
        return characters;
    }

    private static bool IsInteger(string value)
    {
        var firstDigit = value.Length > 0 && value[0] is '+' or '-' ? 1 : 0;
        if (firstDigit == value.Length)
            return false;
        for (var index = firstDigit; index < value.Length; index++)
            if (value[index] is < '0' or > '9')
                return false;
        return true;
    }

    private static bool IsIsoDate(string value)
    {
        if (value.Length != 10 || value[4] != '-' || value[7] != '-')
            return false;
        for (var index = 0; index < value.Length; index++)
            if (index != 4 && index != 7 && value[index] is < '0' or > '9')
                return false;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
    }

}
