namespace PTS.Modules.PlatformAdministration;

/// <summary>
/// Persistence port for platform-operator grants. Implemented in the Host.
/// </summary>
public interface IPlatformAdministratorStore
{
    Task<bool> IsPlatformAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);

    Task EnsureAsync(Guid userId, CancellationToken cancellationToken = default);
}
