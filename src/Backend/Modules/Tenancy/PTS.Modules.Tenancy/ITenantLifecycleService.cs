using PTS.SharedKernel.Identity;

namespace PTS.Modules.Tenancy;

/// <summary>
/// Authenticated tenant lifecycle: create organization, invite, accept.
/// UserId always comes from <see cref="ICurrentUser"/>, never from the caller.
/// </summary>
public interface ITenantLifecycleService
{
    Task<Tenant> CreateTenantAsync(string name, string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates organization metadata (name). Authorization is Owner/Admin
    /// Active membership, evaluated on the server — never from the client.
    /// The identifier (slug) is not mutable: routing and uniqueness treat it
    /// as a creation-time value; TenantId remains the real identity.
    /// </summary>
    Task<Tenant> UpdateTenantAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    Task<Membership> InviteAsync(Guid tenantId, Guid inviteeUserId, CancellationToken cancellationToken = default);

    Task<Membership> AcceptInvitationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessibleTenant>> ListAccessibleTenantsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessibleTenant>> ListPendingInvitationsAsync(CancellationToken cancellationToken = default);
}
