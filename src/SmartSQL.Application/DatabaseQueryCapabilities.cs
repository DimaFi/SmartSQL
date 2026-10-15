using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Operations over provider-confirmed native values; not a replacement for native type metadata.</summary>
public enum DatabaseFilterOperator
{
    Equal, NotEqual, LessThan, LessOrEqual, GreaterThan, GreaterOrEqual,
    Contains, StartsWith, EndsWith, IsNull, IsNotNull
}

public enum DatabaseFilterValueKind { Unsupported, Text, Integer, Number, Date, Boolean }

/// <summary>AND conditions. A value is literal input, never SQL; only null tests omit it.</summary>
public sealed record DatabaseFilter(string ColumnName, DatabaseFilterOperator Operator, string? Value = null)
{
    public const int MaxValueCharacters = 1024;

    public void Validate()
    {
        if (string.IsNullOrEmpty(ColumnName) || ColumnName.Length > 1024)
            throw new ArgumentException("Выберите существующий столбец фильтра.", nameof(ColumnName));
        DatabaseBrowseQuery.ValidateText(ColumnName, nameof(ColumnName));
        if (!Enum.IsDefined(Operator))
            throw new ArgumentException("Неизвестная операция фильтра.", nameof(Operator));
        if (Operator is DatabaseFilterOperator.IsNull or DatabaseFilterOperator.IsNotNull)
        {
            if (Value is not null)
                throw new ArgumentException("Для проверки NULL не указывается значение.", nameof(Value));
        }
        else if (Value is null)
            throw new ArgumentException("Введите значение фильтра; пустая строка отличается от NULL.", nameof(Value));
        if (Value?.Length > MaxValueCharacters)
            throw new ArgumentException($"Значение фильтра ограничено {MaxValueCharacters} символами.", nameof(Value));
        DatabaseBrowseQuery.ValidateText(Value, nameof(Value));
    }
}

public sealed record DatabaseQueryColumn(string Name, DatabaseFilterValueKind ValueKind, bool CanSort,
    IReadOnlyList<DatabaseFilterOperator> Operators, string? Limitation = null);

public sealed record DatabaseQueryCapabilities(DatabaseTableRef Table, IReadOnlyList<DatabaseQueryColumn> Columns,
    IReadOnlyList<DatabaseDiagnostic> Diagnostics);

public static class DatabaseQueryOperations
{
    public static IReadOnlyList<DatabaseFilterOperator> For(DatabaseFilterValueKind kind) => kind switch
    {
        DatabaseFilterValueKind.Text => [DatabaseFilterOperator.Equal, DatabaseFilterOperator.NotEqual,
            DatabaseFilterOperator.Contains, DatabaseFilterOperator.StartsWith, DatabaseFilterOperator.EndsWith,
            DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull],
        DatabaseFilterValueKind.Integer or DatabaseFilterValueKind.Number or DatabaseFilterValueKind.Date =>
            [DatabaseFilterOperator.Equal, DatabaseFilterOperator.NotEqual, DatabaseFilterOperator.LessThan,
            DatabaseFilterOperator.LessOrEqual, DatabaseFilterOperator.GreaterThan, DatabaseFilterOperator.GreaterOrEqual,
            DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull],
        DatabaseFilterValueKind.Boolean => [DatabaseFilterOperator.Equal, DatabaseFilterOperator.NotEqual,
            DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull],
        _ => [DatabaseFilterOperator.IsNull, DatabaseFilterOperator.IsNotNull]
    };
}
