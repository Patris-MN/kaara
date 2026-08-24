using Microsoft.EntityFrameworkCore;
using PTS.Modules.Tenancy;

namespace PTS.Host.Persistence;

/// <summary>
/// EF Core adapter for Tenancy's <see cref="IMembershipLookup"/> port.
///
/// Memberships RLS allows a row only when memberships.user_id equals
/// <c>app.current_user_id</c>. This lookup therefore SET LOCALs that GUC to
/// the userId being asked about (which the caller already authenticated)
/// inside its own transaction, without setting a tenant GUC. That is the
/// bootstrap step: user identity first, tenant context later.
/// </summary>
internal sealed class EfMembershipLookup : IMembershipLookup
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public EfMembershipLookup(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Membership?> FindActiveMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        return await db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.UserId == userId && m.TenantId == tenantId && m.Status == MembershipStatus.Active,
                cancellationToken);
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
}
