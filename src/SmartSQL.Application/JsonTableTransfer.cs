using System.Globalization;
using System.Text.Json;
using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Portable, typed JSON exchange for one existing table.</summary>
public static class JsonTableTransfer
{
    private const string Format = "SmartSQL.Table.v1";
    private const int MaxImportRows = 100_000;
    private const int MaxImportCharacters = 50_000_000;

    public static int Export(IDataWorkspace workspace, Guid tableId, Stream output)
    {
        var table = workspace.GetTables().FirstOrDefault(item => item.Id == tableId)
            ?? throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        if (table.Fields.Any(field => !Enum.IsDefined(field.Type)))
            throw new FormatException("Тип поля не поддерживается форматом SmartSQL.Table.v1.");
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", Format);
        writer.WriteString("table", table.Name);
        writer.WriteStartArray("fields");
        foreach (var field in table.Fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("type", field.Type.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("records");
        var count = 0;
        while (true)
        {
            var page = workspace.GetRecords(tableId, count, 500);
            if (page.Count == 0)
                break;
            foreach (var record in page)
            {
                writer.WriteStartArray();
                foreach (var field in table.Fields)
                    WriteValue(writer, field.Type, record.Values.GetValueOrDefault(field.Id));
                writer.WriteEndArray();
                count++;
            }
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return count;
    }

    public static JsonTableImportPreview PrepareImport(IDataWorkspace workspace, Guid tableId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxImportCharacters)
            throw new FormatException("JSON-файл пуст или превышает предел 50 миллионов символов.");
        if (content[0] == '\uFEFF')
            content = content[1..];
        var table = workspace.GetTables().FirstOrDefault(item => item.Id == tableId)
            ?? throw new ArgumentException("Таблица не найдена.", nameof(tableId));
        using var document = ParseDocument(content);
        var root = document.RootElement;
        ValidateProperties(root, ["format", "table", "fields", "records"], "Документ JSON");
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String ||
            format.GetString() != Format ||
            !root.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("records", out var recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("Ожидается JSON формата SmartSQL.Table.v1 с полями и записями.");
        if (root.TryGetProperty("table", out var tableName) &&
            (tableName.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tableName.GetString())))
            throw new FormatException("Название таблицы в JSON должно быть непустой строкой.");

        if (fieldsElement.GetArrayLength() != table.Fields.Count)
            throw new FormatException("Набор полей JSON не совпадает с выбранной таблицей.");
        var sourceFields = fieldsElement.EnumerateArray().Select(ReadField).ToArray();
        if (sourceFields.Select(field => field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceFields.Length)
            throw new FormatException("Набор полей JSON не совпадает с выбранной таблицей.");
        var mapped = sourceFields.Select(source => table.Fields.FirstOrDefault(target =>
            string.Equals(source.Name, target.Name, StringComparison.OrdinalIgnoreCase) && source.Type == target.Type)).ToArray();
        if (mapped.Any(field => field is null))
            throw new FormatException("Названия или типы полей JSON не совпадают с выбранной таблицей. Данные не импортированы.");

        var rows = new List<IReadOnlyDictionary<Guid, string?>>();
        var samples = new List<IReadOnlyList<string>>();
        if (recordsElement.GetArrayLength() > MaxImportRows)
            throw new FormatException($"За один раз можно импортировать не более {MaxImportRows:N0} строк.");
        foreach (var record in recordsElement.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Array || record.GetArrayLength() != sourceFields.Length)
                throw new FormatException($"Запись {rows.Count + 1}: число значений не совпадает с числом полей.");
            var values = new Dictionary<Guid, string?>();
            var sample = new string[sourceFields.Length];
            var index = 0;
            foreach (var value in record.EnumerateArray())
            {
                var field = mapped[index]!;
                try
                {
                    var parsed = ReadValue(field.Type, value);
                    values[field.Id] = parsed;
                    sample[index] = parsed ?? string.Empty;
                }
                catch (FormatException exception)
                {
                    throw new FormatException($"Запись {rows.Count + 1}, поле «{field.Name}»: {exception.Message}", exception);
                }
                index++;
            }
            rows.Add(values);
            if (samples.Count < 5)
                samples.Add(sample);
        }
        return new JsonTableImportPreview(table, sourceFields.Select(field => field.Name).ToArray(), samples, rows);
    }

    private static (string Name, FieldType Type) ReadField(JsonElement element)
    {
        ValidateProperties(element, ["name", "type"], "Описание поля JSON");
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()) ||
            !element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<FieldType>(type.GetString(), ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed) || type.GetString() != parsed.ToString())
            throw new FormatException("Описание поля JSON содержит неверное название или тип.");
        return (name.GetString()!, parsed);
    }

    private static string? ReadValue(FieldType type, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && type == FieldType.Number)
            return FieldValue.NormalizeNumber(value.GetRawText(), allowExponent: true);
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False && type == FieldType.Boolean)
            return value.GetBoolean() ? "1" : "0";
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Пустые и пробельные строки нельзя сохранить без изменения. Для пустого значения используйте null.");
            return FieldValue.Normalize(type, text);
        }
        throw new FormatException("Тип значения JSON не соответствует типу поля.");
    }

    private static JsonDocument ParseDocument(string content)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException exception)
        {
            throw new FormatException("JSON содержит синтаксическую ошибку или превышает допустимую глубину вложенности.", exception);
        }
    }

    private static void ValidateProperties(JsonElement element, IReadOnlyList<string> allowed, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{description}: ожидается объект.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new FormatException($"{description}: свойство «{property.Name}» указано несколько раз.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new FormatException($"{description}: неизвестное свойство «{property.Name}». Данные не импортированы.");
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, FieldType type, string? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        if (type == FieldType.Number)
            writer.WriteNumberValue(decimal.Parse(FieldValue.NormalizeNumber(value, allowExponent: true), CultureInfo.InvariantCulture));
        else if (type == FieldType.Boolean)
            writer.WriteBooleanValue(value switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException("В базе найдено неверное значение Да/Нет.")
            });
        else if (type is FieldType.Text or FieldType.Date)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("В базе найдено пустое или пробельное значение, которое нельзя перенести без изменения.");
            if (type == FieldType.Date && FieldValue.Normalize(type, value) != value)
                throw new FormatException("В базе найдено значение даты в неподдерживаемом формате.");
            writer.WriteStringValue(value);
        }
        else
            throw new FormatException("Тип поля не поддерживается форматом SmartSQL.Table.v1.");
    }
}
