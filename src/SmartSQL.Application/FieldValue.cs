using System.Globalization;
using SmartSQL.Domain;

namespace SmartSQL.Application;

public static class FieldValue
{
    public static string? Normalize(FieldType type, string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
            return null;
        if (type == FieldType.Text)
            return value;

        var input = value.Trim();
        switch (type)
        {
            case FieldType.Number:
            {
                var normalized = input.Replace(',', '.');
                return NormalizeNumber(normalized);
            }
            case FieldType.Date:
            {
                if (!DateTime.TryParseExact(input, ["yyyy-MM-dd", "dd.MM.yyyy"],
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new FormatException("Ожидается дата в формате ДД.ММ.ГГГГ или ГГГГ-ММ-ДД.");
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            case FieldType.Boolean:
                return input.ToLowerInvariant() switch
                {
                    "да" or "true" or "1" => "1",
                    "нет" or "false" or "0" => "0",
                    _ => throw new FormatException("Ожидается Да или Нет.")
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(type), "Неизвестный тип поля.");
        }
    }

    // JSON numbers (and values stored by older versions) can use an exponent.
    // Ordinary text input deliberately keeps the existing decimal-only grammar.
    internal static string NormalizeNumber(string input, bool allowExponent = false)
    {
        var styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (allowExponent)
            styles |= NumberStyles.AllowExponent;
        if (!decimal.TryParse(input, styles, CultureInfo.InvariantCulture, out var number))
            throw new FormatException("Ожидается число в пределах точности decimal, например 12,5.");

        var result = number.ToString("0.############################", CultureInfo.InvariantCulture);
        if (NumericIdentity(input) != NumericIdentity(result))
            throw new FormatException("Число невозможно сохранить точно: превышена точность decimal.");
        return result;
    }

    private static (bool Negative, string Digits, long Power) NumericIdentity(string input)
    {
        var negative = input[0] == '-';
        if (input[0] is '-' or '+')
            input = input[1..];
        var exponentIndex = input.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0)
        {
            if (!int.TryParse(input.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out exponent))
                throw new FormatException("Показатель степени числа слишком велик.");
            input = input[..exponentIndex];
        }
        var decimalIndex = input.IndexOf('.');
        var fractionLength = decimalIndex < 0 ? 0 : input.Length - decimalIndex - 1;
        var digits = input.Replace(".", string.Empty).TrimStart('0');
        if (digits.Length == 0)
            return (false, "0", 0);
        var significant = digits.TrimEnd('0');
        return (negative, significant, (long)exponent - fractionLength + digits.Length - significant.Length);
    }
}
