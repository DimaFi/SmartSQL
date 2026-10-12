using System.Globalization;
using System.Text.Json;

namespace SmartSql.LocalAssistant;

internal static class IntentParser
{
    private static readonly IReadOnlyDictionary<string, FilterOperator> Operators =
        new Dictionary<string, FilterOperator>(StringComparer.Ordinal)
        {
            ["equal"] = FilterOperator.Equal,
            ["notEqual"] = FilterOperator.NotEqual,
            ["contains"] = FilterOperator.Contains,
            ["greaterThan"] = FilterOperator.GreaterThan,
            ["greaterThanOrEqual"] = FilterOperator.GreaterThanOrEqual,
            ["lessThan"] = FilterOperator.LessThan,
            ["lessThanOrEqual"] = FilterOperator.LessThanOrEqual
        };

    public static AssistantDecision ParseAndValidate(string response, AssistantRequest request)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryString(root, "decision", out var decision))
            {
                return Refuse("Модель вернула ответ неизвестного формата.");
            }

            if (decision == "refuse")
            {
                return TryString(root, "reason", out var reason) && !string.IsNullOrWhiteSpace(reason)
                    ? Refuse(reason)
                    : Refuse("Модель отказала без пояснения.");
            }

            if (decision == "explain")
            {
                return TryString(root, "topic", out var topic) && !string.IsNullOrWhiteSpace(topic) &&
                       TryString(root, "explanation", out var explanation) && !string.IsNullOrWhiteSpace(explanation)
                    ? new ExplainedDecision(new ExplanationIntent(topic, explanation))
                    : Refuse("Модель вернула неполное объяснение.");
            }

            if (decision != "view")
            {
                return Refuse("Запрошенное действие не поддерживается: разрешён только просмотр.");
            }

            if (!request.Permissions.CanView)
            {
                return Refuse("У пользователя нет права на просмотр таблицы.");
            }

            if (!TryString(root, "table", out var table) ||
                !string.Equals(table, request.Schema.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Refuse("Модель указала неизвестную таблицу.");
            }

            var fields = ParseFields(root, request);
            if (fields is null || fields.Count == 0)
            {
                return Refuse("Ответ содержит неизвестные или недоступные поля.");
            }

            var filters = ParseFilters(root, request);
            if (filters is null)
            {
                return Refuse("Ответ содержит недопустимый фильтр.");
            }

            var sort = ParseSort(root, request);
            if (sort.Invalid)
            {
                return Refuse("Ответ содержит недопустимую сортировку.");
            }

            var limit = root.TryGetProperty("limit", out var limitElement) && limitElement.TryGetInt32(out var parsedLimit)
                ? parsedLimit
                : request.Permissions.MaximumRows;
            if (limit is <= 0 || limit > request.Permissions.MaximumRows)
            {
                return Refuse("Запрошено недопустимое число строк.");
            }

            return new AcceptedDecision(new ViewIntent(request.Schema.Name, fields, filters, sort.Value, limit));
        }
        catch (JsonException)
        {
            return Refuse("Модель вернула невалидный JSON.");
        }
    }

    private static IReadOnlyList<string>? ParseFields(JsonElement root, AssistantRequest request)
    {
        if (!root.TryGetProperty("fields", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || FindReadableField(item.GetString(), request) is not { } field)
            {
                return null;
            }

            if (!result.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(field.Name);
            }
        }

        return result;
    }

    private static IReadOnlyList<FilterClause>? ParseFilters(JsonElement root, AssistantRequest request)
    {
        if (!root.TryGetProperty("filters", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<FilterClause>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryString(item, "field", out var fieldName) ||
                FindReadableField(fieldName, request) is not { } field ||
                !TryString(item, "operator", out var operatorName) ||
                !Operators.TryGetValue(operatorName, out var filterOperator) ||
                !item.TryGetProperty("value", out var value) ||
                !IsValidFilter(field.Type, filterOperator, value))
            {
                return null;
            }

            result.Add(new FilterClause(field.Name, filterOperator, value.Clone()));
        }

        return result;
    }

    private static (bool Invalid, SortClause? Value) ParseSort(JsonElement root, AssistantRequest request)
    {
        if (!root.TryGetProperty("sort", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return (false, null);
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !TryString(element, "field", out var fieldName) ||
            FindReadableField(fieldName, request) is not { } field ||
            !TryString(element, "direction", out var direction) ||
            direction is not ("ascending" or "descending"))
        {
            return (true, null);
        }

        return (false, new SortClause(
            field.Name,
            direction == "ascending" ? SortDirection.Ascending : SortDirection.Descending));
    }

    private static FieldSchema? FindReadableField(string? name, AssistantRequest request) =>
        request.Schema.Fields.FirstOrDefault(field =>
            string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase) &&
            request.Permissions.ReadableFields.Contains(field.Name));

    private static bool IsValidFilter(FieldType type, FilterOperator filterOperator, JsonElement value) => type switch
    {
        FieldType.Text => filterOperator is FilterOperator.Equal or FilterOperator.NotEqual or FilterOperator.Contains &&
                          value.ValueKind == JsonValueKind.String,
        FieldType.Number => filterOperator is not FilterOperator.Contains && value.ValueKind == JsonValueKind.Number,
        FieldType.Date => filterOperator is not FilterOperator.Contains && value.ValueKind == JsonValueKind.String &&
                          DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        FieldType.Boolean => filterOperator is FilterOperator.Equal or FilterOperator.NotEqual &&
                             value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => false
    };

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = item.GetString() ?? string.Empty;
        return true;
    }

    private static RefusedDecision Refuse(string explanation) => new(explanation);
}
