using Microsoft.EntityFrameworkCore;
using Npgsql;
using PTS.Host.Persistence;
using PTS.Modules.Tenancy;

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

        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}
