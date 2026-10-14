namespace SmartSQL.Application;

/// <summary>Presentation preference only; never grants database permissions or enables writes.</summary>
public enum WorkspacePresentationMode
{
    Simple,
    Advanced
}

public interface IWorkspacePreferencesStore
{
    Task<WorkspacePresentationMode?> LoadModeAsync(CancellationToken cancellationToken = default);
    Task SaveModeAsync(WorkspacePresentationMode mode, CancellationToken cancellationToken = default);
}
