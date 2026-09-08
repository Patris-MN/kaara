using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Security;

namespace PTS.Host.Persistence;

internal sealed class EfTenantInvitationStore : ITenantInvitationStore
{
    public const int InvitationLifetimeDays = 7;

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IHostEnvironment _environment;

    public EfTenantInvitationStore(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IHostEnvironment environment)
    {
        _dbContextFactory = dbContextFactory;
        _environment = environment;
    }

    public async Task<InvitationDeliveryResult> CreateInvitationAsync(
        Guid actingUserId,
        Guid tenantId,
        string invitedEmail,
        MembershipRole role,
        IReadOnlyList<(Guid WorkspaceId, string AccessLevel)> workspaceGrants,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(invitedEmail);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvitationNotAllowedException("Email is required.");
        }

        if (role is MembershipRole.Owner)
        {
            throw new InvitationNotAllowedException("Owner role cannot be assigned through invitation.");
        }

        var effectiveGrants = role == MembershipRole.Member ? workspaceGrants : Array.Empty<(Guid, string)>();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);

        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);
        await EnsureCanInviteEmailAsync(db, actingUserId, tenantId, normalizedEmail, cancellationToken);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstAsync(item => item.Id == tenantId, cancellationToken);
        var inviterDisplayName = await db.Users.AsNoTracking()
            .Where(item => item.Id == actingUserId)
            .Select(item => item.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        var credential = await db.UserCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);

        if (credential?.UserId == actingUserId)
        {
            throw new InvitationNotAllowedException("A user cannot invite themselves.");
        }

        await RevokePendingInvitationsForEmailAsync(db, tenantId, normalizedEmail, cancellationToken);

        Membership? invitedMembership = null;
        if (credential is not null)
        {
            invitedMembership = new Membership
            {
                Id = Guid.NewGuid(),
                UserId = credential.UserId,
                TenantId = tenantId,
                Role = role,
                Status = MembershipStatus.Invited,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Memberships.Add(invitedMembership);
        }

        var (rawToken, tokenHash) = InvitationTokenGenerator.Generate();
        var now = DateTimeOffset.UtcNow;
        var invitation = new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvitedEmail = normalizedEmail,
            Role = role,
            TokenHash = tokenHash,
            ExpiresAtUtc = now.AddDays(InvitationLifetimeDays),
            CreatedByMembershipId = actor.Id,
            OrganizationName = tenant.Name,
            InviterDisplayName = inviterDisplayName,
            InviteeUserId = credential?.UserId,
            MembershipId = invitedMembership?.Id,
            CreatedAtUtc = now,
        };
        db.TenantInvitations.Add(invitation);

        await AddWorkspaceGrantsAsync(db, tenantId, invitation.Id, effectiveGrants, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvitationNotAllowedException("A membership for this user and tenant already exists.");
        }

        return BuildDeliveryResult(invitation.Id, normalizedEmail, rawToken, invitation.ExpiresAtUtc);
    }

    public async Task<InvitationPreview?> FindPreviewByRawTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var invitation = await FindActiveInvitationByRawTokenAsync(db, rawToken, track: false, cancellationToken);
        if (invitation is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        ValidateInvitationUsable(invitation);

        var grants = await db.InvitationWorkspaceGrants.AsNoTracking()
            .Where(item => item.InvitationId == invitation.Id)
            .OrderBy(item => item.WorkspaceName)
            .Select(item => new InvitationWorkspaceGrantPreview(item.WorkspaceName, item.AccessLevel.ToString()))
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new InvitationPreview
        {
            OrganizationName = invitation.OrganizationName,
            InvitedEmail = invitation.InvitedEmail,
            Role = invitation.Role,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            InviterDisplayName = invitation.InviterDisplayName,
            RequiresRegistration = invitation.InviteeUserId is null,
            WorkspaceGrants = grants,
        };
    }

    public async Task<Membership> AcceptByRawTokenAsync(
        Guid authenticatedUserId,
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, authenticatedUserId, cancellationToken);

        var invitation = await FindActiveInvitationByRawTokenAsync(db, rawToken, track: true, cancellationToken)
            ?? throw new InvitationNotFoundException();
        ValidateInvitationUsable(invitation);

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == authenticatedUserId, cancellationToken)
            ?? throw new InvitationNotFoundException();

        var userEmail = await db.UserCredentials.AsNoTracking()
            .Where(item => item.UserId == authenticatedUserId)
            .Select(item => item.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.Equals(userEmail, invitation.InvitedEmail, StringComparison.Ordinal))
        {
            throw new InvitationEmailMismatchException();
        }

        var membership = await ActivateInvitationInternalAsync(db, invitation, authenticatedUserId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return membership;
    }

    public async Task<Membership> RegisterAndAcceptByRawTokenAsync(
        string rawToken,
        string displayName,
        string password,
        Func<string, string, string, CancellationToken, Task<Guid>> registerUserAsync,
        CancellationToken cancellationToken = default)
    {
        var preview = await FindPreviewByRawTokenAsync(rawToken, cancellationToken)
            ?? throw new InvitationNotFoundException();

        if (!preview.RequiresRegistration)
        {
            throw new InvitationNotAllowedException("This invitation requires signing in with an existing account.");
        }

        var userId = await registerUserAsync(
            preview.InvitedEmail,
            password,
            displayName,
            cancellationToken);

        return await AcceptByRawTokenAsync(userId, rawToken, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingTenantInvitation>> ListPendingForTenantAsync(
        Guid actingUserId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var rows = await db.TenantInvitations.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId
                && item.UsedAtUtc == null
                && item.RevokedAtUtc == null
                && item.ExpiresAtUtc > now)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new PendingTenantInvitation
            {
                Id = item.Id,
                InvitedEmail = item.InvitedEmail,
                Role = item.Role,
                ExpiresAtUtc = item.ExpiresAtUtc,
                CreatedAtUtc = item.CreatedAtUtc,
                MembershipId = item.MembershipId,
            })
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    public async Task<InvitationDeliveryResult> ResendAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);

        var existing = await db.TenantInvitations
            .FirstOrDefaultAsync(item => item.Id == invitationId && item.TenantId == tenantId, cancellationToken)
            ?? throw new InvitationNotFoundException();

        if (existing.UsedAtUtc is not null)
        {
            throw new InvitationAlreadyUsedException();
        }

        var grants = await db.InvitationWorkspaceGrants.AsNoTracking()
            .Where(item => item.InvitationId == invitationId)
            .Select(item => new { item.WorkspaceId, AccessLevel = item.AccessLevel.ToString() })
            .ToListAsync(cancellationToken);

        existing.RevokedAtUtc = DateTimeOffset.UtcNow;
        if (existing.MembershipId is Guid membershipId)
        {
            var membership = await db.Memberships
                .FirstOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
            if (membership is { Status: MembershipStatus.Invited })
            {
                db.Memberships.Remove(membership);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await CreateInvitationAsync(
            actingUserId,
            tenantId,
            existing.InvitedEmail,
            existing.Role,
            grants.Select(item => (item.WorkspaceId, item.AccessLevel)).ToList(),
            cancellationToken);
    }

    public async Task RevokeAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);

        var invitation = await db.TenantInvitations
            .FirstOrDefaultAsync(item => item.Id == invitationId && item.TenantId == tenantId, cancellationToken)
            ?? throw new InvitationNotFoundException();

        if (invitation.UsedAtUtc is not null)
        {
            throw new InvitationAlreadyUsedException();
        }

        invitation.RevokedAtUtc = DateTimeOffset.UtcNow;

        if (invitation.MembershipId is Guid membershipId)
        {
            var membership = await db.Memberships
                .FirstOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
            if (membership is { Status: MembershipStatus.Invited })
            {
                db.Memberships.Remove(membership);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Membership> UpdateMemberRoleAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        MembershipRole newRole,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);

        var target = await db.Memberships
            .FirstOrDefaultAsync(item => item.Id == membershipId && item.TenantId == tenantId, cancellationToken)
            ?? throw new MemberManagementForbiddenException();

        if (target.Status != MembershipStatus.Active)
        {
            throw new MemberManagementForbiddenException();
        }

        if (target.Role == MembershipRole.Owner && newRole != MembershipRole.Owner)
        {
            var ownerCount = await db.Memberships.CountAsync(
                item => item.TenantId == tenantId
                        && item.Status == MembershipStatus.Active
                        && item.Role == MembershipRole.Owner,
                cancellationToken);
            if (ownerCount <= 1)
            {
                throw new OwnerProtectionException();
            }
        }

        if (newRole == MembershipRole.Owner)
        {
            throw new MemberManagementForbiddenException();
        }

        target.Role = newRole;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return target;
    }

    public async Task<Membership> UpdateMemberStatusAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        MembershipStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        if (newStatus is not (MembershipStatus.Active or MembershipStatus.Suspended))
        {
            throw new MemberManagementForbiddenException();
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);

        var target = await db.Memberships
            .FirstOrDefaultAsync(item => item.Id == membershipId && item.TenantId == tenantId, cancellationToken)
            ?? throw new MemberManagementForbiddenException();

        if (target.UserId == actingUserId)
        {
            throw new MemberManagementForbiddenException();
        }

        if (target.Status != MembershipStatus.Active && target.Status != MembershipStatus.Suspended)
        {
            throw new MemberManagementForbiddenException();
        }

        if (target.Role == MembershipRole.Owner && newStatus == MembershipStatus.Suspended)
        {
            var activeOwners = await db.Memberships.CountAsync(
                item => item.TenantId == tenantId
                        && item.Status == MembershipStatus.Active
                        && item.Role == MembershipRole.Owner,
                cancellationToken);
            if (activeOwners <= 1)
            {
                throw new OwnerProtectionException();
            }
        }

        target.Status = newStatus;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return target;
    }

    public async Task<Membership> RemoveMemberFromOrganizationAsync(
        Guid actingUserId,
        Guid tenantId,
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, tenantId, cancellationToken);
        var actor = await RequireActiveManagerAsync(db, actingUserId, tenantId, cancellationToken);
        await ApplyManagerRlsContextAsync(db, actor, cancellationToken);

        var target = await db.Memberships
            .FirstOrDefaultAsync(item => item.Id == membershipId && item.TenantId == tenantId, cancellationToken)
            ?? throw new MemberManagementForbiddenException();

        if (target.UserId == actingUserId)
        {
            throw new MemberManagementForbiddenException();
        }

        if (target.Status is not (MembershipStatus.Active or MembershipStatus.Suspended))
        {
            throw new MemberManagementForbiddenException();
        }

        if (target.Role == MembershipRole.Owner)
        {
            var activeOwners = await db.Memberships.CountAsync(
                item => item.TenantId == tenantId
                        && item.Status == MembershipStatus.Active
                        && item.Role == MembershipRole.Owner,
                cancellationToken);
            if (activeOwners <= 1)
            {
                throw new OwnerProtectionException();
            }
        }

        var workspaceAccess = await db.WorkspaceAccess
            .Where(item => item.MembershipId == membershipId)
            .ToListAsync(cancellationToken);
        if (workspaceAccess.Count > 0)
        {
            db.WorkspaceAccess.RemoveRange(workspaceAccess);
        }

        target.Status = MembershipStatus.Removed;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return target;
    }

    private async Task<Membership> ActivateInvitationInternalAsync(
        AppDbContext db,
        TenantInvitation invitation,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, invitation.TenantId, cancellationToken);

        var existing = await db.Memberships
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.TenantId == invitation.TenantId,
                cancellationToken);

        if (existing is { Status: MembershipStatus.Active })
        {
            throw new AlreadyTenantMemberException();
        }

        Membership membership;
        if (existing is not null)
        {
            membership = existing;
            membership.Status = MembershipStatus.Active;
            membership.Role = invitation.Role;
        }
        else if (invitation.MembershipId is Guid membershipId)
        {
            membership = await db.Memberships
                .FirstAsync(item => item.Id == membershipId, cancellationToken);
            membership.Status = MembershipStatus.Active;
            membership.Role = invitation.Role;
        }
        else
        {
            membership = new Membership
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = invitation.TenantId,
                Role = invitation.Role,
                Status = MembershipStatus.Active,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Memberships.Add(membership);
            invitation.MembershipId = membership.Id;
        }

        invitation.UsedAtUtc = DateTimeOffset.UtcNow;
        invitation.InviteeUserId = userId;

        if (membership.Role == MembershipRole.Member)
        {
            await ApplyInvitationWorkspaceGrantsAsync(db, invitation, membership.Id, cancellationToken);
        }

        return membership;
    }

    private static async Task ApplyInvitationWorkspaceGrantsAsync(
        AppDbContext db,
        TenantInvitation invitation,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var grants = await db.InvitationWorkspaceGrants.AsNoTracking()
            .Where(item => item.InvitationId == invitation.Id)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var grant in grants)
        {
            var existing = await db.WorkspaceAccess.FirstOrDefaultAsync(
                item => item.MembershipId == membershipId && item.WorkspaceId == grant.WorkspaceId,
                cancellationToken);
            if (existing is null)
            {
                db.WorkspaceAccess.Add(new WorkspaceAccess
                {
                    Id = Guid.NewGuid(),
                    TenantId = invitation.TenantId,
                    MembershipId = membershipId,
                    WorkspaceId = grant.WorkspaceId,
                    AccessLevel = grant.AccessLevel,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                existing.AccessLevel = grant.AccessLevel;
                existing.UpdatedAtUtc = now;
            }
        }
    }

    private static async Task<TenantInvitation?> FindActiveInvitationByRawTokenAsync(
        AppDbContext db,
        string rawToken,
        bool track,
        CancellationToken cancellationToken)
    {
        var tokenHash = InvitationTokenGenerator.Hash(rawToken);
        await PostgresRlsSettings.SetInvitationTokenHashAsync(db, tokenHash, cancellationToken);

        var query = track ? db.TenantInvitations : db.TenantInvitations.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
    }

    private static void ValidateInvitationUsable(TenantInvitation invitation)
    {
        if (invitation.RevokedAtUtc is not null)
        {
            throw new InvitationRevokedException();
        }

        if (invitation.UsedAtUtc is not null)
        {
            throw new InvitationAlreadyUsedException();
        }

        if (invitation.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvitationExpiredException();
        }
    }

    private static async Task<Membership> RequireActiveManagerAsync(
        AppDbContext db,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);

        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            throw new MemberManagementForbiddenException();
        }

        if (membership.Role is not MembershipRole.Owner and not MembershipRole.Admin)
        {
            throw new MemberManagementForbiddenException();
        }

        return membership;
    }

    private static async Task ApplyManagerRlsContextAsync(
        AppDbContext db,
        Membership actor,
        CancellationToken cancellationToken)
    {
        await PostgresRlsSettings.SetCurrentMembershipRoleAsync(db, actor.Role, cancellationToken);
        await PostgresRlsSettings.SetCurrentMembershipIdAsync(db, actor.Id, cancellationToken);
    }

    private static async Task EnsureCanInviteEmailAsync(
        AppDbContext db,
        Guid actingUserId,
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var userId = await db.UserCredentials.AsNoTracking()
            .Where(item => item.Email == normalizedEmail)
            .Select(item => item.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId == Guid.Empty)
        {
            return;
        }

        var existing = await db.Memberships.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);

        if (existing is { Status: MembershipStatus.Active })
        {
            throw new AlreadyTenantMemberException();
        }

        if (existing is { Status: MembershipStatus.Invited })
        {
            throw new InvitationNotAllowedException("This user already has a pending invitation.");
        }
    }

    private static async Task RevokePendingInvitationsForEmailAsync(
        AppDbContext db,
        Guid tenantId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var pending = await db.TenantInvitations
            .Where(item =>
                item.TenantId == tenantId
                && item.InvitedEmail == normalizedEmail
                && item.UsedAtUtc == null
                && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var invitation in pending)
        {
            invitation.RevokedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static async Task AddWorkspaceGrantsAsync(
        AppDbContext db,
        Guid tenantId,
        Guid invitationId,
        IReadOnlyList<(Guid WorkspaceId, string AccessLevel)> workspaceGrants,
        CancellationToken cancellationToken)
    {
        foreach (var (workspaceId, accessLevelRaw) in workspaceGrants)
        {
            if (!Enum.TryParse<WorkspaceAccessLevel>(accessLevelRaw, true, out var accessLevel))
            {
                throw new InvitationNotAllowedException("Invalid workspace access level.");
            }

            var exists = await db.Workspaces.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Id == workspaceId)
                .Select(item => new { item.Id, item.Name })
                .FirstOrDefaultAsync(cancellationToken);
            if (exists is null)
            {
                throw new InvitationNotAllowedException("Workspace not found in this organization.");
            }

            db.InvitationWorkspaceGrants.Add(new InvitationWorkspaceGrant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvitationId = invitationId,
                WorkspaceId = workspaceId,
                WorkspaceName = exists.Name,
                AccessLevel = accessLevel,
            });
        }
    }

    private InvitationDeliveryResult BuildDeliveryResult(
        Guid invitationId,
        string invitedEmail,
        string rawToken,
        DateTimeOffset expiresAtUtc)
        => new()
        {
            InvitationId = invitationId,
            InvitedEmail = invitedEmail,
            RawToken = rawToken,
            ExpiresAtUtc = expiresAtUtc,
            DevelopmentInvitationUrl = _environment.IsDevelopment()
                ? $"/invite/{rawToken}"
                : null,
        };

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
