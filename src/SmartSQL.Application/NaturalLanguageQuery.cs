using System.Text.RegularExpressions;
using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed record InterpretedQuery(IReadOnlyList<RecordFilter> Filters, string Description);

/// <summary>A small, deterministic Russian query grammar that produces structured filters.</summary>
public static class NaturalLanguageQuery
{
    private static readonly Regex ClausePattern = new(
        @"^(?<field>.+?)\s*(?<operation>не\s+пусто|пусто|содержит|больше|меньше|равно|=)\s*(?<value>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static InterpretedQuery Interpret(TableDefinition table, string request)
    {
        if (string.IsNullOrWhiteSpace(request))
            throw new FormatException("Напишите запрос, например: покажи записи, где Документы = Нет.");
        var text = request.Trim().TrimEnd('.', '?', '!').Trim();
        text = Regex.Replace(text, @"^(покажи|найди|выведи)\s+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^(мне\s+)?(все\s+)?(записи|строки)\s*,?\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^где\s+", "", RegexOptions.IgnoreCase);
        var clauses = Regex.Split(text, @"\s+и\s+", RegexOptions.IgnoreCase)
            .Where(clause => !string.IsNullOrWhiteSpace(clause)).ToArray();
        if (clauses.Length is < 1 or > 10)
            throw new FormatException("Запрос должен содержать от одного до десяти условий.");

        var filters = new List<RecordFilter>();
        var descriptions = new List<string>();
        foreach (var clause in clauses)
        {
            var match = ClausePattern.Match(clause.Trim());
            if (!match.Success)
                throw new FormatException($"Не удалось понять условие «{clause.Trim()}». Пример: Курс = 3.");
            var fieldName = match.Groups["field"].Value.Trim();
            var field = table.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"В таблице нет поля «{fieldName}».");
            var operationText = Regex.Replace(match.Groups["operation"].Value, @"\s+", " ").ToLowerInvariant();
            var operation = operationText switch
            {
                "=" or "равно" => FilterOperator.Equals,
                "содержит" => FilterOperator.Contains,
                "больше" => FilterOperator.GreaterThan,
                "меньше" => FilterOperator.LessThan,
                "пусто" => FilterOperator.IsEmpty,
                "не пусто" => FilterOperator.IsNotEmpty,
                _ => throw new FormatException("Неизвестная операция.")
            };
            if (operation == FilterOperator.Contains && field.Type != FieldType.Text ||
                operation is FilterOperator.GreaterThan or FilterOperator.LessThan && field.Type == FieldType.Boolean)
                throw new FormatException($"Операция «{operationText}» не подходит для поля «{field.Name}».");
            var value = operation is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty
                ? null : match.Groups["value"].Value.Trim();
            if (value is not null && FieldValue.Normalize(field.Type, value) is null)
                throw new FormatException($"Для поля «{field.Name}» нужно значение.");
            filters.Add(new RecordFilter(field.Id, operation, value));
            descriptions.Add(value is null ? $"{field.Name} {operationText}" : $"{field.Name} {operationText} {value}");
        }
        return new InterpretedQuery(filters, string.Join("; ", descriptions));
    }
}
