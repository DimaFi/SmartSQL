using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Read-only browsing of ordinary databases. SQL and native connections stay in providers.</summary>
public interface IDatabaseBrowser : IAsyncDisposable
{
    Task<IReadOnlyList<DatabaseTableRef>> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default);
    async Task<DatabaseStructure> GetStructureAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var columns = await GetColumnsAsync(table, cancellationToken).ConfigureAwait(false);
        var structure = new DatabaseStructure(table, columns, DatabaseMetadataCoverage.Unavailable, [],
            DatabaseMetadataCoverage.Unavailable, [], null,
            [new DatabaseDiagnostic("structure_unsupported", "Провайдер пока предоставляет только столбцы. Ключи, ограничения и индексы не подтверждены.")]);
        DatabaseStructureLimits.Validate(structure);
        return structure;
    }
    Task<DatabaseQueryCapabilities> GetQueryCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DatabaseQueryCapabilities(table, [],
            [new DatabaseDiagnostic("typed_query_unsupported", "Провайдер пока не подтвердил фильтры по типам и нативную сортировку.")]));
    }
    Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize,
        CancellationToken cancellationToken = default);

    Task<DatabasePage> ReadPageAsync(DatabaseTableRef table, long offset, int pageSize,
        DatabaseBrowseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        if (!string.IsNullOrEmpty(query.SearchText) || query.SortColumn is not null || query.EffectiveFilters.Count > 0)
            throw new DatabaseBrowserException("browse_query_unsupported", "Провайдер пока не поддерживает поиск, фильтры и выбранную сортировку.");
        return ReadPageAsync(table, offset, pageSize, cancellationToken);
    }
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
