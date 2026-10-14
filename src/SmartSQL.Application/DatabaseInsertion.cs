using SmartSQL.Domain;

namespace SmartSQL.Application;

public enum DatabaseInsertValueMode { ServerDefault, Null, Value }

/// <summary>Insertion rules differ from updates: supported keys may be supplied, generated fields may not.</summary>
public sealed record InsertableDatabaseColumn(EditableDatabaseColumn Column, bool CanUseDefault,
    bool MustUseDefault, string? DefaultDescription = null, string? ReadOnlyReason = null);

public sealed record DatabaseInsertCapabilities(DatabaseTableRef Table, bool CanInsert,
    IReadOnlyList<InsertableDatabaseColumn> Columns, IReadOnlyList<DatabaseDiagnostic> Diagnostics)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? ConfirmationHandle { get; init; }
    public override string ToString() => $"{nameof(DatabaseInsertCapabilities)} {{ Table = {Table}, CanInsert = {CanInsert} }}";
}

/// <summary>ServerDefault omits the field; Null sends an explicit NULL; Value preserves an explicit value.</summary>
public sealed record DatabaseInsertValue(string ColumnName, DatabaseInsertValueMode Mode, string? Value = null)
{
    public override string ToString() => $"{nameof(DatabaseInsertValue)} {{ ColumnName = {ColumnName}, Mode = {Mode} }}";
}

public sealed record DatabaseInsertRequest(DatabaseTableRef Table, Guid ConfirmationHandle,
    IReadOnlyList<DatabaseInsertValue> Values)
{
    public const int MaxFields = 2048;
    public const int MaxTotalCharacters = 16_000_000;
    public override string ToString() => $"{nameof(DatabaseInsertRequest)} {{ Table = {Table} }}";

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Table);
        ArgumentNullException.ThrowIfNull(Values);
        if (ConfirmationHandle == Guid.Empty)
            throw new ArgumentException("Обновите структуру перед добавлением строки.", nameof(ConfirmationHandle));
        ValidateName(Table.Schema);
        ValidateName(Table.Name);
        if (!Enum.IsDefined(Table.Kind)) throw new ArgumentException("Неизвестный вид объекта.", nameof(Table));
        if (Values.Count > MaxFields) throw new ArgumentException("Слишком много полей для одной строки.", nameof(Values));
        var names = new HashSet<string>(StringComparer.Ordinal);
        long characters = 0;
        foreach (var input in Values)
        {
            ArgumentNullException.ThrowIfNull(input);
            ValidateName(input.ColumnName);
            if (!names.Add(input.ColumnName)) throw new ArgumentException("Поле задано несколько раз.", nameof(Values));
            if (!Enum.IsDefined(input.Mode)) throw new ArgumentException("Неизвестный способ заполнения поля.", nameof(Values));
            if (input.Mode == DatabaseInsertValueMode.Value && input.Value is null
                || input.Mode != DatabaseInsertValueMode.Value && input.Value is not null)
                throw new ArgumentException("Различайте значение, NULL и заполнение сервером.", nameof(Values));
            DatabaseEditValueValidation.ValidateText(input.Value, nameof(Values));
            characters += input.Value?.Length ?? 0;
            if (characters > MaxTotalCharacters) throw new ArgumentException("Строка превышает допустимый объём.", nameof(Values));
        }
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Имя объекта не может быть пустым.");
        DatabaseEditValueValidation.ValidateText(name, nameof(name));
    }
}

public sealed record DatabaseInsertResult(DatabaseDisplayRow Row, string? Message = null);

public static class DatabaseInsertValueValidation
{
    public static DatabaseInsertValue Normalize(InsertableDatabaseColumn column, DatabaseInsertValue input)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(input);
        if (!string.Equals(column.Column.Name, input.ColumnName, StringComparison.Ordinal))
            throw new ArgumentException("Выбрано другое поле.", nameof(input));
        if (!Enum.IsDefined(input.Mode)) throw new ArgumentException("Неизвестный способ заполнения поля.", nameof(input));
        if (input.Mode == DatabaseInsertValueMode.ServerDefault)
        {
            if (!column.CanUseDefault || input.Value is not null)
                throw new ArgumentException("Поле требует явного значения или недоступно для заполнения сервером.", nameof(input));
            return input;
        }
        if (column.MustUseDefault)
            throw new ArgumentException(column.ReadOnlyReason ?? "Это поле заполняет сервер.", nameof(input));
        if (input.Mode == DatabaseInsertValueMode.Null)
        {
            if (input.Value is not null) throw new ArgumentException("NULL задаётся отдельно от текста.", nameof(input));
            DatabaseEditValueValidation.Normalize(column.Column, null);
            return input;
        }
        if (input.Value is null) throw new ArgumentException("Для отсутствующего значения выберите NULL.", nameof(input));
        return input with { Value = DatabaseEditValueValidation.Normalize(column.Column, input.Value) };
    }
}
