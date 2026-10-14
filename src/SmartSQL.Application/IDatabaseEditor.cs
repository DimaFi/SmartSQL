using SmartSQL.Domain;

namespace SmartSQL.Application;

/// <summary>
/// Optional, explicitly enabled editing of ordinary database cells.
/// Providers own native metadata, permissions, identity snapshots and concurrency checks.
/// </summary>
public interface IDatabaseEditor : IDatabaseBrowser
{
    bool EditingEnabled { get; }

    Task<DatabaseEditCapabilities> GetEditCapabilitiesAsync(DatabaseTableRef table,
        CancellationToken cancellationToken = default);

    Task<DatabaseUpdateResult> UpdateCellAsync(DatabaseCellUpdate request,
        CancellationToken cancellationToken = default);
}
