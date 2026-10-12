using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed class CsvImportPreview
{
    private readonly TableDefinition _table;
    private readonly IReadOnlyList<IReadOnlyDictionary<Guid, string?>> _rows;
    private bool _committed;

    internal CsvImportPreview(TableDefinition table, IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows, IReadOnlyList<IReadOnlyDictionary<Guid, string?>> rows,
        IReadOnlyList<string> skippedColumns, IReadOnlyList<string> emptyFields, IReadOnlyList<string?> targetFields)
    {
        _table = table;
        Headers = headers;
        SampleRows = sampleRows;
        SkippedColumns = skippedColumns;
        EmptyFields = emptyFields;
        TargetFields = targetFields;
        _rows = rows;
    }

    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; }
    public IReadOnlyList<string> SkippedColumns { get; }
    public IReadOnlyList<string> EmptyFields { get; }
    public IReadOnlyList<string?> TargetFields { get; }
    public int RowCount => _rows.Count;

    public int Commit(IDataWorkspace workspace)
    {
        if (_committed)
            throw new InvalidOperationException("Этот CSV уже импортирован.");
        var current = workspace.GetTables().FirstOrDefault(table => table.Id == _table.Id)
            ?? throw new InvalidOperationException("Таблица была удалена после предварительного просмотра.");
        if (current.Fields.Count != _table.Fields.Count || current.Fields.Where((field, index) =>
                field.Id != _table.Fields[index].Id || field.Type != _table.Fields[index].Type ||
                field.Name != _table.Fields[index].Name).Any())
            throw new InvalidOperationException("Поля таблицы изменились после предварительного просмотра. Откройте CSV заново.");
        workspace.AddRecords(_table.Id, _rows);
        _committed = true;
        return _rows.Count;
    }
}
