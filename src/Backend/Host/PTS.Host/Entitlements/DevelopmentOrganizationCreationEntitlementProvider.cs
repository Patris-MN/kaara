using PTS.Modules.Tenancy;
using PTS.SharedKernel.Entitlements;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Entitlements;

/// <summary>
/// Pre-Billing development policy for Organization creation.
/// <list type="bullet">
/// <item>Fresh independent signup (no active memberships, no pending invitations) may create a first Organization.</item>
/// <item>Users with at least one Active Owner membership may create additional Organizations for multi-org testing.</item>
/// <item>Tenant Admin/Member roles never grant global creation by themselves.</item>
/// <item>Users with pending invitations but no active memberships must recover/accept invitations first.</item>
/// </list>
/// </summary>
internal sealed class DevelopmentOrganizationCreationEntitlementProvider : IOrganizationCreationEntitlementProvider
{
    public const int UnlimitedOrganizationLimit = -1;
    public const int FirstOrganizationLimit = 1;

    private readonly ICurrentUser _currentUser;
    private readonly ITenantLifecycleService _lifecycle;

    public DevelopmentOrganizationCreationEntitlementProvider(
        ICurrentUser currentUser,
        ITenantLifecycleService lifecycle)
    {
        _currentUser = currentUser;
        _lifecycle = lifecycle;
    }

    public async Task<OrganizationCreationEntitlement> GetForCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not Guid)
        {
            throw new UnauthenticatedException();
        }

        var active = await _lifecycle.ListAccessibleTenantsAsync(cancellationToken);
        var pending = await _lifecycle.ListPendingInvitationsAsync(cancellationToken);

        var ownerCount = active.Count(tenant => tenant.Role == MembershipRole.Owner);
        var activeCount = active.Count;
        var pendingCount = pending.Count;

        var canCreate = ownerCount > 0 || (activeCount == 0 && pendingCount == 0);
        var limit = ownerCount > 0 ? UnlimitedOrganizationLimit : FirstOrganizationLimit;

        return new OrganizationCreationEntitlement(
            canCreate,
            limit,
            ownerCount,
            activeCount,
            pendingCount);
    }
}
