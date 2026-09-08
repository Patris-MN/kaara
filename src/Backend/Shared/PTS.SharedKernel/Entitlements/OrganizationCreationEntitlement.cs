namespace PTS.SharedKernel.Entitlements;

/// <summary>
/// Global account-scoped answer to whether the authenticated user may create
/// a new Organization. Not derived from tenant Membership role alone.
/// </summary>
public sealed record OrganizationCreationEntitlement(
    bool CanCreateOrganization,
    /// <summary>
    /// Maximum organizations the account may own. <c>-1</c> means unlimited
    /// (development policy for existing Owners until Billing supplies limits).
    /// </summary>
    int OrganizationLimit,
    /// <summary>
    /// Active Owner memberships — organizations this user currently owns.
    /// </summary>
    int CurrentOrganizationCount,
    int ActiveMembershipCount,
    int PendingInvitationCount);
