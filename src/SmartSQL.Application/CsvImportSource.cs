using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed class CsvImportSource
{
    private readonly TableDefinition _table;
    private readonly IReadOnlyList<string[]> _records;

    internal CsvImportSource(TableDefinition table, char delimiter, IReadOnlyList<string[]> records)
    {
        _table = table;
        _records = records;
        Delimiter = delimiter;
        Headers = records[0];
        SampleRows = records.Skip(1).Take(5).Select(row => (IReadOnlyList<string>)row).ToArray();
    }

    public char Delimiter { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; }
    public int RowCount => _records.Count - 1;

    public IReadOnlyDictionary<int, Guid> SuggestMapping()
    {
        var mapping = new Dictionary<int, Guid>();
        var usedFields = new HashSet<Guid>();
        for (var index = 0; index < Headers.Count; index++)
        {
            var field = _table.Fields.FirstOrDefault(item =>
                string.Equals(item.Name, Headers[index].Trim(), StringComparison.OrdinalIgnoreCase));
            if (field is not null && usedFields.Add(field.Id))
                mapping[index] = field.Id;
        }
        return mapping;
    }

    public CsvNewTablePreview PrepareNewTable(string tableName, IReadOnlyList<FieldInput> fields)
    {
        tableName = tableName.Trim();
        if (tableName.Length == 0)
            throw new ArgumentException("Укажите название таблицы.", nameof(tableName));
        if (fields.Count != Headers.Count)
            throw new ArgumentException("Для каждого столбца CSV укажите поле таблицы.", nameof(fields));
        var normalizedFields = fields.Select(field => field with { Name = field.Name.Trim() }).ToArray();
        if (normalizedFields.Any(field => field.Name.Length == 0))
            throw new ArgumentException("Названия полей не могут быть пустыми.", nameof(fields));
        if (normalizedFields.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Count)
            throw new ArgumentException("Названия полей не должны повторяться.", nameof(fields));
        if (normalizedFields.Any(field => !Enum.IsDefined(field.Type)))
            throw new ArgumentException("Неизвестный тип поля.", nameof(fields));

        var rows = new List<IReadOnlyList<string?>>(RowCount);
        for (var rowIndex = 1; rowIndex < _records.Count; rowIndex++)
        {
            var values = new string?[fields.Count];
            for (var columnIndex = 0; columnIndex < fields.Count; columnIndex++)
            {
                try
                {
                    values[columnIndex] = FieldValue.Normalize(normalizedFields[columnIndex].Type,
                        _records[rowIndex][columnIndex]);
                }
                catch (FormatException exception)
                {
                    throw new FormatException($"Строка {rowIndex + 1}, поле «{normalizedFields[columnIndex].Name}»: {exception.Message}", exception);
                }
            }
            rows.Add(values);
        }
        return new CsvNewTablePreview(tableName, normalizedFields, rows);
    }

    public CsvImportPreview Prepare(IReadOnlyDictionary<int, Guid> mapping)
    {
        if (mapping.Count == 0)
            throw new ArgumentException("Сопоставьте хотя бы один столбец с полем таблицы.", nameof(mapping));
        if (mapping.Keys.Any(index => index < 0 || index >= Headers.Count))
            throw new ArgumentException("Указан несуществующий столбец CSV.", nameof(mapping));
        if (mapping.Values.Distinct().Count() != mapping.Count)
            throw new ArgumentException("Одно поле таблицы нельзя выбрать для нескольких столбцов.", nameof(mapping));
        if (mapping.Values.Any(id => _table.Fields.All(field => field.Id != id)))
            throw new ArgumentException("Указано несуществующее поле таблицы.", nameof(mapping));

        var rows = new List<IReadOnlyDictionary<Guid, string?>>(RowCount);
        for (var rowIndex = 1; rowIndex < _records.Count; rowIndex++)
        {
            var source = _records[rowIndex];
            var values = new Dictionary<Guid, string?>();
            foreach (var (columnIndex, fieldId) in mapping)
            {
                var field = _table.Fields.Single(item => item.Id == fieldId);
                try
                {
                    FieldValue.Normalize(field.Type, source[columnIndex]);
                }
                catch (FormatException exception)
                {
                    throw new FormatException($"Строка {rowIndex + 1}, поле «{field.Name}»: {exception.Message}", exception);
                }
                values[fieldId] = source[columnIndex];
            }
            rows.Add(values);
        }

        var skippedColumns = Enumerable.Range(0, Headers.Count).Where(index => !mapping.ContainsKey(index))
            .Select(index => string.IsNullOrWhiteSpace(Headers[index]) ? $"Столбец {index + 1}" : Headers[index]).ToArray();
        var emptyFields = _table.Fields.Where(field => !mapping.Values.Contains(field.Id)).Select(field => field.Name).ToArray();
        var targetFields = Enumerable.Range(0, Headers.Count)
            .Select(index => mapping.TryGetValue(index, out var fieldId)
                ? _table.Fields.Single(field => field.Id == fieldId).Name : null).ToArray();
        return new CsvImportPreview(_table, Headers, SampleRows, rows, skippedColumns, emptyFields, targetFields);
    }
}
