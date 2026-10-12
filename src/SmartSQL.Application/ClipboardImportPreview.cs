using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed class ClipboardImportPreview
{
    private readonly TableDefinition _table;
    private readonly IReadOnlyList<IReadOnlyDictionary<Guid, string?>> _rows;
    private readonly object _commitLock = new();
    private bool _committed;

    internal ClipboardImportPreview(TableDefinition table, IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows, IReadOnlyList<IReadOnlyDictionary<Guid, string?>> rows,
        string[] skippedColumns, string[] emptyFields, string?[] targetFields)
    {
        _table = table with { Fields = table.Fields.ToArray() };
        _rows = rows;
        Headers = headers;
        SampleRows = sampleRows;
        SkippedColumns = Array.AsReadOnly(skippedColumns);
        EmptyFields = Array.AsReadOnly(emptyFields);
        TargetFields = Array.AsReadOnly(targetFields);
    }

    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; }
    public IReadOnlyList<string> SkippedColumns { get; }
    public IReadOnlyList<string> EmptyFields { get; }
    public IReadOnlyList<string?> TargetFields { get; }
    public int RowCount => _rows.Count;
    public int ColumnCount => Headers.Count;

    public int Commit(IDataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        lock (_commitLock)
        {
            if (_committed)
                throw new InvalidOperationException("Эти ячейки уже вставлены.");
            var current = workspace.GetTables().FirstOrDefault(table => table.Id == _table.Id)
                ?? throw new InvalidOperationException("Таблица была удалена после предварительного просмотра.");
            if (!current.Fields.SequenceEqual(_table.Fields))
                throw new InvalidOperationException("Поля таблицы изменились после предварительного просмотра. Подготовьте вставку заново.");
            workspace.AddRecords(_table.Id, _rows);
            _committed = true;
            return _rows.Count;
        }
    }
}
