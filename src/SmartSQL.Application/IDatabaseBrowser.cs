using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Read-only browsing of ordinary databases. SQL and native connections stay in providers.</summary>
public interface IDatabaseBrowser : IAsyncDisposable
{
    Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default);
    Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize,
        CancellationToken cancellationToken = default);
}

public static class DatabaseBrowseLimits
{
    public const int MaxPageSize = 500;

    public static void ValidatePageRequest(long offset, int pageSize)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Смещение страницы не может быть отрицательным.");
        if (pageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Размер страницы должен быть от 1 до {MaxPageSize}.");
    }
}
