namespace PTS.Modules.Tenancy;

/// <summary>
/// Narrow persistence port for tenant create / invite / accept. Not a generic
/// repository. Implemented by the Host so Tenancy never references EF/Npgsql.
/// </summary>
public interface ITenantLifecycleStore
{
    Task CreateTenantWithOwnerAsync(Tenant tenant, Membership ownerMembership, CancellationToken cancellationToken = default);

    Task<Membership?> FindMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    Task AddInvitedMembershipAsync(Membership membership, Guid actingUserId, CancellationToken cancellationToken = default);

    Task ActivateInvitationAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Memberships of <paramref name="userId"/> in <paramref name="status"/>,
    /// joined to tenant metadata. Relies on RLS: memberships (self) plus
    /// tenants SELECT (Active, and Invited via the dedicated invited policy).
    /// </summary>
    Task<IReadOnlyList<AccessibleTenant>> ListByStatusAsync(
        Guid userId,
        MembershipStatus status,
        CancellationToken cancellationToken = default);
}
