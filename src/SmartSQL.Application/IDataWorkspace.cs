using SmartSQL.Domain;

namespace SmartSQL.Application;

public interface IDataWorkspace
{
    string FilePath { get; }
    int SchemaVersion { get; }
    void CreateBackup(string destinationPath);
    RestorationResult RestoreBackup(string sourcePath);
    IReadOnlyList<TableDefinition> GetTables();
    TableDefinition CreateTable(string name, IReadOnlyList<string> fieldNames);
    TableDefinition CreateTable(string name, IReadOnlyList<FieldInput> fields);
    TableDefinition CreateTableWithRecords(string name, IReadOnlyList<FieldInput> fields, IReadOnlyList<IReadOnlyList<string?>> rows);
    FieldDefinition AddField(Guid tableId, FieldInput field);
    TableDefinition UpdateSchema(Guid tableId, string tableName, IReadOnlyDictionary<Guid, string> fieldNames);
    IReadOnlyList<DataRecord> GetRecords(Guid tableId);
    IReadOnlyList<DataRecord> GetRecords(Guid tableId, int offset, int limit);
    int CountRecords(Guid tableId);
    IReadOnlyList<DataRecord> QueryRecords(Guid tableId, int offset, int limit, string? search, Guid? sortFieldId, bool descending);
    IReadOnlyList<DataRecord> QueryRecords(Guid tableId, int offset, int limit, string? search, Guid? sortFieldId, bool descending, IReadOnlyList<RecordFilter> filters);
    int CountRecords(Guid tableId, string? search);
    int CountRecords(Guid tableId, string? search, IReadOnlyList<RecordFilter> filters);
    IReadOnlyList<SavedFilterDefinition> GetSavedFilters(Guid tableId);
    SavedFilterDefinition SaveFilter(Guid tableId, string name, IReadOnlyList<RecordFilter> filters);
    void DeleteSavedFilter(Guid tableId, Guid savedFilterId);
    DeletionImpact GetFieldDeletionImpact(Guid tableId, Guid fieldId);
    DestructiveChangeResult DeleteField(Guid tableId, Guid fieldId);
    DeletionImpact GetTableDeletionImpact(Guid tableId);
    DestructiveChangeResult DeleteTable(Guid tableId);
    DataRecord AddRecord(Guid tableId, IReadOnlyDictionary<Guid, string?> values);
    void AddRecords(Guid tableId, IReadOnlyList<IReadOnlyDictionary<Guid, string?>> rows);
    void UpdateCell(Guid tableId, Guid recordId, Guid fieldId, string? value);
    void DeleteRecords(Guid tableId, IReadOnlyCollection<Guid> recordIds);
}
