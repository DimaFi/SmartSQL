namespace SmartSQL.Domain;

public enum FieldType
{
    Text,
    Number,
    Date,
    Boolean
}

public sealed record FieldInput(string Name, FieldType Type);

public sealed record FieldDefinition(Guid Id, string Name, int Position, FieldType Type = FieldType.Text)
{
    public override string ToString() => Name;
}

public sealed record TableDefinition(Guid Id, string Name, IReadOnlyList<FieldDefinition> Fields)
{
    public override string ToString() => Name;
}

public sealed record DataRecord(Guid Id, IReadOnlyDictionary<Guid, string?> Values);

public enum FilterOperator
{
    Equals,
    Contains,
    GreaterThan,
    LessThan,
    IsEmpty,
    IsNotEmpty
}

public sealed record RecordFilter(Guid FieldId, FilterOperator Operator, string? Value);

public sealed record SavedFilterDefinition(Guid Id, Guid TableId, string Name, IReadOnlyList<RecordFilter> Filters)
{
    public override string ToString() => Name;
}

public sealed record DeletionImpact(string TargetName, int RecordCount, int AffectedSavedFilterCount);

public sealed record DestructiveChangeResult(string BackupPath);

public sealed record RestorationResult(string SafetyBackupPath);
