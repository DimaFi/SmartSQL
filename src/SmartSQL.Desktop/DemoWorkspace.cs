using SmartSQL.Domain;
using SmartSQL.Infrastructure;

namespace SmartSQL.Desktop;

/// <summary>Sample data for exploring the UI; domain-specific data stays outside the core.</summary>
internal static class DemoWorkspace
{
    public static (string Path, Guid MainTableId, SqliteWorkspace Workspace) OpenOrCreate()
    {
        var path = WorkspacePaths.DemoDatabasePath;
        var workspace = File.Exists(path)
            ? SqliteWorkspace.OpenExisting(path)
            : SqliteWorkspace.CreateNew(path);
        var existing = workspace.GetTables();
        if (existing.Count > 0)
            return (path, existing.FirstOrDefault(table => table.Name == "Студенты и практика")?.Id ?? existing[0].Id, workspace);

        var students = workspace.CreateTable("Студенты и практика",
        [
            new FieldInput("ФИО", FieldType.Text),
            new FieldInput("Курс", FieldType.Number),
            new FieldInput("Группа", FieldType.Text),
            new FieldInput("Целевик", FieldType.Boolean),
            new FieldInput("Родная школа", FieldType.Text),
            new FieldInput("Место практики", FieldType.Text),
            new FieldInput("Начало", FieldType.Date),
            new FieldInput("Документы", FieldType.Boolean),
            new FieldInput("Статус", FieldType.Text)
        ]);
        var studentRows = new (string Name, string Course, string Group, string Target,
            string HomeSchool, string PracticeSchool, string Start, string Documents, string Status)[]
        {
            ("Анна Иванова", "3", "ПИ-31", "Да", "Школа № 12", "Школа № 12", "2026-06-01", "Да", "Подтверждено"),
            ("Мария Петрова", "3", "ПИ-31", "Нет", "Школа № 8", "Лицей № 4", "2026-06-08", "Нет", "Ожидает документы"),
            ("Илья Смирнов", "3", "ПИ-32", "Да", "Гимназия № 2", "Школа № 15", "2026-06-01", "Нет", "Требует проверки"),
            ("Дарья Кузнецова", "2", "ПИ-21", "Нет", "Школа № 12", "Школа № 12", "2026-06-15", "Да", "Подтверждено"),
            ("Павел Соколов", "3", "ПИ-32", "Да", "Лицей № 4", "Лицей № 4", "2026-06-08", "Да", "Подтверждено"),
            ("Елена Волкова", "4", "ПИ-41", "Нет", "Школа № 7", "Гимназия № 2", "2026-06-01", "Нет", "Ожидает документы"),
            ("Никита Орлов", "3", "ПИ-31", "Да", "Школа № 15", "Школа № 8", "2026-06-15", "Нет", "Требует проверки"),
            ("София Морозова", "2", "ПИ-22", "Нет", "Школа № 8", "Школа № 8", "2026-06-08", "Да", "Подтверждено")
        };
        var studentFields = students.Fields.ToDictionary(field => field.Name, field => field.Id);
        workspace.AddRecords(students.Id, studentRows.Select(row =>
            (IReadOnlyDictionary<Guid, string?>)new Dictionary<Guid, string?>
            {
                [studentFields["ФИО"]] = row.Name,
                [studentFields["Курс"]] = row.Course,
                [studentFields["Группа"]] = row.Group,
                [studentFields["Целевик"]] = row.Target,
                [studentFields["Родная школа"]] = row.HomeSchool,
                [studentFields["Место практики"]] = row.PracticeSchool,
                [studentFields["Начало"]] = row.Start,
                [studentFields["Документы"]] = row.Documents,
                [studentFields["Статус"]] = row.Status
            }).ToArray());

        var organizations = workspace.CreateTable("Организации",
        [
            new FieldInput("Название", FieldType.Text),
            new FieldInput("Тип", FieldType.Text),
            new FieldInput("Город", FieldType.Text),
            new FieldInput("Контакт", FieldType.Text)
        ]);
        var organizationFields = organizations.Fields.ToDictionary(field => field.Name, field => field.Id);
        workspace.AddRecords(organizations.Id,
        [
            Organization("Школа № 12", "Школа", "Саратов", "Методист"),
            Organization("Школа № 8", "Школа", "Саратов", "Заместитель директора"),
            Organization("Гимназия № 2", "Гимназия", "Энгельс", "Методист"),
            Organization("Лицей № 4", "Лицей", "Саратов", "Куратор практики"),
            Organization("Школа № 15", "Школа", "Балаково", "Заместитель директора")
        ]);

        workspace.SaveFilter(students.Id, "Нет документов",
            [new RecordFilter(studentFields["Документы"], FilterOperator.Equals, "Нет")]);
        workspace.SaveFilter(students.Id, "Целевики 3 курса",
        [
            new RecordFilter(studentFields["Целевик"], FilterOperator.Equals, "Да"),
            new RecordFilter(studentFields["Курс"], FilterOperator.Equals, "3")
        ]);
        return (path, students.Id, workspace);

        IReadOnlyDictionary<Guid, string?> Organization(string name, string type, string city, string contact) =>
            new Dictionary<Guid, string?>
            {
                [organizationFields["Название"]] = name,
                [organizationFields["Тип"]] = type,
                [organizationFields["Город"]] = city,
                [organizationFields["Контакт"]] = contact
            };
    }
}
