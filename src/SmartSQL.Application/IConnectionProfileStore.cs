namespace SmartSQL.Application;

/// <summary>Stores complete sets of nonsecret profiles; malformed persisted settings fail explicitly.</summary>
public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
}
