namespace SmartSQL.Desktop.Queries;

public readonly record struct SqlFindMatch(int Start, int Length);

/// <summary>
/// Literal, case-insensitive navigation within the editor's current SQL text.
/// Display-only: never parses, changes, saves or executes the SQL statement.
/// Offsets follow TextBox UTF-16 selection indices.
/// </summary>
public static class SqlTextSearch
{
    public static SqlFindMatch? Find(
        string? text, string? search,
        int selectionStart, int selectionEnd, bool backwards = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return null;

        var position = backwards
            ? Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length)
            : Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);

        int match;
        if (backwards)
        {
            match = position > 0
                ? text.LastIndexOf(search, position - 1, StringComparison.OrdinalIgnoreCase)
                : -1;
            if (match < 0)
                match = text.LastIndexOf(search, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            match = text.IndexOf(search, position, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
                match = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        }

        return match < 0 ? null : new SqlFindMatch(match, search.Length);
    }
}
