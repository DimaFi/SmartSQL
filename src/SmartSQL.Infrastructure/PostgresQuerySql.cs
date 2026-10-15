using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

/// <summary>Only exact pg_catalog builtin identities admit typed operators or casts.</summary>
internal static class PostgresQuerySql
{
    internal const string Columns = """
        SELECT a.attname::pg_catalog.text, t.oid, n.nspname::pg_catalog.text,
               t.typname::pg_catalog.text, t.typtype::pg_catalog.text
        FROM pg_catalog.pg_attribute AS a
        JOIN pg_catalog.pg_type AS t ON t.oid = a.atttypid
        JOIN pg_catalog.pg_namespace AS n ON n.oid = t.typnamespace
        WHERE a.attrelid = $1 AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attnum
        """;

    internal sealed record Column(string Name, DatabaseFilterValueKind Kind, string? Cast)
    {
        internal DatabaseQueryColumn Describe() => new(Name, Kind, Cast is not null,
            DatabaseQueryOperations.For(Kind), Cast is null
                ? "Тип не подтверждён как поддерживаемый встроенный тип PostgreSQL: доступны только проверки NULL." : null);
    }

    internal static Column Confirm(string name, uint oid, string schema, string nativeName, string category)
    {
        var cast = schema == "pg_catalog" && category == "b" ? (oid, nativeName) switch
        {
            (25, "text") => "text", (1043, "varchar") => "text",
            (21, "int2") => "int2", (23, "int4") => "int4", (20, "int8") => "int8",
            (1700, "numeric") => "numeric", (1082, "date") => "date", (16, "bool") => "bool",
            _ => null
        } : null;
        var kind = cast switch
        {
            "text" => DatabaseFilterValueKind.Text,
            "int2" or "int4" or "int8" => DatabaseFilterValueKind.Integer,
            "numeric" => DatabaseFilterValueKind.Number, "date" => DatabaseFilterValueKind.Date,
            "bool" => DatabaseFilterValueKind.Boolean, _ => DatabaseFilterValueKind.Unsupported
        };
        return new Column(name, kind, cast);
    }

    internal static DatabaseQueryCapabilities Describe(DatabaseTableRef table, IReadOnlyList<Column> columns) =>
        new(table, columns.Select(column => column.Describe()).ToArray(),
            [new DatabaseDiagnostic("typed_query_limits", "Фильтры объединяются И. Текст сравнивается буквально с сортировкой C; дата — YYYY-MM-DD, Boolean — true/false. Numeric передаётся точно, без округления .NET. Домены и другие неподдерживаемые типы допускают только проверки NULL.")]);

    internal static string Sort(DatabaseBrowseQuery query, IReadOnlyList<Column>? columns)
    {
        var column = columns?.SingleOrDefault(column => column.Name == query.SortColumn);
        if (column?.Cast is null)
            throw new DatabaseBrowserException("sort_type_unsupported", "Нативная сортировка этого типа не поддерживается. Выберите подтверждённый встроенный тип.");
        var expression = $"q.{PostgresBrowserSql.QuoteIdentifier(column.Name)}";
        if (column.Kind == DatabaseFilterValueKind.Text)
            expression = $"({expression}::pg_catalog.text) COLLATE pg_catalog.\"C\"";
        return $"{expression} USING OPERATOR(pg_catalog.{(query.Descending ? ">" : "<")}) NULLS FIRST";
    }

    internal static (IReadOnlyList<string> Sql, IReadOnlyList<string> Values) Filters(DatabaseBrowseQuery query,
        IReadOnlyList<Column>? columns, int firstParameter)
    {
        var predicates = new List<string>();
        var values = new List<string>();
        foreach (var filter in query.EffectiveFilters)
        {
            var column = columns?.SingleOrDefault(column => column.Name == filter.ColumnName)
                ?? throw new DatabaseBrowserException("filter_column_unavailable", "Столбец фильтра не найден. Обновите структуру и выберите существующий столбец.");
            if (!DatabaseQueryOperations.For(column.Kind).Contains(filter.Operator))
                throw new DatabaseBrowserException("filter_operator_unsupported", "Операция не поддерживается для подтверждённого типа столбца.");
            var left = $"q.{PostgresBrowserSql.QuoteIdentifier(column.Name)}";
            if (filter.Operator is DatabaseFilterOperator.IsNull or DatabaseFilterOperator.IsNotNull)
            {
                predicates.Add($"{left} IS {(filter.Operator == DatabaseFilterOperator.IsNotNull ? "NOT " : "")}NULL");
                continue;
            }
            var value = ValidateValue(column, filter.Value!);
            var parameter = $"${firstParameter + values.Count}::pg_catalog.text";
            values.Add(value);
            if (column.Kind == DatabaseFilterValueKind.Text)
            {
                left = $"({left}::pg_catalog.text) COLLATE pg_catalog.\"C\"";
                parameter = $"({parameter}) COLLATE pg_catalog.\"C\"";
            }
            else parameter = $"({parameter})::pg_catalog.{column.Cast}";
            predicates.Add(filter.Operator switch
            {
                DatabaseFilterOperator.Contains => $"pg_catalog.strpos({left}, {parameter}) OPERATOR(pg_catalog.>) 0",
                DatabaseFilterOperator.StartsWith => $"pg_catalog.starts_with({left}, {parameter})",
                DatabaseFilterOperator.EndsWith => $"pg_catalog.right({left}, pg_catalog.char_length({parameter})) OPERATOR(pg_catalog.=) {parameter}",
                _ => $"{left} OPERATOR(pg_catalog.{Operator(filter.Operator)}) {parameter}"
            });
        }
        return (predicates, values);
    }

    internal static void AddParameters(NpgsqlCommand command, DatabaseBrowseQuery query, IReadOnlyList<Column> columns)
    {
        var (_, values) = Filters(query, columns, string.IsNullOrEmpty(query.SearchText) ? 3 : 4);
        foreach (var value in values)
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = value });
    }

    private static string Operator(DatabaseFilterOperator operation) => operation switch
    {
        DatabaseFilterOperator.Equal => "=", DatabaseFilterOperator.NotEqual => "<>",
        DatabaseFilterOperator.LessThan => "<", DatabaseFilterOperator.LessOrEqual => "<=",
        DatabaseFilterOperator.GreaterThan => ">", DatabaseFilterOperator.GreaterOrEqual => ">=",
        _ => throw new DatabaseBrowserException("filter_operator_unsupported", "Операция фильтра не поддерживается.")
    };

    internal static string ValidateValue(Column column, string value)
    {
        var valid = column.Cast switch
        {
            "text" => true,
            "int2" => short.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "int4" => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "int8" => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "numeric" => Regex.IsMatch(value, @"\A[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "bool" => value is "true" or "false",
            _ => false
        };
        if (!valid)
            throw new DatabaseBrowserException("filter_value_invalid", "Значение фильтра не соответствует типу или допустимому диапазону. Используйте целое число, точное число с точкой, дату YYYY-MM-DD или true/false.");
        return value;
    }
}
