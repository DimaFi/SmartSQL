using System.Globalization;
using System.Numerics;
using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

public sealed partial class NativeSqliteDatabaseBrowser
{
    public Task<DatabaseQueryCapabilities> GetQueryCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        Validate(table);
        return ReadAsync(context =>
        {
            var columns = Columns(context, Relation(context, table));
            return new DatabaseQueryCapabilities(table, columns.Select(column =>
            {
                var kind = QueryKind(column.NativeType);
                var limitation = kind switch
                {
                    DatabaseFilterValueKind.Integer => "Сравнение требует INTEGER во всех непустых строках; ввод — точное целое Int64.",
                    DatabaseFilterValueKind.Number => "Сравнение требует INTEGER или конечный REAL во всех непустых строках; ввод — Int64 или обратимое представление double без потери цифр.",
                    DatabaseFilterValueKind.Text => "Сравнение требует корректный TEXT во всех непустых строках; регистр учитывается, NULL отличается от пустоты и пробелов.",
                    DatabaseFilterValueKind.Date => "Сравнение требует TEXT с существующей датой строго yyyy-MM-dd во всех непустых строках.",
                    DatabaseFilterValueKind.Boolean => "Сравнение требует INTEGER 0/1 во всех непустых строках; ввод — true/false или 0/1.",
                    _ => "Объявленный тип не поддержан для типизированного сравнения; доступны проверки NULL и нативная сортировка классов хранения."
                };
                return new DatabaseQueryColumn(column.Name, kind, true, DatabaseQueryOperations.For(kind), limitation);
            }).ToArray(),
            [new("sqlite_dynamic_types", "Объявленный тип SQLite не гарантирует тип значений. Перед каждым типизированным фильтром весь затронутый столбец проверяется в одном снимке с чтением страницы; несовместимые значения вызывают явную ошибку."),
             new("sqlite_native_storage_order", "Сортировка использует исходные значения: NULL сначала, затем числа INTEGER/REAL, TEXT по BINARY, BLOB по байтам. Это порядок классов хранения, а не преобразование смешанных данных в числа или даты.")]);
        }, cancellationToken);
    }

    private static DatabaseFilterValueKind QueryKind(string declaredType)
    {
        var type = declaredType.Trim().ToUpperInvariant();
        var facet = type.IndexOf('(');
        if (facet >= 0)
        {
            if (!type.EndsWith(')') || facet == type.Length - 2 ||
                type[(facet + 1)..^1].Any(character => !(char.IsAsciiDigit(character) || character is ',' or ' ')))
                return DatabaseFilterValueKind.Unsupported;
            type = type[..facet].TrimEnd();
        }
        return type switch
        {
            "INTEGER" or "INT" or "BIGINT" or "SMALLINT" or "TINYINT" or "MEDIUMINT" or "INT2" or "INT8" => DatabaseFilterValueKind.Integer,
            "REAL" or "DOUBLE" or "DOUBLE PRECISION" or "FLOAT" or "NUMERIC" or "DECIMAL" => DatabaseFilterValueKind.Number,
            "TEXT" or "CHAR" or "CHARACTER" or "VARCHAR" or "VARYING CHARACTER" or "NCHAR" or "NVARCHAR" or "NATIVE CHARACTER" or "CLOB" => DatabaseFilterValueKind.Text,
            "DATE" when facet < 0 => DatabaseFilterValueKind.Date,
            "BOOLEAN" or "BOOL" when facet < 0 => DatabaseFilterValueKind.Boolean,
            _ => DatabaseFilterValueKind.Unsupported
        };
    }

    private static IReadOnlyList<KeyValuePair<string, object>> PrepareFilters(Context context,
        DatabaseTableRef table, IReadOnlyList<DatabaseColumn> columns, DatabaseBrowseQuery query)
    {
        var values = new List<KeyValuePair<string, object>>();
        var validated = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < query.EffectiveFilters.Count; index++)
        {
            context.Token.ThrowIfCancellationRequested();
            var filter = query.EffectiveFilters[index];
            var column = columns.FirstOrDefault(column => column.Name == filter.ColumnName)
                ?? throw new DatabaseBrowserException("filter_column_unavailable", "Столбец фильтра отсутствует. Обновите структуру.");
            var kind = QueryKind(column.NativeType);
            if (!DatabaseQueryOperations.For(kind).Contains(filter.Operator))
                throw new DatabaseBrowserException("filter_operation_unsupported", "Тип или операция SQLite-фильтра не поддерживается. Для неизвестных типов доступны проверки NULL.");
            if (filter.Operator is DatabaseFilterOperator.IsNull or DatabaseFilterOperator.IsNotNull)
                continue;
            values.Add(new("$filter" + index, FilterValue(kind, filter.Value!)));
            if (validated.Add(column.Name))
                ValidateFilterColumn(context, table, column, kind);
        }
        return values;
    }

    private static object FilterValue(DatabaseFilterValueKind kind, string value)
    {
        switch (kind)
        {
            case DatabaseFilterValueKind.Text:
                return value;
            case DatabaseFilterValueKind.Integer:
                if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    return integer;
                break;
            case DatabaseFilterValueKind.Number:
                if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exactInteger))
                    return exactInteger;
                if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) &&
                    SameDecimalLiteral(value, number.ToString("R", CultureInfo.InvariantCulture)))
                    return number;
                break;
            case DatabaseFilterValueKind.Boolean:
                if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1L;
                if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0L;
                break;
            case DatabaseFilterValueKind.Date:
                if (CanonicalDate(value)) return value;
                break;
        }
        throw new DatabaseBrowserException("filter_value_invalid", "Значение фильтра не соответствует нативному типу или теряет точность. Используйте Int64, обратимый конечный double, true/false (0/1) или существующую дату yyyy-MM-dd.");
    }

    // Compare decimal spellings without decimal's precision/range limits or double rounding the input.
    private static bool SameDecimalLiteral(string input, string roundTrip) => NormalizeDecimal(input) == NormalizeDecimal(roundTrip);

    private static (bool Negative, string Digits, BigInteger Exponent) NormalizeDecimal(string value)
    {
        var negative = value.StartsWith('-');
        if (value.StartsWith('+') || negative) value = value[1..];
        var exponentIndex = value.IndexOfAny(['e', 'E']);
        var exponent = exponentIndex < 0 ? BigInteger.Zero
            : BigInteger.Parse(value[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        if (exponentIndex >= 0) value = value[..exponentIndex];
        var point = value.IndexOf('.');
        if (point >= 0)
        {
            exponent -= value.Length - point - 1;
            value = value.Remove(point, 1);
        }
        value = value.TrimStart('0');
        if (value.Length == 0) return (false, "0", BigInteger.Zero);
        var trimmed = value.TrimEnd('0');
        exponent += value.Length - trimmed.Length;
        return (negative, trimmed, exponent);
    }

    private static bool CanonicalDate(string value) => value.Length == 10 &&
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == value;

    private static void ValidateFilterColumn(Context context, DatabaseTableRef table, DatabaseColumn column,
        DatabaseFilterValueKind kind)
    {
        var name = NativeSqliteBrowserSql.Quote(column.Name);
        using var command = context.Command($"SELECT typeof({name}), length(CAST({name} AS BLOB)), CASE WHEN typeof({name})='text' THEN CAST({name} AS BLOB) ELSE {name} END FROM main.{NativeSqliteBrowserSql.Quote(table.Name)} WHERE {name} IS NOT NULL");
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            context.Token.ThrowIfCancellationRequested();
            var storage = reader.GetString(0);
            var compatible = kind switch
            {
                DatabaseFilterValueKind.Integer => storage == "integer",
                DatabaseFilterValueKind.Number => storage == "integer" || storage == "real" && double.IsFinite(reader.GetDouble(2)),
                DatabaseFilterValueKind.Boolean => storage == "integer" && reader.GetInt64(2) is 0 or 1,
                DatabaseFilterValueKind.Text or DatabaseFilterValueKind.Date => storage == "text",
                _ => false
            };
            if (!compatible)
                throw new DatabaseBrowserException("filter_storage_incompatible", "Непустое значение столбца имеет несовместимый класс хранения SQLite. Проверен весь столбец, включая строки вне страницы; данные не преобразованы и не исключены молча.");
            if (kind is DatabaseFilterValueKind.Text or DatabaseFilterValueKind.Date)
            {
                if (reader.GetInt64(1) > MaxCell * 4L)
                    throw new DatabaseBrowserException("cell_too_large", "TEXT превышает предел проверки фильтра. Значение не обрезано.");
                var text = Decode((byte[])reader.GetValue(2), context.TextEncoding);
                if (text.Length > MaxCell)
                    throw new DatabaseBrowserException("cell_too_large", "TEXT превышает предел проверки фильтра. Значение не обрезано.");
                if (kind == DatabaseFilterValueKind.Date && !CanonicalDate(text))
                    throw new DatabaseBrowserException("filter_storage_incompatible", "DATE содержит значение, отличное от существующей даты TEXT в формате yyyy-MM-dd. Типизированное сравнение отменено для всего столбца.");
            }
        }
    }
}
