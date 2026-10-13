using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

internal static class PostgresBrowserSql
{
    internal const int DefaultPageSize = 100;

    internal const string ConfigureReadOnlyTransaction = """
        SET TRANSACTION READ ONLY;
        SET LOCAL statement_timeout = '15s';
        SET LOCAL lock_timeout = '3s';
        SET LOCAL search_path = pg_catalog;
        SET LOCAL DateStyle = 'ISO,YMD';
        SET LOCAL IntervalStyle = 'iso_8601';
        SET LOCAL TimeZone = 'UTC';
        SET LOCAL extra_float_digits = 3;
        SET LOCAL bytea_output = 'hex';
        """;

    internal const string Tables = """
        SELECT n.nspname::pg_catalog.text, c.relname::pg_catalog.text, c.relkind::pg_catalog.text
        FROM pg_catalog.pg_class AS c
        JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'v', 'm', 'f', 'p')
          AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\'
          AND n.nspname <> 'information_schema'
          AND pg_catalog.has_schema_privilege(n.oid, 'USAGE')
          AND pg_catalog.has_table_privilege(c.oid, 'SELECT')
        ORDER BY n.nspname COLLATE pg_catalog."C", c.relname COLLATE pg_catalog."C"
        LIMIT 10001
        """;

    internal const string Relation = """
        SELECT c.oid, n.nspname::pg_catalog.text, c.relname::pg_catalog.text,
               c.relkind::pg_catalog.text, c.relrowsecurity, c.relhassubclass
        FROM pg_catalog.pg_class AS c
        JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
        WHERE n.nspname = $1 AND c.relname = $2
          AND c.relkind IN ('r', 'v', 'm', 'f', 'p')
          AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\'
          AND n.nspname <> 'information_schema'
          AND pg_catalog.has_schema_privilege(n.oid, 'USAGE')
          AND pg_catalog.has_table_privilege(c.oid, 'SELECT')
        """;

    internal const string Columns = """
        SELECT a.attname::pg_catalog.text, pg_catalog.format_type(a.atttypid, a.atttypmod),
               NOT a.attnotnull AND NOT t.typnotnull,
               CASE WHEN i.indexrelid IS NOT NULL THEN pg_catalog.array_position(pk.conkey, a.attnum) ELSE NULL END
        FROM pg_catalog.pg_attribute AS a
        JOIN pg_catalog.pg_type AS t ON t.oid = a.atttypid
        LEFT JOIN pg_catalog.pg_constraint AS pk
          ON pk.conrelid = a.attrelid AND pk.contype = 'p' AND pk.convalidated
        LEFT JOIN pg_catalog.pg_index AS i
          ON i.indexrelid = pk.conindid AND i.indisvalid AND i.indisunique
        WHERE a.attrelid = $1 AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY a.attnum
        """;

    internal static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        if (identifier.Contains('\0'))
            throw new ArgumentException("Идентификатор PostgreSQL содержит недопустимый символ.", nameof(identifier));
        for (var index = 0; index < identifier.Length; index++)
        {
            if (char.IsHighSurrogate(identifier[index]) && index + 1 < identifier.Length && char.IsLowSurrogate(identifier[index + 1]))
                index++;
            else if (char.IsSurrogate(identifier[index]))
                throw new ArgumentException("Идентификатор PostgreSQL содержит некорректный Unicode.", nameof(identifier));
        }
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    internal static void ValidatePage(long offset, int pageSize)
    {
        DatabaseBrowseLimits.ValidatePageRequest(offset, pageSize);
    }

