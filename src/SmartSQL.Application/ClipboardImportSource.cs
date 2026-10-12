using System.Collections.ObjectModel;
using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed class ClipboardImportSource
{
    private readonly TableDefinition _table;
    private readonly IReadOnlyList<string[]> _rows;

    internal ClipboardImportSource(TableDefinition table, IReadOnlyList<string[]> records, bool hasHeader)
    {
        _table = table with { Fields = table.Fields.ToArray() };
        HasHeader = hasHeader;
        Headers = Array.AsReadOnly(hasHeader ? records[0].ToArray()
            : Enumerable.Range(1, records[0].Length).Select(index => $"Столбец {index}").ToArray());
        _rows = records.Skip(hasHeader ? 1 : 0).ToArray();
        SampleRows = Array.AsReadOnly(_rows.Take(5)
            .Select(row => (IReadOnlyList<string>)Array.AsReadOnly(row.ToArray())).ToArray());
    }

    public bool HasHeader { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; }
    public int RowCount => _rows.Count;
    public int ColumnCount => Headers.Count;

    public IReadOnlyDictionary<int, Guid> SuggestMapping()
    {
        var mapping = new Dictionary<int, Guid>();
        var usedFields = new HashSet<Guid>();
        for (var index = 0; index < Headers.Count; index++)
        {
            var field = HasHeader
                ? _table.Fields.FirstOrDefault(item => string.Equals(item.Name, Headers[index].Trim(), StringComparison.OrdinalIgnoreCase))
                : _table.Fields.ElementAtOrDefault(index);
            if (field is not null && usedFields.Add(field.Id))
                mapping[index] = field.Id;
        }
        return new ReadOnlyDictionary<int, Guid>(mapping);
    }

    public ClipboardImportPreview Prepare(IReadOnlyDictionary<int, Guid> mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.Count == 0)
            throw new ArgumentException("Сопоставьте хотя бы один столбец с полем таблицы.", nameof(mapping));
        if (mapping.Keys.Any(index => index < 0 || index >= Headers.Count))
            throw new ArgumentException("Указан несуществующий столбец вставки.", nameof(mapping));
        if (mapping.Values.Distinct().Count() != mapping.Count)
            throw new ArgumentException("Одно поле таблицы нельзя выбрать для нескольких столбцов.", nameof(mapping));
        var fields = _table.Fields.ToDictionary(field => field.Id);
        if (mapping.Values.Any(id => !fields.ContainsKey(id)))
            throw new ArgumentException("Указано несуществующее поле таблицы.", nameof(mapping));
        var rows = new List<IReadOnlyDictionary<Guid, string?>>(RowCount);
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var values = new Dictionary<Guid, string?>();
            foreach (var (columnIndex, fieldId) in mapping)
            {
                var field = fields[fieldId];
                try
                {
                    var value = _rows[rowIndex][columnIndex];
                    if (field.Type == FieldType.Text && value.Length > 0 && string.IsNullOrWhiteSpace(value))
                        throw new FormatException("Текст из одних пробельных символов нельзя сохранить без изменения. Очистите ячейку или добавьте текст.");
                    values[fieldId] = FieldValue.Normalize(field.Type, value);
                }
                catch (FormatException exception)
                {
                    var sourceRow = rowIndex + (HasHeader ? 2 : 1);
                    throw new FormatException($"Строка {sourceRow}, поле «{field.Name}»: {exception.Message}", exception);
                }
            }
            rows.Add(new ReadOnlyDictionary<Guid, string?>(values));
        }
        var skipped = Enumerable.Range(0, Headers.Count).Where(index => !mapping.ContainsKey(index))
            .Select(index => string.IsNullOrWhiteSpace(Headers[index]) ? $"Столбец {index + 1}" : Headers[index]).ToArray();
        var empty = _table.Fields.Where(field => !mapping.Values.Contains(field.Id)).Select(field => field.Name).ToArray();
        var targets = Enumerable.Range(0, Headers.Count)
            .Select(index => mapping.TryGetValue(index, out var fieldId) ? fields[fieldId].Name : null).ToArray();
        return new ClipboardImportPreview(_table, Headers, SampleRows, rows, skipped, empty, targets);
    }
}
