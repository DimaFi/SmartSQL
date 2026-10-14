namespace SmartSQL.Domain;

/// <summary>Native database objects, independent of SmartSQL-managed table metadata.</summary>
public enum DatabaseObjectKind
{
    Table,
    View,
    MaterializedView,
    ForeignTable,
    PartitionedTable
}

public sealed record DatabaseTableRef(string Schema, string Name, DatabaseObjectKind Kind);

/// <param name="Ordinal">Zero-based position in the displayed column list.</param>
/// <param name="PrimaryKeyOrder">One-based position in a composite primary key, or null.</param>
public sealed record DatabaseColumn(string Name, int Ordinal, string NativeType,
    bool IsNullable, int? PrimaryKeyOrder);

/// <summary>Display values with an optional transient provider edit handle; never a database row identity.</summary>
public sealed record DatabaseDisplayRow(IReadOnlyList<string?> Values)
{
    /// <summary>Session-only capability for an explicitly enabled editor. Not exported or persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? EditHandle { get; init; }

    public override string ToString() => $"{nameof(DatabaseDisplayRow)} {{ Values = {Values} }}";
}

public sealed record DatabaseDiagnostic(string Code, string Message);

/// <summary>A read-only page whose values follow the order of Columns.</summary>
public sealed record DatabasePage(DatabaseTableRef Table, IReadOnlyList<DatabaseColumn> Columns,
    IReadOnlyList<DatabaseDisplayRow> Rows, long Offset, int PageSize, bool HasMore,
    IReadOnlyList<DatabaseDiagnostic> Diagnostics);
