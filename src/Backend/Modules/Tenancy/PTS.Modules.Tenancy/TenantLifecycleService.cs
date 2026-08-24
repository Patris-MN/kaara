using PTS.SharedKernel.Identity;

namespace PTS.Modules.Tenancy;

/// <summary>
/// Tenant create + invitation lifecycle. Invitation role checks use
/// database-backed Membership, never JWT claims.
/// </summary>
public sealed class TenantLifecycleService : ITenantLifecycleService
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantLifecycleStore _store;

    public TenantLifecycleService(ICurrentUser currentUser, ITenantLifecycleStore store)
    {
        _currentUser = currentUser;
        _store = store;
    }

    public async Task<Tenant> CreateTenantAsync(string name, string slug, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var normalizedSlug = NormalizeSlug(slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            throw new ArgumentException("Slug is required.", nameof(slug));
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = normalizedSlug,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var owner = new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenant.Id,
            Role = MembershipRole.Owner,
            Status = MembershipStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await _store.CreateTenantWithOwnerAsync(tenant, owner, cancellationToken);
        return tenant;
    }

    public async Task<Membership> InviteAsync(Guid tenantId, Guid inviteeUserId, CancellationToken cancellationToken = default)
    {
        var actingUserId = RequireUser();
        if (inviteeUserId == actingUserId)
        {
            throw new InvitationNotAllowedException("A user cannot invite themselves.");
        }

        var actor = await _store.FindMembershipAsync(actingUserId, tenantId, cancellationToken);
        if (actor is null || actor.Status != MembershipStatus.Active)
        {
            throw new InvitationNotAllowedException("Active Owner or Admin membership is required to invite.");
        }

        if (actor.Role is not MembershipRole.Owner and not MembershipRole.Admin)
        {
            throw new InvitationNotAllowedException("Members cannot invite other users.");
        }

        var existing = await _store.FindMembershipAsync(inviteeUserId, tenantId, cancellationToken);
        if (existing is not null)
        {
            throw new InvitationNotAllowedException("A membership for this user and tenant already exists.");
        }

        var invited = new Membership
        {
            Id = Guid.NewGuid(),
            UserId = inviteeUserId,
            TenantId = tenantId,
            Role = MembershipRole.Member,
            Status = MembershipStatus.Invited,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await _store.AddInvitedMembershipAsync(invited, actingUserId, cancellationToken);
        return invited;
    }

    public async Task<Membership> AcceptInvitationAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        await _store.ActivateInvitationAsync(userId, tenantId, cancellationToken);
        var membership = await _store.FindMembershipAsync(userId, tenantId, cancellationToken);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            throw new InvitationNotFoundException();
        }

        return membership;
    }

    public Task<IReadOnlyList<AccessibleTenant>> ListAccessibleTenantsAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        return _store.ListByStatusAsync(userId, MembershipStatus.Active, cancellationToken);
    }

    public Task<IReadOnlyList<AccessibleTenant>> ListPendingInvitationsAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        return _store.ListByStatusAsync(userId, MembershipStatus.Invited, cancellationToken);
    }

    private Guid RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not Guid userId)
        {
            throw new UnauthenticatedException();
        }

        return userId;
    }

    private static string NormalizeSlug(string slug)
        => slug.Trim().ToLowerInvariant();
}
