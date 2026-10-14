using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Optional, explicitly enabled creation of a single native row.</summary>
public interface IDatabaseRowCreator : IDatabaseBrowser
{
    bool EditingEnabled { get; }
    Task<DatabaseInsertCapabilities> GetInsertCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default);
    Task<DatabaseInsertResult> InsertRowAsync(DatabaseInsertRequest request,
        CancellationToken cancellationToken = default);
}
