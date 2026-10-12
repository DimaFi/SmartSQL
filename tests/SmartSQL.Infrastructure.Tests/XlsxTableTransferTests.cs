using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure.Tests;

public sealed class XlsxTableTransferTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Xunit.Fact]
    public void RoundTripPreservesTypedValuesExactNumbersLiteralFormulaTextUnicodeAndNulls()
    {
        var workspace = NewWorkspace();
        FieldInput[] fields = [new("Текст", FieldType.Text), new("Число", FieldType.Number), new("Дата", FieldType.Date), new("Флаг", FieldType.Boolean)];
        var source = workspace.CreateTable("Исходные/данные", fields);
        var sourceRecord = workspace.AddRecord(source.Id, new Dictionary<Guid, string?>
        {
            [source.Fields[0].Id] = "  =SUM(A1:A2)\r\n🌍 _x0041_ \u0001  ",
            [source.Fields[1].Id] = "79228162514264337593543950335",
            [source.Fields[2].Id] = "29.02.2024",
            [source.Fields[3].Id] = "Да"
        });
        workspace.AddRecord(source.Id, new Dictionary<Guid, string?>());
        using var output = new MemoryStream();
        var result = XlsxTableTransfer.Export(workspace, source.Id, output);
        Xunit.Assert.Equal(new XlsxExportResult(2, 1, "Исходные_данные", true), result);
        using (var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XElement.Load(sheetStream);
            XNamespace ns = Ns;
            Xunit.Assert.Empty(sheet.Descendants(ns + "f"));
            Xunit.Assert.Contains(sheet.Descendants(ns + "c"), cell => (string?)cell.Attribute("t") == "b");
            Xunit.Assert.Contains(sheet.Descendants(ns + "c"), cell => (string?)cell.Attribute("s") == "1");
        }
        output.Position = 0;
        var book = XlsxTableTransfer.Read(output);
        var name = Xunit.Assert.Single(book.Worksheets);
        var target = workspace.CreateTable("Приёмник", fields);
        var import = book.Inspect(target, name);
        Xunit.Assert.Equal(2, import.RowCount);
        Xunit.Assert.Equal(0, workspace.CountRecords(target.Id));
        var preview = import.Prepare(import.SuggestMapping());
        Xunit.Assert.Equal(2, preview.Commit(workspace));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        var records = workspace.GetRecords(target.Id);
        var actual = Xunit.Assert.Single(records, row => row.Values[target.Fields[0].Id] is not null);
        for (var index = 0; index < fields.Length; index++)
            Xunit.Assert.Equal(sourceRecord.Values[source.Fields[index].Id], actual.Values[target.Fields[index].Id]);
        Xunit.Assert.All(Xunit.Assert.Single(records, row => row.Id != actual.Id).Values.Values, Xunit.Assert.Null);
    }

    [Xunit.Theory]
    [Xunit.InlineData("123456789012345", false)]
    [Xunit.InlineData("1234567890123456", true)]
    [Xunit.InlineData("0.0000000000000000000000000001", false)]
    [Xunit.InlineData("0.1234567890123456789012345678", true)]
    [Xunit.InlineData("12345678901234500000000000000", false)]
    public void ExportStoresNumbersExceedingExcelPrecisionAsExactText(string number, bool text)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Числа", [new FieldInput("Число", FieldType.Number)]);
        workspace.AddRecord(table.Id, new Dictionary<Guid, string?> { [table.Fields[0].Id] = number });
        using var output = new MemoryStream();
        var report = XlsxTableTransfer.Export(workspace, table.Id, output);
        Xunit.Assert.Equal(text ? 1 : 0, report.NumbersStoredAsText);
        output.Position = 0;
        var book = XlsxTableTransfer.Read(output);
        var source = book.Inspect(table, book.Worksheets[0]);
        Xunit.Assert.Equal(number, Xunit.Assert.Single(source.SampleRows)[0]);
    }

    [Xunit.Theory]
    [Xunit.InlineData("Данные", "Данные", false)]
    [Xunit.InlineData("Таблица/оборудование", "Таблица_оборудование", true)]
    [Xunit.InlineData("1234567890123456789012345678901234567890", "1234567890123456789012345678901", true)]
    [Xunit.InlineData("123456789012345678901234567890🌍", "123456789012345678901234567890", true)]
    public void WorksheetNameChangesAreReportedAndNeverSplitUnicode(string name, string expected, bool changed)
    {
        var workspace = NewWorkspace();
        var table = workspace.CreateTable(name, ["Значение"]);
        using var output = new MemoryStream();
        var result = XlsxTableTransfer.Export(workspace, table.Id, output);
        Xunit.Assert.Equal(expected, result.WorksheetName);
        Xunit.Assert.Equal(changed, result.WorksheetNameChanged);
        output.Position = 0;
        Xunit.Assert.Equal(expected, Xunit.Assert.Single(XlsxTableTransfer.Read(output).Worksheets));
    }

    [Xunit.Fact]
    public void WorksheetChoiceKeepsDataAndPreviewIndependent()
    {
        using var package = Package(
            "<row r='1'><c r='A1' t='inlineStr'><is><t>Имя</t></is></c></row><row r='2'><c r='A2' t='inlineStr'><is><t>Анна</t></is></c></row>",
            secondSheet: "<row r='1'><c r='A1' t='inlineStr'><is><t>Имя</t></is></c></row><row r='2'><c r='A2' t='inlineStr'><is><t>Мария</t></is></c></row>");
        var book = XlsxTableTransfer.Read(package);
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", ["Имя"]);
        Xunit.Assert.Equal(["Первый", "Второй"], book.Worksheets);
        var source = book.Inspect(table, "Второй");
        source.Prepare(source.SuggestMapping()).Commit(workspace);
        Xunit.Assert.Equal("Мария", Xunit.Assert.Single(workspace.GetRecords(table.Id)).Values[table.Fields[0].Id]);
        Xunit.Assert.Throws<ArgumentException>(() => book.Inspect(table, "Отсутствует"));
    }

    [Xunit.Fact]
    public void SharedStringsAndSparseCellsRespectCoordinatesAndMapping()
    {
        using var package = Package("<row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1' t='s'><v>1</v></c></row><row r='2'><c r='B2'><v>1.25E+3</v></c></row><row r='4'><c r='A4' t='s'><v>2</v></c></row>",
            sharedStrings: "<si><t>Имя</t></si><si><t>Возраст</t></si><si><r><t>Ан</t></r><r><t>на</t></r></si>");
        var book = XlsxTableTransfer.Read(package);
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Люди", [new FieldInput("Возраст", FieldType.Number), new FieldInput("Имя", FieldType.Text)]);
        var source = book.Inspect(table, "Первый");
        Xunit.Assert.Equal(3, source.RowCount);
        Xunit.Assert.Equal("1250", source.SampleRows[0][1]);
        Xunit.Assert.Equal(["", ""], source.SampleRows[1]);
        Xunit.Assert.Equal("Анна", source.SampleRows[2][0]);
        Xunit.Assert.Contains(book.GetWarnings("Первый"), warning => warning.Contains("Форматированный текст", StringComparison.Ordinal));
        Xunit.Assert.Equal(3, source.Prepare(source.SuggestMapping()).Commit(workspace));
    }

    [Xunit.Theory]
    [Xunit.InlineData("59", false, "1900-02-28")]
    [Xunit.InlineData("61", false, "1900-03-01")]
    [Xunit.InlineData("0", true, "1904-01-01")]
    [Xunit.InlineData("1", true, "1904-01-02")]
    public void NumericExcelDatesRespectBothDateSystems(string serial, bool date1904, string expected)
    {
        using var package = Package(Header + $"<row r='2'><c r='A2' s='1'><v>{serial}</v></c></row>", styles: DateStyles, date1904: date1904);
        var book = XlsxTableTransfer.Read(package);
        Xunit.Assert.Equal(expected, Xunit.Assert.Single(book.InspectNewTable("Первый").SampleRows)[0]);
    }

    [Xunit.Theory]
    [Xunit.InlineData("<c r='A3'><f>1+1</f><v>2</v></c>", "Формулы")]
    [Xunit.InlineData("<c r='A3' t='e'><v>#DIV/0!</v></c>", "ошибку")]
    [Xunit.InlineData("<c r='A3' t='b'><v>2</v></c>", "Да/Нет")]
    [Xunit.InlineData("<c r='A3' t='s'><v>99</v></c>", "словаря")]
    [Xunit.InlineData("<c r='A3' t='unknown'><v>12</v></c>", "Тип ячейки")]
    [Xunit.InlineData("<c r='A3'><v>1e-29</v></c>", "точност")]
    [Xunit.InlineData("<c r='A3'><v>0.123456789012345678901234567891</v></c>", "точность")]
    [Xunit.InlineData("<c r='A3' t='inlineStr'><is><t>   </t></is></c>", "пробельную")]
    [Xunit.InlineData("<c r='A3' cm='1'><v>1</v></c>", "типы")]
    [Xunit.InlineData("<c r='A3' t='inlineStr'><is><t>Текст</t></is><v>1</v></c>", "противоречит")]
    [Xunit.InlineData("<c r='A3'><is><t>Текст</t></is></c>", "противоречит")]
    [Xunit.InlineData("<c r='A3' t='inlineStr'><is><t>_xD800_</t></is></c>", "Unicode")]
    [Xunit.InlineData("<c r='A3' t='str'><v>_xDC00_</v></c>", "Unicode")]
    public void UnsupportedValuesRejectTheWholeSelectionBeforeAnyImport(string cell, string message)
    {
        using var package = Package(Header + "<row r='2'><c r='A2'><v>1</v></c></row><row r='3'>" + cell + "</row>");
        var book = XlsxTableTransfer.Read(package);
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Данные", ["Значение"]);
        var error = Xunit.Assert.Throws<FormatException>(() => book.Inspect(table, "Первый"));
        Xunit.Assert.Contains("A3", error.Message);
        Xunit.Assert.Contains(message, error.Message);
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
    }

    [Xunit.Theory]
    [Xunit.InlineData("60")]
    [Xunit.InlineData("61.5")]
    [Xunit.InlineData("-1")]
    [Xunit.InlineData("4000000")]
    public void InvalidOrTimeBearingDatesCannotLoseInformation(string serial)
    {
        using var package = Package(Header + $"<row r='2'><c r='A2' s='1'><v>{serial}</v></c></row>", styles: DateStyles);
        var book = XlsxTableTransfer.Read(package);
        Xunit.Assert.Throws<FormatException>(() => book.InspectNewTable("Первый"));
    }

    [Xunit.Theory]
    [Xunit.InlineData(18)]
    [Xunit.InlineData(22)]
    [Xunit.InlineData(32)]
    [Xunit.InlineData(33)]
    [Xunit.InlineData(34)]
    [Xunit.InlineData(35)]
    [Xunit.InlineData(52)]
    [Xunit.InlineData(53)]
    [Xunit.InlineData(55)]
    [Xunit.InlineData(56)]
    [Xunit.InlineData(75)]
    [Xunit.InlineData(77)]
    [Xunit.InlineData(80)]
    public void KnownTimeAndAmbiguousLocaleFormatsCannotBeMistakenForDates(int format)
    {
        var styles = $"<cellXfs count='2'><xf numFmtId='0'/><xf numFmtId='{format}'/></cellXfs>";
        using var package = Package(Header + "<row r='2'><c r='A2' s='1'><v>1</v></c></row>", styles: styles);
        var error = Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(package).InspectNewTable("Первый"));
        Xunit.Assert.Contains("времени", error.Message);
    }

    [Xunit.Fact]
    public void EncodedSurrogatePairIsDecodedAsOneUnicodeCharacter()
    {
        using var package = Package(Header + "<row r='2'><c r='A2' t='inlineStr'><is><t>_xD83C__xDF0D_</t></is></c></row>");
        var sample = XlsxTableTransfer.Read(package).InspectNewTable("Первый").SampleRows;
        Xunit.Assert.Equal("🌍", Xunit.Assert.Single(sample)[0]);
    }

    [Xunit.Fact]
    public void OversizedCustomNumberFormatIsRejectedBeforeRegexClassification()
    {
        var styles = "<numFmts count='1'><numFmt numFmtId='164' formatCode='" + new string('[', 256) + "'/></numFmts><cellXfs count='1'><xf numFmtId='164'/></cellXfs>";
        using var package = Package(Header, styles: styles);
        Xunit.Assert.Contains("255", Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(package)).Message);
    }

    [Xunit.Fact]
    public void FormulaOnAnotherSheetDoesNotPreventReadingTheSelectedPlainSheet()
    {
        using var package = Package(Header + "<row r='2'><c r='A2'><v>1</v></c></row>",
            secondSheet: Header + "<row r='2'><c r='A2'><f>1+1</f><v>2</v></c></row>");
        var book = XlsxTableTransfer.Read(package);
        Xunit.Assert.Equal(1, book.InspectNewTable("Первый").RowCount);
        Xunit.Assert.Throws<FormatException>(() => book.InspectNewTable("Второй"));
    }

    [Xunit.Theory]
    [Xunit.InlineData("<row r='1'><c r='A1' t='inlineStr'><is><t>Значение</t></is></c><c r='A1'><v>1</v></c></row>")]
    [Xunit.InlineData(Header + "<row r='2'><c r='B2'><v>1</v></c></row>")]
    [Xunit.InlineData(Header + "<row r='2'><c r='A3'><v>1</v></c></row>")]
    [Xunit.InlineData(Header + "<row r='100002'><c r='A100002'><v>1</v></c></row>")]
    [Xunit.InlineData(Header + "<row r='2'><c r='XFE2'><v>1</v></c></row>")]
    public void MalformedCoordinatesAndLimitViolationsAreRejected(string rows)
    {
        using var package = Package(rows);
        var book = XlsxTableTransfer.Read(package);
        Xunit.Assert.Throws<FormatException>(() => book.InspectNewTable("Первый"));
    }

    [Xunit.Fact]
    public void UnsupportedPresentationCapabilitiesAreDisclosed()
    {
        using var package = Package(Header + "<row r='2' hidden='1'><c r='A2'><v>1</v></c></row>",
            extraSheetXml: "<autoFilter ref='A1:A2'/><dataValidations count='1'><dataValidation type='whole' sqref='A2'/></dataValidations><sheetProtection sheet='1'/><drawing r:id='drawing1'/>");
        var warnings = XlsxTableTransfer.Read(package).GetWarnings("Первый");
        Xunit.Assert.Contains(warnings, warning => warning.Contains("Скрытые строки", StringComparison.Ordinal));
        Xunit.Assert.Contains(warnings, warning => warning.Contains("Фильтр", StringComparison.Ordinal));
        Xunit.Assert.Contains(warnings, warning => warning.Contains("проверки данных", StringComparison.Ordinal));
        Xunit.Assert.Contains(warnings, warning => warning.Contains("Защита", StringComparison.Ordinal));
        Xunit.Assert.Contains(warnings, warning => warning.Contains("Диаграммы", StringComparison.Ordinal));
    }

    [Xunit.Fact]
    public void MergedCellsFailExplicitly()
    {
        using var package = Package(Header, extraSheetXml: "<mergeCells count='1'><mergeCell ref='A1:B1'/></mergeCells>");
        var error = Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(package).GetCsvContent("Первый"));
        Xunit.Assert.Contains("объединённые", error.Message);
    }

    [Xunit.Fact]
    public void ExternalRelationshipsAndXmlEntitiesAreRejected()
    {
        using var external = Package(Header, sheetRelationships: "<Relationship Id='link' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink' Target='https://example.com' TargetMode='External'/>");
        Xunit.Assert.Contains("внешние ссылки", Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(external)).Message);
        using var entity = Package(Header, rawWorksheet: "<!DOCTYPE worksheet [<!ENTITY x SYSTEM 'file:///D:/private'>]><worksheet xmlns='" + Ns + "'><sheetData>&x;</sheetData></worksheet>");
        Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(entity));
        using var notZip = new MemoryStream(Encoding.UTF8.GetBytes("not a workbook"));
        Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(notZip));
    }

    [Xunit.Fact]
    public void ZipExpansionIsCappedBeforeWorksheetParsing()
    {
        using var package = Package(Header);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        using (var extra = archive.CreateEntry("oversized.xml").Open())
        {
            var chunk = new byte[1_000_000];
            for (var index = 0; index < 101; index++)
                extra.Write(chunk);
        }
        package.Position = 0;
        Xunit.Assert.Contains("100 МБ", Xunit.Assert.Throws<FormatException>(() => XlsxTableTransfer.Read(package)).Message);
    }

    [Xunit.Fact]
    public void SchemaChangeAfterPreviewRejectsImportAndNewTableUsesAtomicContract()
    {
        using var package = Package(Header + "<row r='2'><c r='A2'><v>123</v></c></row>");
        var book = XlsxTableTransfer.Read(package);
        var workspace = NewWorkspace();
        var table = workspace.CreateTable("Старая", ["Значение"]);
        var source = book.Inspect(table, "Первый");
        var preview = source.Prepare(source.SuggestMapping());
        workspace.AddField(table.Id, new FieldInput("Новое", FieldType.Text));
        Xunit.Assert.Throws<InvalidOperationException>(() => preview.Commit(workspace));
        Xunit.Assert.Equal(0, workspace.CountRecords(table.Id));
        var newPreview = book.InspectNewTable("Первый").PrepareNewTable("Новая", [new FieldInput("Значение", FieldType.Number)]);
        var created = newPreview.Commit(workspace);
        Xunit.Assert.Equal("123", Xunit.Assert.Single(workspace.GetRecords(created.Id)).Values[created.Fields[0].Id]);
    }

    private const string Header = "<row r='1'><c r='A1' t='inlineStr'><is><t>Значение</t></is></c></row>";
    private const string DateStyles = "<numFmts count='1'><numFmt numFmtId='164' formatCode='yyyy-mm-dd'/></numFmts><cellXfs count='2'><xf numFmtId='0'/><xf numFmtId='164'/></cellXfs>";

    private static SqliteWorkspace NewWorkspace() => new(Path.Combine(TestPaths.Root, "smartsql-tests", $"{Guid.NewGuid():N}.db"));

    private static MemoryStream Package(string rows, string? secondSheet = null, string? sharedStrings = null,
        string? styles = null, bool date1904 = false, string extraSheetXml = "", string? sheetRelationships = null, string? rawWorksheet = null)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/></Types>");
            Add(archive, "_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='book' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>");
            var additionalSheets = secondSheet is null ? "" : "<sheet name='Второй' sheetId='2' r:id='sheet2'/>";
            Add(archive, "xl/workbook.xml", $"<workbook xmlns='{Ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><workbookPr date1904='{(date1904 ? 1 : 0)}'/><sheets><sheet name='Первый' sheetId='1' r:id='sheet1'/>{additionalSheets}</sheets></workbook>");
            var relations = "<Relationship Id='sheet1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/>";
            if (secondSheet is not null)
                relations += "<Relationship Id='sheet2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet2.xml'/>";
            if (sharedStrings is not null)
            {
                relations += "<Relationship Id='strings' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings' Target='sharedStrings.xml'/>";
                Add(archive, "xl/sharedStrings.xml", $"<sst xmlns='{Ns}'>{sharedStrings}</sst>");
            }
            if (styles is not null)
            {
                relations += "<Relationship Id='styles' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles' Target='styles.xml'/>";
                Add(archive, "xl/styles.xml", $"<styleSheet xmlns='{Ns}'>{styles}</styleSheet>");
            }
            Add(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + relations + "</Relationships>");
            Add(archive, "xl/worksheets/sheet1.xml", rawWorksheet ?? $"<worksheet xmlns='{Ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheetData>{rows}</sheetData>{extraSheetXml}</worksheet>");
            if (secondSheet is not null)
                Add(archive, "xl/worksheets/sheet2.xml", $"<worksheet xmlns='{Ns}'><sheetData>{secondSheet}</sheetData></worksheet>");
            if (sheetRelationships is not null)
                Add(archive, "xl/worksheets/_rels/sheet1.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + sheetRelationships + "</Relationships>");
        }
        output.Position = 0;
        return output;
    }

    private static void Add(ZipArchive archive, string part, string xml)
    {
        using var writer = new StreamWriter(archive.CreateEntry(part).Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }
}
