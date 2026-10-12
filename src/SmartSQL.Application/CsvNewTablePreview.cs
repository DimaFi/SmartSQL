using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed class CsvNewTablePreview
{
    private bool _committed;

    internal CsvNewTablePreview(string tableName, IReadOnlyList<FieldInput> fields,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        TableName = tableName;
        Fields = fields;
        Rows = rows;
    }

    public string TableName { get; }
    public IReadOnlyList<FieldInput> Fields { get; }
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; }
    public int RowCount => Rows.Count;

    public TableDefinition Commit(IDataWorkspace workspace)
    {
        if (_committed)
            throw new InvalidOperationException("Этот импорт уже выполнен.");
        var table = workspace.CreateTableWithRecords(TableName, Fields, Rows);
        _committed = true;
        return table;
    }
}
