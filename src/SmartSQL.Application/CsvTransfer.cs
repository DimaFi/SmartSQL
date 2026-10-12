using System.Text;
using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>CSV exchange for an existing table. Headers are matched by field name, not column position.</summary>
public static class CsvTransfer
{
    private const int MaxImportRows = 100_000;
    private const int MaxImportCharacters = 50_000_000;

    public static int Import(IDataWorkspace workspace, Guid tableId, TextReader reader) =>
        PrepareImport(workspace, tableId, reader).Commit(workspace);

    public static CsvImportPreview PrepareImport(IDataWorkspace workspace, Guid tableId, TextReader reader)
    {
        var table = workspace.GetTables().FirstOrDefault(item => item.Id == tableId)
            ?? throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        var source = Inspect(table, reader.ReadToEnd());
        var mapping = source.SuggestMapping();
        if (mapping.Count != table.Fields.Count || mapping.Count != source.Headers.Count)
            throw new FormatException("Заголовки CSV должны совпадать с полями выбранной таблицы.");
        return source.Prepare(mapping);
    }

    public static CsvImportSource Inspect(TableDefinition table, string content, char? delimiter = null)
    {
        if (content.Length == 0)
            throw new FormatException("CSV-файл пуст.");
        if (content.Length > MaxImportCharacters)
            throw new FormatException("CSV-файл слишком большой: предел 50 миллионов символов.");
        if (delimiter is not null and not (';' or ','))
            throw new ArgumentException("Разделителем CSV может быть только ; или ,.", nameof(delimiter));
        var candidates = new List<(char Delimiter, List<string[]> Records, int Score)>();
        foreach (var candidateDelimiter in delimiter is null ? new[] { ';', ',' } : new[] { delimiter.Value })
        {
            try
            {
                var parsed = Parse(content, candidateDelimiter);
                if (parsed.Count == 0 || parsed[0].Length == 0 ||
                    parsed.Skip(1).Any(row => row.Length != parsed[0].Length))
                    continue;
                var matches = parsed[0].Count(header => table.Fields.Any(field =>
                    string.Equals(field.Name, header.Trim(), StringComparison.OrdinalIgnoreCase)));
                candidates.Add((candidateDelimiter, parsed, matches * 1000 + parsed[0].Length));
            }
            catch (FormatException)
            {
                // Try the other delimiter if it was not specified explicitly.
            }
        }
        if (candidates.Count == 0)
            throw new FormatException("Не удалось разобрать CSV: проверьте разделитель, кавычки и число столбцов в строках.");
        var selected = candidates.OrderByDescending(candidate => candidate.Score).First();
        var records = selected.Records;
        if (records.Count - 1 > MaxImportRows)
            throw new FormatException($"За один раз можно импортировать не более {MaxImportRows:N0} строк.");
        return new CsvImportSource(table, selected.Delimiter, records);
    }

    public static CsvImportSource InspectNewTable(string content, char? delimiter = null) =>
        Inspect(new TableDefinition(Guid.Empty, "CSV", []), content, delimiter);

    public static int Export(IDataWorkspace workspace, Guid tableId, TextWriter writer, bool spreadsheetSafe = false)
    {
        var table = workspace.GetTables().FirstOrDefault(item => item.Id == tableId)
            ?? throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        WriteRow(writer, table.Fields.Select(field => field.Name));
        var count = 0;
        while (true)
        {
            var page = workspace.GetRecords(tableId, count, 500);
            if (page.Count == 0)
                return count;
            foreach (var record in page)
            {
                WriteRow(writer, table.Fields.Select(field =>
                    PrepareForExport(record.Values.GetValueOrDefault(field.Id), spreadsheetSafe)));
                count++;
            }
        }
    }

    private static string PrepareForExport(string? value, bool spreadsheetSafe)
    {
        var text = value ?? string.Empty;
        if (!spreadsheetSafe)
            return text;
        var first = text.TrimStart();
        return first.Length > 0 && "=+-@".Contains(first[0]) || text.StartsWith('\t') || text.StartsWith('\r')
            ? "'" + text : text;
    }

    private static void WriteRow(TextWriter writer, IEnumerable<string> values) =>
        writer.WriteLine(string.Join(";", values.Select(value =>
            value.IndexOfAny([';', '"', '\r', '\n']) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value)));

    private static List<string[]> Parse(string text, char delimiter)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        var started = false;
        for (var index = text[0] == '\uFEFF' ? 1 : 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        afterQuote = true;
                    }
                }
                else
                    field.Append(character);
                continue;
            }
            if (character == delimiter || character is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                started = false;
                afterQuote = false;
                if (character == delimiter)
                    continue;
                records.Add(fields.ToArray());
                fields.Clear();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                continue;
            }
            if (character == '"' && !started && field.Length == 0 && !afterQuote)
            {
                quoted = true;
                started = true;
                continue;
            }
            if (character == '"' || afterQuote)
                throw new FormatException("Некорректное экранирование CSV.");
            field.Append(character);
            started = true;
        }
        if (quoted)
            throw new FormatException("Незакрытое поле CSV в кавычках.");
        if (started || afterQuote || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }
        return records;
    }
}
