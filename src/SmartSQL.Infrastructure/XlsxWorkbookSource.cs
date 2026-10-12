using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

/// <summary>A bounded, detached snapshot of the workbook; the input stream can be closed after Read.</summary>
public sealed class XlsxWorkbookSource
{
    private readonly IReadOnlyList<XlsxWorksheetSource> _sheets;

    internal XlsxWorkbookSource(IReadOnlyList<XlsxWorksheetSource> sheets)
    {
        _sheets = sheets;
        Worksheets = sheets.Select(sheet => sheet.Name).ToArray();
    }

    public IReadOnlyList<string> Worksheets { get; }

    /// <summary>The first row is the header. The resulting preview uses the existing atomic import contract.</summary>
    public CsvImportSource Inspect(TableDefinition table, string worksheetName) =>
        CsvTransfer.Inspect(table, GetCsvContent(worksheetName), ';');

    public CsvImportSource InspectNewTable(string worksheetName) =>
        CsvTransfer.InspectNewTable(GetCsvContent(worksheetName), ';');

    public string GetCsvContent(string worksheetName) => Find(worksheetName).Content;

    /// <summary>Display before committing: XLSX features that are absent from the table model.</summary>
    public IReadOnlyList<string> GetWarnings(string worksheetName) => Find(worksheetName).Warnings;

    private XlsxWorksheetSource Find(string name) => _sheets.FirstOrDefault(sheet => sheet.Name == name)
        ?? throw new ArgumentException("Лист XLSX не найден.", nameof(name));
}

internal sealed class XlsxWorksheetSource
{
    private readonly XElement _root;
    private readonly XlsxSharedString[] _strings;
    private readonly XlsxCellStyle[] _styles;
    private readonly bool _date1904;
    private readonly bool _hidden;
    private readonly IReadOnlyList<string> _relationshipTypes;
    private readonly IReadOnlyList<string> _workbookWarnings;
    private string? _content;
    private IReadOnlyList<string>? _warnings;

    internal XlsxWorksheetSource(string name, XElement root, XlsxSharedString[] strings,
        XlsxCellStyle[] styles, bool date1904, bool hidden, IReadOnlyList<string> relationshipTypes, IReadOnlyList<string> workbookWarnings)
    {
        Name = name;
        _root = root;
        _strings = strings;
        _styles = styles;
        _date1904 = date1904;
        _hidden = hidden;
        _relationshipTypes = relationshipTypes;
        _workbookWarnings = workbookWarnings;
    }

    internal string Name { get; }
    internal string Content { get { EnsureParsed(); return _content!; } }
    internal IReadOnlyList<string> Warnings { get { EnsureParsed(); return _warnings!; } }

