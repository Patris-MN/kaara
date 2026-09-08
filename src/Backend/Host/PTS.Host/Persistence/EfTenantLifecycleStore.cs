using Microsoft.EntityFrameworkCore;
using Npgsql;
using PTS.Host.Persistence;
using PTS.Modules.Tenancy;
using PTS.SharedKernel.Text;

namespace PTS.Host.Persistence;

/// <summary>
/// EF adapter for tenant create / invite / accept. Create and accept run with
/// only <c>app.current_user_id</c> (no tenant GUC yet, or not required).
/// Invite runs with both GUCs so the extra memberships INSERT policy can
/// allow inserting another user's Invited row.
/// </summary>
internal sealed class EfTenantLifecycleStore : ITenantLifecycleStore
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public EfTenantLifecycleStore(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task CreateTenantWithOwnerAsync(Tenant tenant, Membership ownerMembership, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, ownerMembership.UserId, cancellationToken);

        var normalizedName = ResourceNameNormalizer.Normalize(tenant.Name);
        var conflictingName = await FindUserOrganizationNameConflictAsync(
            db,
            ownerMembership.UserId,
            normalizedName,
            excludeTenantId: null,
            cancellationToken);
        if (conflictingName is not null)
        {
            throw new TenantNameConflictException(conflictingName);
        }

        db.Tenants.Add(tenant);
        db.Memberships.Add(ownerMembership);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DuplicateSlugException(tenant.Slug);
        }
    }

    public async Task<Tenant> UpdateTenantAsync(Guid userId, Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        var membership = await db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            throw new TenantUpdateForbiddenException();
        }

        if (membership.Role is not MembershipRole.Owner and not MembershipRole.Admin)
        {
            throw new TenantUpdateForbiddenException();
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new TenantUpdateForbiddenException();
        }

        var normalizedName = ResourceNameNormalizer.Normalize(name);
        var conflictingName = await FindUserOrganizationNameConflictAsync(
            db,
            userId,
            normalizedName,
            excludeTenantId: tenantId,
            cancellationToken);
        if (conflictingName is not null)
        {
            throw new TenantNameConflictException(conflictingName);
        }

        tenant.Name = name;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tenant;
    }

    public async Task<Membership?> FindMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        return await db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);
    }

    public async Task AddInvitedMembershipAsync(Membership membership, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, actingUserId, cancellationToken);
        await PostgresRlsSettings.SetCurrentTenantIdAsync(db, membership.TenantId, cancellationToken);

        db.Memberships.Add(membership);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvitationNotAllowedException("A membership for this user and tenant already exists.");
        }
    }

    public async Task ActivateInvitationAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, cancellationToken);

        if (membership is null || membership.Status != MembershipStatus.Invited)
        {
            throw new InvitationNotFoundException();
        }

        membership.Status = MembershipStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessibleTenant>> ListByStatusAsync(
        Guid userId,
        MembershipStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        var rows = await (
            from membership in db.Memberships.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            where membership.UserId == userId && membership.Status == status
            orderby tenant.Name
            select new AccessibleTenant(
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                membership.Role,
                membership.Status)).ToListAsync(cancellationToken);

        var counts = status == MembershipStatus.Active
            ? await LoadWorkspaceCountsAsync(db, cancellationToken)
            : new Dictionary<Guid, int>();

        await transaction.CommitAsync(cancellationToken);
        return rows
            .Select(row => row with { WorkspaceCount = counts.GetValueOrDefault(row.TenantId) })
            .ToList();
    }

    /// <summary>
    /// One aggregate under the current user GUC. Workspace RLS now includes a
    /// membership-gated SELECT policy so this does not require
    /// <c>app.current_tenant_id</c> or an N+1 per-tenant session. Tenancy
    /// still does not reference WorkManagement — only this Host adapter does.
    /// </summary>
    private static async Task<Dictionary<Guid, int>> LoadWorkspaceCountsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        return await db.Workspaces
            .AsNoTracking()
            .GroupBy(workspace => workspace.TenantId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static async Task<string?> FindUserOrganizationNameConflictAsync(
        AppDbContext db,
        Guid userId,
        string normalizedName,
        Guid? excludeTenantId,
        CancellationToken cancellationToken)
    {
        var names = await (
            from membership in db.Memberships.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on membership.TenantId equals tenant.Id
            where membership.UserId == userId
                  && membership.Status == MembershipStatus.Active
                  && (excludeTenantId == null || tenant.Id != excludeTenantId)
            select tenant.Name).ToListAsync(cancellationToken);

        return names.FirstOrDefault(name => ResourceNameNormalizer.Normalize(name) == normalizedName);
    }
}
