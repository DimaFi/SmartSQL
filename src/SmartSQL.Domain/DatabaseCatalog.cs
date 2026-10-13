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

/// <summary>Display values only. Null is distinct from an empty string; no row identity is invented.</summary>
public sealed record DatabaseDisplayRow(IReadOnlyList<string?> Values);

public sealed record DatabaseDiagnostic(string Code, string Message);

/// <summary>A read-only page whose values follow the order of Columns.</summary>
public sealed record DatabasePage(DatabaseTableRef Table, IReadOnlyList<DatabaseColumn> Columns,
    IReadOnlyList<DatabaseDisplayRow> Rows, long Offset, int PageSize, bool HasMore,
    IReadOnlyList<DatabaseDiagnostic> Diagnostics);
