using SmartSQL.Application;
using SmartSQL.Domain;

namespace SmartSQL.Infrastructure;

internal static class NativeSqliteBrowserSql
{
    internal static string Quote(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\0')) throw new ArgumentException("Некорректное имя объекта.", nameof(name));
        for (var i = 0; i < name.Length; i++)
            if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) i++;
            else if (char.IsSurrogate(name[i])) throw new ArgumentException("Некорректный Unicode имени.", nameof(name));
        return "\"" + name.Replace("\"", "\"\"") + "\"";
    }

    internal static string Display(string column) => $"CASE WHEN typeof({column})='blob' THEN '0x'||hex({column}) ELSE CAST({column} AS TEXT) END";

    internal static string Page(DatabaseTableRef table, IReadOnlyList<DatabaseColumn> columns, DatabaseBrowseQuery query)
    {
        if (query.SortColumn is not null && !columns.Any(c => c.Name == query.SortColumn))
            throw new DatabaseBrowserException("sort_column_unavailable", "Столбец сортировки отсутствует. Обновите структуру.");
        var names = columns.Select(c => "q." + Quote(c.Name)).ToArray();
        var projection = string.Join(", ", names.Select(n => $"typeof({n}), length(CAST({n} AS BLOB)), CASE WHEN typeof({n})='text' THEN CAST({n} AS BLOB) ELSE {n} END"));
        var keys = columns.Where(c => c.PrimaryKeyOrder is not null).OrderBy(c => c.PrimaryKeyOrder).Select(c => "q." + Quote(c.Name)).ToArray();
        var order = query.SortColumn is not null
            ? new[] { Display("q." + Quote(query.SortColumn)) + " COLLATE BINARY" + (query.Descending ? " DESC" : " ASC") + " NULLS FIRST" }.Concat(keys)
            : keys.Length > 0 ? keys : names.Select(n => Display(n) + " COLLATE BINARY");
        var where = string.IsNullOrEmpty(query.SearchText) ? string.Empty
            : " WHERE " + string.Join(" OR ", names.Select(n => $"instr(lower({Display(n)}), lower($search))>0"));
        return $"SELECT {projection} FROM main.{Quote(table.Name)} AS q{where} ORDER BY {string.Join(", ", order)} LIMIT $limit OFFSET $offset";
    }
}
