namespace PTS.Modules.Tenancy;

/// <summary>
/// Server-side tenant-context resolution — the only legitimate path from
/// "authenticated user + a requested tenant" to an established TenantContext.
///
/// The conceptual flow (see docs/architecture/architecture-charter.md §4.1):
///   Authenticated User → Requested Tenant → Membership lookup →
///   Membership validation → TenantContext established.
///
/// <paramref name="requestedTenantId"/> may come from a client (e.g. a tenant
/// switcher), but it is never trusted directly — it is only a hint that gets
/// verified against a real, active <see cref="Membership"/> row before it can
/// become the resolved <see cref="TenantResolutionResult.TenantId"/>.
/// </summary>
public interface ITenantContextResolver
{
    Task<TenantResolutionResult> ResolveAsync(Guid userId, Guid requestedTenantId, CancellationToken cancellationToken = default);
}