    // Only catalog-confirmed names reach this builder; values remain parameters.
    internal static string Page(DatabaseTableRef table, IReadOnlyList<DatabaseColumn> columns,
        DatabaseBrowseQuery? query = null)
    {
        query ??= new DatabaseBrowseQuery();
        query.Validate();
        var selectedSort = query.SortColumn is null ? null
            : columns.SingleOrDefault(column => string.Equals(column.Name, query.SortColumn, StringComparison.Ordinal));
        if (query.SortColumn is not null && selectedSort is null)
            throw new DatabaseBrowserException("sort_column_unavailable", "Столбец сортировки не найден. Обновите структуру таблицы и выберите существующий столбец.");
        var filtering = string.IsNullOrEmpty(query.SearchText) ? string.Empty
            : columns.Count == 0 ? " WHERE $3::pg_catalog.text IS NULL"
            : " WHERE (" + string.Join(" OR ", columns.Select(column =>
                $"pg_catalog.strpos(pg_catalog.lower(q.{QuoteIdentifier(column.Name)}::pg_catalog.text), pg_catalog.lower($3::pg_catalog.text)) > 0")) + ")";
        if (columns.Count == 0)
            return $"SELECT NULL::pg_catalog.text FROM {QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)} AS q{filtering} LIMIT $1 OFFSET $2";
        var projection = string.Join(", ", columns.Select(column => $"q.{QuoteIdentifier(column.Name)}::pg_catalog.text"));
        var primaryKey = columns.Where(column => column.PrimaryKeyOrder is not null).OrderBy(column => column.PrimaryKeyOrder).ToArray();
        var ordering = primaryKey.Length > 0
            ? string.Join(", ", primaryKey.Select(column => $"q.{QuoteIdentifier(column.Name)}"))
            : string.Join(", ", columns.Select(column => $"(q.{QuoteIdentifier(column.Name)}::pg_catalog.text) COLLATE pg_catalog.\"C\" NULLS FIRST"));
        if (selectedSort is not null)
            ordering = $"(q.{QuoteIdentifier(selectedSort.Name)}::pg_catalog.text) COLLATE pg_catalog.\"C\" {(query.Descending ? "DESC" : "ASC")} NULLS FIRST, " + ordering;
        return $"SELECT {projection} FROM {QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)} AS q{filtering} ORDER BY {ordering} LIMIT $1 OFFSET $2";
    }

    internal static IReadOnlyList<DatabaseDiagnostic> Diagnostics(DatabaseTableRef table, IReadOnlyList<DatabaseColumn> columns,
        bool rowLevelSecurity, bool hasSubclasses, DatabaseBrowseQuery? query = null)
    {
        var diagnostics = new List<DatabaseDiagnostic>
        {
            new("native_text", "Значения показаны в текстовом представлении PostgreSQL без преобразования в локальные типы. Время с часовым поясом показано в UTC."),
            new("live_pagination", "Каждая страница читает отдельный снимок базы. Изменения на сервере между страницами могут сдвинуть строки.")
        };
        if (query?.SortColumn is not null)
            diagnostics.Add(new("selected_text_sort", "Выбранный столбец сортируется по текстовому представлению PostgreSQL (C), а не по его нативному типу; например, 10 идёт перед 2. NULL идут первыми. Совпадения упорядочены по ключу, если он подтверждён."));
        if (!string.IsNullOrEmpty(query?.SearchText))
            diagnostics.Add(new("native_text_search", "Поиск ищет буквальный фрагмент текстового представления во всех столбцах. Сопоставление регистра определяется локалью базы PostgreSQL; локаль C учитывает регистр нелатинских букв. NULL не совпадает; точное сравнение нативных типов не выполняется."));
        if (!columns.Any(column => column.PrimaryKeyOrder is not null))
        {
            diagnostics.Add(new("no_primary_key", "У объекта нет подтверждённого первичного ключа. Надёжная идентификация строк недоступна."));
            diagnostics.Add(new("unstable_pagination", "Строки упорядочены по текстовым значениям; одинаковые значения не задают однозначного порядка."));
        }
        else if (table.Kind == DatabaseObjectKind.Table && hasSubclasses)
            diagnostics.Add(new("inherited_identity", "Таблица включает строки наследников. Первичный ключ родителя не гарантирует уникальность среди всех наследников."));
        if (rowLevelSecurity)
            diagnostics.Add(new("row_level_security", "На таблице включена защита строк PostgreSQL; видимые данные определяются правами текущего пользователя."));
        if (table.Kind == DatabaseObjectKind.MaterializedView)
            diagnostics.Add(new("materialized_snapshot", "Материализованное представление показывает результат последнего обновления на сервере."));
        return diagnostics;
    }
}
