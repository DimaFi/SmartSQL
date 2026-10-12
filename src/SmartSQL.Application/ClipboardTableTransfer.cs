using System.Text;
using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>
/// Inspects rectangular tab-separated data. A quote at the start of a cell opens
/// Excel-style quoting (embedded quotes are doubled); quotes elsewhere in an
/// unquoted cell stay literal. Tabs and line breaks inside quoted cells are preserved.
/// </summary>
public static class ClipboardTableTransfer
{
    public const int MaxRows = 100_000;
    public const int MaxColumns = 16_384;
    public const int MaxCells = 2_000_000;
    public const int MaxCharacters = 50_000_000;

    public static ClipboardImportSource Inspect(TableDefinition table, string content, bool hasHeader = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxCharacters)
            throw new FormatException("Вставка слишком большая: предел 50 миллионов символов.");
        if (content.Length == 0 || content == "\uFEFF")
            throw new FormatException("Вставьте скопированные ячейки в поле данных.");
        var records = Parse(content, hasHeader);
        if (records.Count == 0)
            throw new FormatException("Вставьте скопированные ячейки в поле данных.");
        return new ClipboardImportSource(table, records, hasHeader);
    }

    private static List<string[]> Parse(string text, bool hasHeader)
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
            if (character is '\t' or '\r' or '\n')
            {
                CheckColumnCount(fields.Count);
                fields.Add(field.ToString());
                field.Clear();
                started = false;
                afterQuote = false;
                if (character == '\t')
                    continue;
                AddRow(records, fields, hasHeader);
                fields.Clear();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                continue;
            }
            if (character == '"' && !started && !afterQuote)
            {
                quoted = true;
                started = true;
                continue;
            }
            if (afterQuote)
                throw new FormatException($"Строка {records.Count + 1}: неверные кавычки в скопированных ячейках.");
            field.Append(character);
            started = true;
        }
        if (quoted)
            throw new FormatException($"Строка {records.Count + 1}: незакрытая ячейка в кавычках.");
        if (started || afterQuote || field.Length > 0 || fields.Count > 0)
        {
            CheckColumnCount(fields.Count);
            fields.Add(field.ToString());
            AddRow(records, fields, hasHeader);
        }
        return records;
    }

    private static void CheckColumnCount(int completedColumns)
    {
        if (completedColumns >= MaxColumns)
            throw new FormatException($"За один раз можно вставить не более {MaxColumns:N0} столбцов.");
    }

    private static void AddRow(List<string[]> records, List<string> fields, bool hasHeader)
    {
        if (records.Count > 0 && fields.Count != records[0].Length)
            throw new FormatException($"Строка {records.Count + 1}: ожидалось {records[0].Length} столбцов, найдено {fields.Count}. Скопируйте прямоугольный диапазон ячеек.");
        if (records.Count >= MaxRows + (hasHeader ? 1 : 0))
            throw new FormatException($"За один раз можно вставить не более {MaxRows:N0} строк данных.");
        if ((long)(records.Count + 1) * fields.Count > MaxCells)
            throw new FormatException($"Вставка содержит слишком много ячеек: предел {MaxCells:N0}, включая заголовки.");
        records.Add(fields.ToArray());
    }
}
