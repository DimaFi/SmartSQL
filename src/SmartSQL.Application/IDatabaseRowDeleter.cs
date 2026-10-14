using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>Optional, explicitly enabled deletion of one confirmed native row.</summary>
public interface IDatabaseRowDeleter : IDatabaseBrowser
{
    bool EditingEnabled { get; }
    Task<DatabaseDeleteCapabilities> GetDeleteCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default);
    Task<DatabaseDeleteResult> DeleteRowAsync(DatabaseRowDelete request,
        CancellationToken cancellationToken = default);
}
