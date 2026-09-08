namespace PTS.Modules.Tenancy;

/// <summary>
/// Persists secure tenant invitations and token-scoped lookup.
/// Implemented in Host; Tenancy never references EF.
/// </summary>
public interface ITenantInvitationStore
{
    Task<InvitationDeliveryResult> CreateInvitationAsync(
        Guid actingUserId,
        Guid tenantId,
        string invitedEmail,
        MembershipRole role,
        IReadOnlyList<(Guid WorkspaceId, string AccessLevel)> workspaceGrants,
        CancellationToken cancellationToken = default);

    Task<InvitationPreview?> FindPreviewByRawTokenAsync(string rawToken, CancellationToken cancellationToken = default);

    Task<Membership> AcceptByRawTokenAsync(Guid authenticatedUserId, string rawToken, CancellationToken cancellationToken = default);

    Task<Membership> RegisterAndAcceptByRawTokenAsync(
        string rawToken,
        string displayName,
        string password,
        Func<string, string, string, CancellationToken, Task<Guid>> registerUserAsync,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingTenantInvitation>> ListPendingForTenantAsync(
        Guid actingUserId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<InvitationDeliveryResult> ResendAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid actingUserId, Guid tenantId, Guid invitationId, CancellationToken cancellationToken = default);

    Task<Membership> UpdateMemberRoleAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        MembershipRole newRole,
        CancellationToken cancellationToken = default);

    Task<Membership> UpdateMemberStatusAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        MembershipStatus newStatus,
        CancellationToken cancellationToken = default);

    Task<Membership> RemoveMemberFromOrganizationAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken = default);
}
