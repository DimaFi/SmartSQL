namespace SmartSQL.Desktop.Queries;

public enum SqlHighlightKind { Keyword, String, Comment, Number, Identifier }
public readonly record struct SqlHighlightSpan(int Start, int Length, SqlHighlightKind Kind);

/// <summary>Bounded display-only tokenization. Never used to validate, rewrite, or execute SQL.</summary>
public static class SqlSyntaxHighlighting
{
    public const int MaxCharacters = 65_536;
    public const int MaxSpans = 2_048;
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "AS", "AND", "OR", "NOT", "NULL", "IS", "TRUE", "FALSE",
        "WITH", "RECURSIVE", "VALUES", "TABLE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "ON", "USING",
        "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "FETCH", "FIRST", "NEXT", "ROWS", "ROW", "ONLY",
        "ASC", "DESC", "NULLS", "DISTINCT", "ALL", "UNION", "INTERSECT", "EXCEPT", "CASE", "WHEN", "THEN", "ELSE", "END",
        "IN", "EXISTS", "BETWEEN", "LIKE", "ILIKE", "ESCAPE", "CAST", "COLLATE", "OVER", "PARTITION", "WINDOW", "FILTER",
        "RANGE", "PRECEDING", "FOLLOWING", "CURRENT", "UNBOUNDED", "LATERAL", "RETURNING", "INSERT", "UPDATE", "DELETE",
        "INTO", "SET", "CREATE", "ALTER", "DROP", "BEGIN", "COMMIT", "ROLLBACK", "EXPLAIN", "ANALYZE", "SHOW", "PRAGMA",
        "INTEGER", "INT", "BIGINT", "SMALLINT", "TEXT", "VARCHAR", "CHAR", "NUMERIC", "DECIMAL", "REAL", "DOUBLE", "PRECISION",
        "DATE", "TIME", "TIMESTAMP", "INTERVAL", "BOOLEAN", "BOOL", "BLOB", "BYTEA", "JSON", "JSONB"
    };

    public static IReadOnlyList<SqlHighlightSpan> Scan(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxCharacters) return [];
        var keywordLookup = Keywords.GetAlternateLookup<ReadOnlySpan<char>>();
        var spans = new List<SqlHighlightSpan>();
        var index = 0;
        while (index < text.Length && spans.Count < MaxSpans)
        {
            var start = index;
            var current = text[index];
            if (current == '-' && Next(text, index, '-'))
            {
                index += 2;
                while (index < text.Length && text[index] is not ('\r' or '\n')) index++;
                spans.Add(new(start, index - start, SqlHighlightKind.Comment));
            }
            else if (current == '/' && Next(text, index, '*'))
            {
                index += 2;
                var depth = 1;
                while (index < text.Length && depth > 0)
                {
                    if (text[index] == '/' && Next(text, index, '*')) { depth++; index += 2; }
                    else if (text[index] == '*' && Next(text, index, '/')) { depth--; index += 2; }
                    else index++;
                }
                spans.Add(new(start, index - start, SqlHighlightKind.Comment));
            }
            else if (current is '\'' or '"' or '`' or '[')
            {
                var close = current == '[' ? ']' : current;
                var escape = current == '\'' && start > 0 && (text[start - 1] is 'e' or 'E')
                    && (start < 2 || !WordPart(text[start - 2]));
                index++;
                while (index < text.Length)
                {
                    if (escape && text[index] == '\\') { index = Math.Min(text.Length, index + 2); continue; }
                    if (text[index++] != close) continue;
                    if (index < text.Length && text[index] == close) { index++; continue; }
                    break;
                }
                spans.Add(new(start, index - start, current == '\'' ? SqlHighlightKind.String : SqlHighlightKind.Identifier));
            }
            else if (current == '$' && DollarDelimiter(text, index) is { } delimiter)
            {
                var close = text.IndexOf(delimiter, index + delimiter.Length, StringComparison.Ordinal);
                index = close < 0 ? text.Length : close + delimiter.Length;
                spans.Add(new(start, index - start, SqlHighlightKind.String));
            }
            else if (char.IsAsciiDigit(current) || current == '.' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
            {
                if (current == '0' && index + 2 < text.Length && (text[index + 1] is 'x' or 'X') && char.IsAsciiHexDigit(text[index + 2]))
                {
                    index += 2;
                    while (index < text.Length && char.IsAsciiHexDigit(text[index])) index++;
                }
                else
                {
                    while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
                    if (index < text.Length && text[index] == '.')
                    {
                        index++;
                        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
                    }
                    if (index < text.Length && (text[index] is 'e' or 'E'))
                    {
                        var exponent = index + 1;
                        if (exponent < text.Length && (text[exponent] is '+' or '-')) exponent++;
                        if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
                        {
                            index = exponent + 1;
                            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
                        }
                    }
                }
                spans.Add(new(start, index - start, SqlHighlightKind.Number));
            }
            else if (char.IsLetter(current) || current == '_')
            {
                index++;
                while (index < text.Length && WordPart(text[index])) index++;
                if (keywordLookup.Contains(text.AsSpan(start, index - start))) spans.Add(new(start, index - start, SqlHighlightKind.Keyword));
            }
            else index++;
        }
        return spans;
    }

    /// <summary>Remove selected portions so native selection foreground is the only style there.</summary>
    public static IReadOnlyList<SqlHighlightSpan> ExcludeSelection(IReadOnlyList<SqlHighlightSpan> spans, int start, int length)
    {
        if (length <= 0) return spans;
        var end = (long)start + length;
        var result = new List<SqlHighlightSpan>(spans.Count + 1);
        foreach (var span in spans)
        {
            var spanEnd = span.Start + span.Length;
            if (spanEnd <= start || span.Start >= end) { result.Add(span); continue; }
            if (span.Start < start) result.Add(span with { Length = start - span.Start });
            if (spanEnd > end) result.Add(span with { Start = (int)end, Length = spanEnd - (int)end });
        }
        return result;
    }

    private static bool Next(string text, int index, char next) => index + 1 < text.Length && text[index + 1] == next;
    private static bool WordPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' || char.GetUnicodeCategory(value) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;
    private static string? DollarDelimiter(string text, int start)
    {
        var end = start + 1;
        if (end < text.Length && text[end] == '$') return "$$";
        if (end == text.Length || !(char.IsLetter(text[end]) || text[end] == '_')) return null;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return end < text.Length && text[end] == '$' ? text[start..(end + 1)] : null;
    }
}
