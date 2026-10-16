using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Completeness within the explicitly described selected-object scope.</summary>
public enum DatabaseMetadataCoverage { Complete, Partial, Unavailable }
public enum DatabaseConstraintKind { PrimaryKey, Unique, ForeignKey, Check, Exclusion, NotNull, Other }
public enum DatabaseIndexTermKind { Column, Expression, RowIdentifier, Other }

/// <summary>Ordered mappings keep omitted/unknown referenced column names as null.</summary>
public sealed record DatabaseForeignKey(string? ReferencedSchema, string ReferencedTable,
    IReadOnlyList<string?> ReferencedColumns, string? OnUpdate, string? OnDelete, string? Match);

/// <summary>NativeDefinition is opaque display text; it never grants execution or editing rights.</summary>
public sealed record DatabaseConstraint(string? Name, DatabaseConstraintKind Kind, string NativeKind,
    IReadOnlyList<string> Columns, string? NativeDefinition = null, DatabaseForeignKey? ForeignKey = null,
    bool? IsDeferrable = null, bool? IsInitiallyDeferred = null, bool? IsValidated = null);

/// <summary>Ordinal is one-based; IsKey distinguishes key terms from auxiliary/included terms.</summary>
public sealed record DatabaseIndexTerm(int Ordinal, DatabaseIndexTermKind Kind, string? ColumnName,
    string? NativeDefinition, bool IsKey, bool? Descending = null, string? Collation = null);

public sealed record DatabaseIndex(string Name, bool IsUnique, bool IsPrimary, bool IsPartial,
    IReadOnlyList<DatabaseIndexTerm> Terms, string? NativeDefinition = null, string? NativeMethod = null,
    bool? IsValid = null, bool? IsReady = null);

/// <summary>Object-owned constraints/indexes. Incoming FKs/domain constraints require an explicit further scope.</summary>
public sealed record DatabaseStructure(DatabaseTableRef Table, IReadOnlyList<DatabaseColumn> Columns,
    DatabaseMetadataCoverage ConstraintCoverage, IReadOnlyList<DatabaseConstraint> Constraints,
    DatabaseMetadataCoverage IndexCoverage, IReadOnlyList<DatabaseIndex> Indexes,
    string? NativeDefinition, IReadOnlyList<DatabaseDiagnostic> Diagnostics);

/// <summary>Bounded inspector payload; exceeding a bound is explicit, never silently truncated.</summary>
public static class DatabaseStructureLimits
{
    public const int MaxColumns = 4_096;
    public const int MaxItems = 2_000;
    public const int MaxTerms = 4_096;
    public const int MaxTextCharacters = 1_000_000;
    public const int MaxTotalCharacters = 8_000_000;

    public static void Validate(DatabaseStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        long total = 0;
        void Count(int count, int limit) { if (count > limit) TooLarge(); }
        void Text(string? value)
        {
            if (value is null) return;
            if (value.Length > MaxTextCharacters || (total += value.Length) > MaxTotalCharacters) TooLarge();
        }
        Count(structure.Columns.Count, MaxColumns);
        Count(structure.Constraints.Count, MaxItems);
        Count(structure.Indexes.Count, MaxItems);
        Count(structure.Diagnostics.Count, MaxItems);
        Text(structure.Table.Schema); Text(structure.Table.Name); Text(structure.NativeDefinition);
        foreach (var column in structure.Columns) { Text(column.Name); Text(column.NativeType); }
        foreach (var constraint in structure.Constraints)
        {
            Text(constraint.Name); Text(constraint.NativeKind); Text(constraint.NativeDefinition);
            Count(constraint.Columns.Count, MaxTerms);
            foreach (var column in constraint.Columns) Text(column);
            if (constraint.ForeignKey is not { } foreign) continue;
            Count(foreign.ReferencedColumns.Count, MaxTerms);
            Text(foreign.ReferencedSchema); Text(foreign.ReferencedTable);
            Text(foreign.OnUpdate); Text(foreign.OnDelete); Text(foreign.Match);
            foreach (var column in foreign.ReferencedColumns) Text(column);
        }
        foreach (var index in structure.Indexes)
        {
            Text(index.Name); Text(index.NativeDefinition); Text(index.NativeMethod);
            Count(index.Terms.Count, MaxTerms);
            foreach (var term in index.Terms) { Text(term.ColumnName); Text(term.NativeDefinition); Text(term.Collation); }
        }
        foreach (var diagnostic in structure.Diagnostics) { Text(diagnostic.Code); Text(diagnostic.Message); }
    }

    private static void TooLarge() => throw new DatabaseBrowserException("structure_too_large",
        "Структура объекта превышает допустимый размер. Частичные метаданные не показаны.");
}
