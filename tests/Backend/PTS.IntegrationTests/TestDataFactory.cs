using Microsoft.EntityFrameworkCore;
using PTS.Host.Persistence;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

/// <summary>
/// Seeds Users/Tenants/Memberships via <see cref="AppDbContext"/>. After Phase 3,
/// users and memberships are RLS-protected: inserts must SET LOCAL
/// <c>app.current_user_id</c> to the row's user id. Tenants allow INSERT
/// without a user GUC (organization creation bootstrap); SELECT is still
/// membership-gated.
/// </summary>
public sealed class TestDataFactory
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public TestDataFactory(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Guid> CreateTenantAsync(string namePrefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"{namePrefix} {suffix}",
            Slug = $"{namePrefix.ToLowerInvariant().Replace(' ', '-')}-{suffix}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, Guid.NewGuid(), CancellationToken.None);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return tenant.Id;
    }

    public async Task<Guid> CreateUserAsync(string emailPrefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{emailPrefix}+{suffix}@example.test",
            DisplayName = $"{emailPrefix} {suffix}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, user.Id, CancellationToken.None);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return user.Id;
    }

    public async Task CreateMembershipAsync(
        Guid userId,
        Guid tenantId,
        MembershipStatus status = MembershipStatus.Active,
        MembershipRole role = MembershipRole.Member)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, CancellationToken.None);
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            Status = status,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public Task CreateActiveMembershipAsync(Guid userId, Guid tenantId, MembershipRole role = MembershipRole.Member)
        => CreateMembershipAsync(userId, tenantId, MembershipStatus.Active, role);

    public async Task<(Guid UserId, Guid TenantId)> CreateUserWithTenantAsync(string namePrefix)
    {
        var tenantId = await CreateTenantAsync(namePrefix);
        var userId = await CreateUserAsync(namePrefix);
        await CreateActiveMembershipAsync(userId, tenantId);
        return (userId, tenantId);
    }
}
