using SmartSQL.Domain;

namespace SmartSQL.Application;

public sealed record DatabaseDeleteCapabilities(DatabaseTableRef Table, bool CanDelete,
    IReadOnlyList<DatabaseDiagnostic> Diagnostics);

/// <summary>The handle confirms a privately captured row snapshot, not its grid position or displayed key.</summary>
public sealed record DatabaseRowDelete(DatabaseTableRef Table, Guid RowHandle)
{
    public override string ToString() => $"{nameof(DatabaseRowDelete)} {{ Table = {Table} }}";

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Table);
        if (RowHandle == Guid.Empty)
            throw new ArgumentException("Выберите строку текущей страницы для удаления.", nameof(RowHandle));
        ValidateName(Table.Schema);
        ValidateName(Table.Name);
        if (!Enum.IsDefined(Table.Kind)) throw new ArgumentException("Неизвестный вид объекта базы.", nameof(Table));
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Имя объекта не может быть пустым.", nameof(name));
        DatabaseEditValueValidation.ValidateText(name, nameof(name));
    }
}

public sealed record DatabaseDeleteResult(string? Message = null);