    private void EnsureParsed()
    {
        if (_content is not null)
            return;
        var ns = _root.Name.Namespace;
        if (_root.Elements(ns + "sheetData").Count() != 1)
            throw new FormatException("В XLSX должен быть один диапазон данных на листе.");
        if (_root.Element(ns + "mergeCells")?.Elements().Any() == true)
            throw new FormatException($"Лист «{Name}» содержит объединённые ячейки. Разъедините их в отдельной копии перед импортом.");
        if (_root.Element(ns + "extLst") is not null || _root.Element(ns + "AlternateContent") is not null || _root.Elements().Any(element => element.Name.LocalName == "AlternateContent"))
            throw new FormatException($"Лист «{Name}» содержит расширения XLSX. Сохраните копию с обычными значениями.");
        var warnings = new HashSet<string>(_workbookWarnings, StringComparer.Ordinal);
        if (_hidden)
            warnings.Add("Выбран скрытый лист; данные будут импортированы как обычная таблица.");
        WarnFeature(warnings, "autoFilter", "Фильтр Excel не переносится; будут импортированы все строки, включая скрытые фильтром.");
        WarnFeature(warnings, "dataValidations", "Правила проверки данных Excel не переносятся.");
        WarnFeature(warnings, "conditionalFormatting", "Условное форматирование Excel не переносится.");
        WarnFeature(warnings, "sheetProtection", "Защита листа Excel не переносится.");
        WarnFeature(warnings, "tableParts", "Таблицы Excel, их настройки и именованные диапазоны не переносятся.");
        WarnFeature(warnings, "hyperlinks", "Гиперссылки не переносятся; сохраняется только текст ячеек.");
        WarnFeature(warnings, "drawing", "Диаграммы, изображения и текстовые блоки не переносятся.");
        WarnFeature(warnings, "legacyDrawing", "Примечания, изображения и элементы управления Excel не переносятся.");
        WarnFeature(warnings, "oleObjects", "Вложенные объекты Excel не переносятся.");
        WarnFeature(warnings, "pivotTableParts", "Сводные таблицы и их источники не переносятся; сохраняются видимые значения ячеек.");
        if (_relationshipTypes.Any(type => type.EndsWith("/comments", StringComparison.Ordinal) || type.Contains("threadedComment", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Примечания и комментарии Excel не переносятся.");
        if (_root.Element(ns + "cols")?.Elements().Any(column => (string?)column.Attribute("hidden") is "1" or "true") == true)
            warnings.Add("Скрытые столбцы также будут импортированы.");

        var rows = new SortedDictionary<int, Dictionary<int, string>>();
        var previousRow = 0;
        var parsedCells = 0;
        var richText = false;
        var displayFormat = false;
        var numeric = false;
        var sheetData = _root.Element(ns + "sheetData")!;
        if (sheetData.Elements().Any(element => element.Name != ns + "row"))
            throw new FormatException("В диапазоне XLSX найдены неподдерживаемые данные.");
        foreach (var row in sheetData.Elements(ns + "row"))
        {
            if (row.Elements().Any(element => element.Name != ns + "c"))
                throw new FormatException("В строке XLSX найдены неподдерживаемые данные.");
            var rowNumber = row.Attribute("r") is null ? previousRow + 1 : XlsxTableTransfer.ParseInteger((string?)row.Attribute("r"), "номер строки");
            if (rowNumber <= previousRow || rowNumber > XlsxTableTransfer.MaxRows + 1)
                throw new FormatException("Номера строк XLSX повторяются, не упорядочены или превышают предел 100 000 записей.");
            previousRow = rowNumber;
            if ((string?)row.Attribute("hidden") is "1" or "true")
                warnings.Add("Скрытые строки также будут импортированы.");
            var values = new Dictionary<int, string>();
            var previousColumn = -1;
            foreach (var cell in row.Elements(ns + "c"))
            {
                var column = cell.Attribute("r") is null ? previousColumn + 1 : ReadReference((string)cell.Attribute("r")!, rowNumber);
                if (column <= previousColumn || column >= XlsxTableTransfer.MaxColumns)
                    throw new FormatException("Столбцы XLSX повторяются, не упорядочены или превышают предел 16 384 столбцов.");
                previousColumn = column;
                if (++parsedCells > XlsxTableTransfer.MaxCells)
                    throw new FormatException("В XLSX больше 2 миллионов ячеек.");
                try
                {
                    values.Add(column, ReadCell(cell, rowNumber == 1, ref richText, ref displayFormat, ref numeric));
                }
                catch (FormatException exception)
                {
                    throw new FormatException($"Лист «{Name}», ячейка {XlsxTableTransfer.ColumnName(column)}{rowNumber}: {exception.Message}", exception);
                }
            }
            rows.Add(rowNumber, values);
        }
        if (!rows.TryGetValue(1, out var headers) || headers.Count == 0 || headers.Values.All(string.IsNullOrWhiteSpace))
            throw new FormatException("Первая строка листа XLSX должна содержать заголовки столбцов.");
        var width = headers.Keys.Max() + 1;
        if (rows.Values.Any(row => row.Keys.Any(column => column >= width)))
            throw new FormatException("В XLSX есть данные за пределами столбцов заголовка. Добавьте заголовки в первую строку.");
        if ((long)previousRow * width > XlsxTableTransfer.MaxCells)
            throw new FormatException("Прямоугольный диапазон XLSX превышает предел 2 миллионов ячеек.");
        if (richText)
            warnings.Add("Форматированный текст и фонетические подсказки превращаются в обычный текст.");
        if (displayFormat)
            warnings.Add("Оформление и числовые форматы Excel не переносятся; проценты и денежные суммы импортируются как хранимые числа. Даты переводятся в ГГГГ-ММ-ДД.");
        if (numeric)
            warnings.Add("Числа читаются точно из файла; ранее округлённые Excel цифры восстановить нельзя.");
        var content = new StringBuilder();
        for (var rowNumber = 1; rowNumber <= previousRow; rowNumber++)
        {
            rows.TryGetValue(rowNumber, out var values);
            for (var column = 0; column < width; column++)
            {
                if (column != 0)
                    content.Append(';');
                var value = values?.GetValueOrDefault(column) ?? string.Empty;
                content.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            }
            content.Append('\n');
            if (content.Length > 50_000_000)
                throw new FormatException("Данные XLSX превышают предел 50 миллионов символов.");
        }
        _warnings = warnings.ToArray();
        _content = content.ToString();
    }

    private void WarnFeature(ISet<string> warnings, string element, string warning)
    {
        if (_root.Element(_root.Name.Namespace + element) is not null)
            warnings.Add(warning);
    }

    private string ReadCell(XElement cell, bool header, ref bool richText, ref bool displayFormat, ref bool numeric)
    {
        var ns = cell.Name.Namespace;
        if (cell.Element(ns + "f") is not null)
            throw new FormatException("Формулы не импортируются и не выполняются. Замените формулы значениями в отдельной копии XLSX.");
        if (cell.Element(ns + "extLst") is not null || cell.Attribute("cm") is not null || cell.Attribute("vm") is not null || cell.Elements().Any(element => element.Name.Namespace != ns || element.Name.LocalName is not ("v" or "is")))
            throw new FormatException("Расширенные или связанные типы ячеек пока не поддерживаются.");
        if (cell.Elements(ns + "v").Count() > 1 || cell.Elements(ns + "is").Count() > 1)
            throw new FormatException("В ячейке несколько значений.");
        var styleIndex = XlsxTableTransfer.ParseInteger((string?)cell.Attribute("s") ?? "0", "стиль ячейки");
        if (styleIndex >= _styles.Length)
            throw new FormatException("Неизвестный стиль ячейки.");
        var style = _styles[styleIndex];
        displayFormat |= style.DisplayFormat || styleIndex != 0;
        var type = (string?)cell.Attribute("t") ?? "n";
        var value = (string?)cell.Element(ns + "v");
        if (type == "inlineStr" && value is not null || type != "inlineStr" && cell.Element(ns + "is") is not null)
            throw new FormatException("Тип ячейки противоречит её значениям; одновременные текстовые и числовые значения не поддерживаются.");
        string text;
        switch (type)
        {
            case "inlineStr":
                var inline = cell.Element(ns + "is") ?? throw new FormatException("Текстовая ячейка без значения.");
                if (inline.Elements().Any(element => element.Name.Namespace != ns || element.Name.LocalName is not ("t" or "r" or "rPh" or "phoneticPr")))
                    throw new FormatException("Текст ячейки содержит неподдерживаемые данные.");
                richText |= inline.Element(ns + "r") is not null || inline.Element(ns + "rPh") is not null;
                text = XlsxTableTransfer.DecodeText(string.Concat(inline.Elements().Where(child => child.Name.LocalName is "t" or "r").Select(child => child.Name.LocalName == "t" ? child.Value : string.Concat(child.Elements(ns + "t").Select(item => item.Value)))));
                break;
            case "s":
                var index = XlsxTableTransfer.ParseInteger(value, "индекс текста");
                if (index >= _strings.Length)
                    throw new FormatException("Индекс текста выходит за пределы словаря XLSX.");
                text = _strings[index].Text;
                richText |= _strings[index].RichText;
                break;
            case "str":
                text = XlsxTableTransfer.DecodeText(value ?? throw new FormatException("Текстовая ячейка без значения."));
                break;
            case "b":
                return value switch { "0" => "0", "1" => "1", _ => throw new FormatException("Неверное значение Да/Нет в XLSX.") };
            case "d":
                if (!DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.fff"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate) || isoDate.TimeOfDay != TimeSpan.Zero)
                    throw new FormatException("Ожидается дата без времени и часового пояса; время не может быть сохранено без потери.");
                return isoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case "e":
                throw new FormatException("Ячейка содержит ошибку Excel. Исправьте её перед импортом.");
            case "n":
                if (value is null || value.Length == 0)
                    return string.Empty;
                var number = PortableNumber.Normalize(value.Trim());
                if (style.UnsupportedTime)
                    throw new FormatException("Формат времени или даты со временем пока не поддерживается. Преобразуйте его явно в отдельной копии.");
                if (style.Date)
                    return ReadDate(number);
                numeric = true;
                return number;
            default:
                throw new FormatException($"Тип ячейки «{type}» пока не поддерживается.");
        }
        if (!header && string.IsNullOrWhiteSpace(text))
            throw new FormatException("Пустую или пробельную текстовую строку нельзя сохранить без изменения. Очистите ячейку в отдельной копии.");
        if (text.Length > 32767)
            throw new FormatException("Текст превышает предел Excel: 32 767 символов.");
        return text;
    }

    private string ReadDate(string text)
    {
        var serial = decimal.Parse(text, CultureInfo.InvariantCulture);
        if (serial != decimal.Truncate(serial) || serial < 0 || serial > 3_652_058 || !_date1904 && serial is 0 or 60)
            throw new FormatException("Дата XLSX содержит время, неверный день 29.02.1900 или выходит за допустимые пределы.");
        try
        {
            var basis = _date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31);
            var days = (int)serial - (!_date1904 && serial > 60 ? 1 : 0);
            return basis.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException("Дата XLSX выходит за допустимые пределы.", exception);
        }
    }

    private static int ReadReference(string reference, int row)
    {
        var match = Regex.Match(reference, "^([A-Z]{1,3})([1-9][0-9]{0,6})$");
        if (!match.Success || XlsxTableTransfer.ParseInteger(match.Groups[2].Value, "номер строки ячейки") != row)
            throw new FormatException("Адрес ячейки XLSX не соответствует строке.");
        var column = 0;
        foreach (var character in match.Groups[1].Value)
            column = column * 26 + character - 'A' + 1;
        return column - 1;
    }
}
