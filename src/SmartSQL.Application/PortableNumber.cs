namespace SmartSQL.Application;

/// <summary>Converts portable numeric representations, including exponents, without rounding.</summary>
public static class PortableNumber
{
    public static string Normalize(string value) => FieldValue.NormalizeNumber(value, allowExponent: true);
}
