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

    public static string Access(bool isManaged, bool editingEnabled)
    {
        if (isManaged) return "Редактирование проекта";
        return editingEnabled
            ? "Изменения по доступным операциям"
            : "Только чтение";
    }
}
