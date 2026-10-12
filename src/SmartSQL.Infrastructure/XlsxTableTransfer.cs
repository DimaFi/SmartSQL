using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed record XlsxExportResult(int RowCount, int NumbersStoredAsText, string WorksheetName, bool WorksheetNameChanged);

/// <summary>Data-only OOXML exchange. Does not execute formulas, macros, or external references.</summary>
public static class XlsxTableTransfer
{
    internal const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal const int MaxRows = 100_000;
    internal const int MaxColumns = 16_384;
    internal const int MaxCells = 2_000_000;
    private const long MaxPackageBytes = 100_000_000;

    public static XlsxWorkbookSource Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanSeek)
        {
            using var buffered = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer)) != 0)
            {
                if (buffered.Length + read > MaxPackageBytes)
                    throw new FormatException("Файл XLSX превышает предел 100 МБ.");
                buffered.Write(buffer, 0, read);
            }
            buffered.Position = 0;
            return Read(buffered);
        }
        if (input.Length > MaxPackageBytes)
            throw new FormatException("Файл XLSX превышает предел 100 МБ.");
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 4096 || archive.Entries.Sum(entry => entry.Length) > MaxPackageBytes)
                throw new FormatException("Книга XLSX слишком большая: предел 100 МБ распакованных данных и 4096 частей.");
            if (archive.Entries.GroupBy(entry => entry.FullName, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new FormatException("Книга XLSX содержит повторяющиеся части.");
            if (archive.Entries.Any(entry => entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("Книга содержит макросы. Сохраните отдельную копию XLSX без макросов.");
            var contentTypes = ReadXml(archive, "[Content_Types].xml");
            if (contentTypes.Name.LocalName != "Types")
                throw new FormatException("В XLSX неверно описаны типы частей.");
            // External targets may also occur in drawings and embedded objects. Never resolve them.
            foreach (var relationPart in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.Ordinal)))
                ReadRelationships(ReadXml(archive, relationPart.FullName));
            var packageRelationships = ReadXml(archive, "_rels/.rels");
            var workbookRelationship = ReadRelationships(packageRelationships).SingleOrDefault(item => item.Type.EndsWith("/officeDocument", StringComparison.Ordinal))
                ?? throw new FormatException("В XLSX не найдена книга.");
            var workbookPart = ResolvePart(string.Empty, workbookRelationship.Target);
            if (!contentTypes.Elements().Any(element => (string?)element.Attribute("PartName") == "/" + workbookPart &&
                    (string?)element.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"))
                throw new FormatException("Ожидается обычная книга XLSX без макросов.");
            var workbook = ReadXml(archive, workbookPart);
            var ns = workbook.Name.Namespace;
            if (ns.NamespaceName is not (SpreadsheetNamespace or "http://purl.oclc.org/ooxml/spreadsheetml/main") || workbook.Name.LocalName != "workbook")
                throw new FormatException("Неподдерживаемый формат книги XLSX.");
            if (workbook.Element(ns + "externalReferences") is not null)
                throw new FormatException("Книга содержит внешние ссылки. Сохраните копию с обычными значениями.");
            var relationships = ReadRelationships(ReadXml(archive, RelationshipPart(workbookPart))).ToDictionary(item => item.Id, StringComparer.Ordinal);
            var sharedRelationship = relationships.Values.SingleOrDefault(item => item.Type.EndsWith("/sharedStrings", StringComparison.Ordinal));
            var sharedStrings = sharedRelationship is null ? [] : ReadSharedStrings(ReadXml(archive, ResolvePart(workbookPart, sharedRelationship.Target)));
            var styleRelationship = relationships.Values.SingleOrDefault(item => item.Type.EndsWith("/styles", StringComparison.Ordinal));
            var styles = styleRelationship is null ? new[] { XlsxCellStyle.Default } : ReadStyles(ReadXml(archive, ResolvePart(workbookPart, styleRelationship.Target)));
            var date1904 = (string?)workbook.Element(ns + "workbookPr")?.Attribute("date1904") is "1" or "true";
            var sheets = new List<XlsxWorksheetSource>();
            var worksheetParts = new HashSet<string>(StringComparer.Ordinal);
            var worksheetRelationships = new HashSet<string>(StringComparer.Ordinal);
            var workbookWarnings = new List<string>();
            if (workbook.Element(ns + "definedNames") is not null)
                workbookWarnings.Add("Именованные диапазоны и выражения книги не переносятся.");
            if (workbook.Element(ns + "workbookProtection") is not null)
                workbookWarnings.Add("Защита книги Excel не переносится.");
            if (archive.Entries.Any(entry => entry.FullName.StartsWith("customXml/", StringComparison.Ordinal) || entry.FullName is "xl/connections.xml" or "xl/metadata.xml" || entry.FullName.StartsWith("xl/model/", StringComparison.Ordinal)))
                workbookWarnings.Add("Связи данных, модели данных и дополнительные метаданные книги не переносятся.");
            foreach (var sheet in workbook.Element(ns + "sheets")?.Elements(ns + "sheet") ?? [])
            {
                if (sheets.Count >= 512)
                    throw new FormatException("Книга XLSX превышает предел 512 листов.");
                var name = (string?)sheet.Attribute("name");
                var relationshipId = sheet.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
                if (string.IsNullOrWhiteSpace(name) || relationshipId is null || !relationships.TryGetValue(relationshipId, out var relationship))
                    throw new FormatException("Книга содержит некорректное описание листа.");
                if (!relationship.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                    throw new FormatException($"Лист «{name}» не является таблицей. Листы диаграмм и макросов пока не поддерживаются.");
                var part = ResolvePart(workbookPart, relationship.Target);
                if (!worksheetRelationships.Add(relationshipId) || !worksheetParts.Add(part))
                    throw new FormatException("Несколько листов XLSX ссылаются на одни данные.");
                var root = ReadXml(archive, part);
                if (root.Name.LocalName != "worksheet" || root.Name.Namespace != ns)
                    throw new FormatException($"Лист «{name}» имеет неподдерживаемый формат.");
                var sheetRelationships = archive.GetEntry(RelationshipPart(part)) is null ? [] : ReadRelationships(ReadXml(archive, RelationshipPart(part)));
                var state = (string?)sheet.Attribute("state");
                sheets.Add(new XlsxWorksheetSource(DecodeText(name), root, sharedStrings, styles, date1904, state is "hidden" or "veryHidden", sheetRelationships.Select(item => item.Type).ToArray(), workbookWarnings));
            }
            if (sheets.Count == 0 || sheets.Select(sheet => sheet.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sheets.Count)
                throw new FormatException("Книга не содержит листов или названия листов повторяются.");
            return new XlsxWorkbookSource(sheets);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or InvalidOperationException or OverflowException or ArgumentException)
        {
            throw new FormatException("Не удалось прочитать XLSX: файл повреждён или содержит неподдерживаемую структуру.", exception);
        }
    }

    public static XlsxExportResult Export(IDataWorkspace workspace, Guid tableId, Stream output)
    {
        var table = workspace.GetTables().FirstOrDefault(item => item.Id == tableId)
            ?? throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        if (table.Fields.Count is 0 or > MaxColumns || table.Fields.Any(field => !Enum.IsDefined(field.Type)))
            throw new FormatException("Набор полей таблицы не поддерживается XLSX.");
        var totalRows = workspace.CountRecords(tableId);
        if (totalRows > MaxRows || (long)(totalRows + 1) * table.Fields.Count > MaxCells)
            throw new FormatException("Экспорт XLSX ограничен 100 000 записей и 2 миллионами ячеек.");
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            ContentType("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
            ContentType("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
            ContentType("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        WritePart(archive, "_rels/.rels", Relationships([("rId1", "officeDocument", "xl/workbook.xml")]));
        WritePart(archive, "xl/_rels/workbook.xml.rels", Relationships([("rId1", "worksheet", "worksheets/sheet1.xml"), ("rId2", "styles", "styles.xml")]));
        XNamespace ns = SpreadsheetNamespace;
        XNamespace rel = OfficeRelationshipsNamespace;
        var sheetName = Regex.Replace(table.Name, "[\\x00-\\x1F\\[\\]:*?/\\\\]", "_").Trim('\'');
        if (sheetName.Length > 31)
        {
            sheetName = sheetName[..31];
            if (char.IsHighSurrogate(sheetName[^1]))
                sheetName = sheetName[..^1];
        }
        if (sheetName.Length == 0)
            sheetName = "Данные";
        WritePart(archive, "xl/workbook.xml", new XElement(ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel),
            new XElement(ns + "sheets", new XElement(ns + "sheet", new XAttribute("name", EncodeText(sheetName)), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1")))));
        WritePart(archive, "xl/styles.xml", ExportStyles());
        var count = 0;
        var textNumbers = 0;
        using (var writer = CreateWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open()))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("sheetData");
            WriteRow(writer, 1, table.Fields.Select(field => (FieldType.Text, (string?)field.Name)), ref textNumbers);
            while (true)
            {
                var page = workspace.GetRecords(tableId, count, 500);
                if (page.Count == 0)
                    break;
                foreach (var record in page)
                {
                    if (count >= MaxRows || (long)(count + 2) * table.Fields.Count > MaxCells)
                        throw new FormatException("Таблица выросла во время экспорта и превысила предел XLSX.");
                    WriteRow(writer, count + 2, table.Fields.Select(field => (field.Type, record.Values.GetValueOrDefault(field.Id))), ref textNumbers);
                    count++;
                }
            }
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        return new XlsxExportResult(count, textNumbers, sheetName, !string.Equals(sheetName, table.Name, StringComparison.Ordinal));
    }

    private static void WriteRow(XmlWriter writer, int row, IEnumerable<(FieldType Type, string? Value)> values, ref int textNumbers)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
        var column = 0;
        foreach (var (type, value) in values)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", ColumnName(column++) + row.ToString(CultureInfo.InvariantCulture));
            if (value is not null)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new FormatException("Пустую или пробельную строку нельзя перенести без изменения. Используйте пустое значение.");
                if (type == FieldType.Number)
                {
                    var number = PortableNumber.Normalize(value);
                    if (SignificantDigits(number) > 15)
                    {
                        WriteText(writer, number);
                        textNumbers++;
                    }
                    else
                        writer.WriteElementString("v", number);
                }
                else if (type == FieldType.Boolean)
                {
                    writer.WriteAttributeString("t", "b");
                    writer.WriteElementString("v", FieldValue.Normalize(type, value));
                }
                else if (type == FieldType.Date)
                {
                    var date = DateTime.ParseExact(FieldValue.Normalize(type, value)!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    // Excel's numeric date system cannot represent earlier dates portably.
                    if (date < new DateTime(1900, 1, 1))
                    {
                        writer.WriteAttributeString("t", "d");
                        writer.WriteElementString("v", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        writer.WriteAttributeString("s", "1");
                        var serial = (date - new DateTime(1899, 12, 31)).Days + (date >= new DateTime(1900, 3, 1) ? 1 : 0);
                        writer.WriteElementString("v", serial.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                    WriteText(writer, value);
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteText(XmlWriter writer, string text)
    {
        if (text.Length > 32767)
            throw new FormatException("Текст превышает предел Excel: 32 767 символов в ячейке.");
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
        writer.WriteString(EncodeText(text));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    internal static int SignificantDigits(string number) => number.TrimStart('-', '+').Replace(".", string.Empty).Trim('0').Length;

    internal static string ColumnName(int column)
    {
        var name = string.Empty;
        do
        {
            name = (char)('A' + column % 26) + name;
            column = column / 26 - 1;
        } while (column >= 0);
        return name;
    }

    internal static string DecodeText(string text)
    {
        var decoded = Regex.Replace(text, "_x([0-9a-fA-F]{4})_", match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        for (var index = 0; index < decoded.Length; index++)
        {
            if (char.IsHighSurrogate(decoded[index]) && index + 1 < decoded.Length && char.IsLowSurrogate(decoded[index + 1]))
                index++;
            else if (char.IsSurrogate(decoded[index]))
                throw new FormatException("Текст XLSX содержит некорректный символ Unicode.");
        }
        return decoded;
    }

    private static string EncodeText(string text)
    {
        text = Regex.Replace(text, "_x[0-9a-fA-F]{4}_", match => "_x005F_" + match.Value[1..]);
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                result.Append(character).Append(text[++index]);
                continue;
            }
            if (char.IsSurrogate(character))
                throw new FormatException("Текст содержит некорректный символ Unicode.");
            result.Append(XmlConvert.IsXmlChar(character) && character != '\r' ? character.ToString() : $"_x{(int)character:X4}_");
        }
        return result.ToString();
    }

    private sealed record PartRelationship(string Id, string Type, string Target);

    private static PartRelationship[] ReadRelationships(XElement root)
    {
        if (root.Name.LocalName != "Relationships")
            throw new FormatException("Неверная структура связей XLSX.");
        var result = root.Elements().Select(element =>
        {
            if ((string?)element.Attribute("TargetMode") == "External")
                throw new FormatException("XLSX содержит внешние ссылки. Импортируйте копию без внешних ссылок.");
            return new PartRelationship((string?)element.Attribute("Id") ?? throw new FormatException("Связь XLSX без идентификатора."),
                (string?)element.Attribute("Type") ?? throw new FormatException("Связь XLSX без типа."),
                (string?)element.Attribute("Target") ?? throw new FormatException("Связь XLSX без адреса."));
        }).ToArray();
        if (result.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Length)
            throw new FormatException("Идентификаторы связей XLSX повторяются.");
        return result;
    }

    private static string ResolvePart(string ownerPart, string target)
    {
        if (target.Contains('\\') || target.Contains('#') || target.Contains('?') || Uri.TryCreate(target, UriKind.Absolute, out var absolute) && !target.StartsWith('/'))
            throw new FormatException("Недопустимый адрес части XLSX.");
        var segments = new List<string>();
        var combined = target.StartsWith('/') ? target[1..] : ownerPart[..(ownerPart.LastIndexOf('/') + 1)] + target;
        foreach (var segment in Uri.UnescapeDataString(combined).Split('/'))
        {
            if (segment is "" or ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new FormatException("Адрес части XLSX выходит за пределы книги.");
                segments.RemoveAt(segments.Count - 1);
            }
            else
                segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static string RelationshipPart(string part) => part[..(part.LastIndexOf('/') + 1)] + "_rels/" + part[(part.LastIndexOf('/') + 1)..] + ".rels";

    private static XElement ReadXml(ZipArchive archive, string part)
    {
        var entry = archive.GetEntry(part) ?? throw new FormatException($"В XLSX отсутствует часть «{part}».");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPackageBytes };
        // Limit nesting before constructing an XML tree; both reads are bounded and never resolve entities.
        using (var checkedStream = entry.Open())
        using (var checkedReader = XmlReader.Create(checkedStream, settings))
            while (checkedReader.Read())
                if (checkedReader.Depth > 64)
                    throw new FormatException("XML в XLSX превышает допустимую глубину 64.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static XlsxSharedString[] ReadSharedStrings(XElement root)
    {
        var ns = root.Name.Namespace;
        return root.Elements(ns + "si").Select(element => new XlsxSharedString(
            DecodeText(string.Concat(element.Elements().Where(child => child.Name.LocalName is "t" or "r").Select(child => child.Name.LocalName == "t" ? child.Value : string.Concat(child.Elements(ns + "t").Select(text => text.Value))))),
            element.Element(ns + "r") is not null || element.Element(ns + "rPh") is not null)).ToArray();
    }

    private static XlsxCellStyle[] ReadStyles(XElement root)
    {
        var ns = root.Name.Namespace;
        var formats = new Dictionary<int, string>();
        foreach (var format in root.Element(ns + "numFmts")?.Elements(ns + "numFmt") ?? [])
            formats.Add(ParseInteger((string?)format.Attribute("numFmtId"), "формат числа"), (string?)format.Attribute("formatCode") ?? throw new FormatException("Формат числа XLSX без описания."));
        var styles = (root.Element(ns + "cellXfs")?.Elements(ns + "xf") ?? []).Select(style =>
        {
            var id = ParseInteger((string?)style.Attribute("numFmtId") ?? "0", "формат числа");
            if (!formats.TryGetValue(id, out var code))
            {
                if (id is >= 14 and <= 17 or >= 27 and <= 31 or 36 or 50 or 51 or 54 or 57 or 58 or >= 71 and <= 74 or 81)
                    return new XlsxCellStyle(true, false, true);
                // Some locale-dependent IDs may mean either dates or times. Require an explicit format.
                if (id is >= 18 and <= 22 or >= 32 and <= 35 or >= 45 and <= 47 or 52 or 53 or 55 or 56 or >= 75 and <= 80)
                    return new XlsxCellStyle(false, true, true);
                if (id >= 82)
                    throw new FormatException("В XLSX отсутствует описание пользовательского числового формата.");
                return new XlsxCellStyle(false, false, id != 0);
            }
            if (code.Length > 255)
                throw new FormatException("Числовой формат XLSX превышает поддерживаемый предел 255 символов.");
            var elapsed = Regex.IsMatch(code, "\\[[hms]+\\]", RegexOptions.IgnoreCase);
            var stripped = Regex.Replace(code, "\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]|_.|\\*.", string.Empty).ToLowerInvariant();
            if (stripped.IndexOfAny(['ว', 'ด', 'ป', 'ช', 'น', 'ท']) >= 0 || stripped.Contains('b'))
                return new XlsxCellStyle(false, true, true);
            var time = elapsed || stripped.Contains('h') || stripped.Contains('s');
            var date = stripped.Contains('y') || stripped.Contains('d') || stripped.Contains('m');
            if (date && stripped.Contains(';'))
                time = true;
            return new XlsxCellStyle(date && !time, time, true);
        }).ToArray();
        return styles.Length == 0 ? [XlsxCellStyle.Default] : styles;
    }

    internal static int ParseInteger(string? value, string description)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0)
            throw new FormatException($"Некорректное значение XLSX: {description}.");
        return number;
    }

    private static XElement ContentType(string part, string type) => new(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));

    private static XElement Relationships(IEnumerable<(string Id, string Type, string Target)> relationships) => new(XNamespace.Get(RelationshipsNamespace) + "Relationships",
        relationships.Select(item => new XElement(XNamespace.Get(RelationshipsNamespace) + "Relationship", new XAttribute("Id", item.Id), new XAttribute("Type", OfficeRelationshipsNamespace + "/" + item.Type), new XAttribute("Target", item.Target))));

    private static XElement ExportStyles()
    {
        XNamespace ns = SpreadsheetNamespace;
        return new XElement(ns + "styleSheet",
            new XElement(ns + "numFmts", new XAttribute("count", 1), new XElement(ns + "numFmt", new XAttribute("numFmtId", 164), new XAttribute("formatCode", "yyyy-mm-dd"))),
            new XElement(ns + "fonts", new XAttribute("count", 1), new XElement(ns + "font", new XElement(ns + "sz", new XAttribute("val", 11)), new XElement(ns + "name", new XAttribute("val", "Calibri")))),
            new XElement(ns + "fills", new XAttribute("count", 2), new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "none"))), new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(ns + "borders", new XAttribute("count", 1), new XElement(ns + "border", new XElement(ns + "left"), new XElement(ns + "right"), new XElement(ns + "top"), new XElement(ns + "bottom"), new XElement(ns + "diagonal"))),
            new XElement(ns + "cellStyleXfs", new XAttribute("count", 1), new XElement(ns + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            new XElement(ns + "cellXfs", new XAttribute("count", 2), new XElement(ns + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0)), new XElement(ns + "xf", new XAttribute("numFmtId", 164), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyNumberFormat", 1))),
            new XElement(ns + "cellStyles", new XAttribute("count", 1), new XElement(ns + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
    }

    private static XmlWriter CreateWriter(Stream stream) => XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true });

    private static void WritePart(ZipArchive archive, string part, XElement root)
    {
        using var writer = CreateWriter(archive.CreateEntry(part).Open());
        root.Save(writer);
    }
}

internal sealed record XlsxSharedString(string Text, bool RichText);
internal sealed record XlsxCellStyle(bool Date, bool UnsupportedTime, bool DisplayFormat)
{
    internal static XlsxCellStyle Default { get; } = new(false, false, false);
}
