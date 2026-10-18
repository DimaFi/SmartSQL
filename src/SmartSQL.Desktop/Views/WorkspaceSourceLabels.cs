namespace SmartSQL.Desktop.Views;

/// <summary>
/// Labels convey the source type without confusing UI mode with DBMS privileges.
/// These are descriptions only, never permission checks.
/// </summary>
public static class WorkspaceSourceLabels
{
    public static string Kind(bool isManaged, string provider)
    {
        if (isManaged) return "Локальный проект SmartSQL";
        return provider switch
        {
            "SQLite" => "Существующий SQLite-файл",
            "PostgreSQL" => "Подключение к серверу PostgreSQL",
            _ => "Подключённая внешняя база данных"
        };
    }

    /// <summary>
    /// Keep the first connection's friendly name intact; a second identical
    /// display name gets a session qualifier, not a meaningless global counter.
    /// Connection identity remains its Guid/path and is never derived from this label.
    /// </summary>
    public static string UniqueDisplayName(string preferred, IEnumerable<string> currentNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferred);
        ArgumentNullException.ThrowIfNull(currentNames);
        var name = preferred.Trim();
        var taken = currentNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var number = 2; number < int.MaxValue; number++)
        {
            var candidate = $"{name} (сеанс {number})";
            if (!taken.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Слишком много подключений с одинаковыми названиями.");
    }

    public static string Access(bool isManaged, bool editingEnabled)
    {
        if (isManaged) return "Редактирование проекта";
        return editingEnabled
            ? "Изменения по доступным операциям"
            : "Только чтение";
    }
}
