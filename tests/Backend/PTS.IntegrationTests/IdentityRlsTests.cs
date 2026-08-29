using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class IdentityRlsTests
{
    private readonly PostgresFixture _fixture;

    public IdentityRlsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Authenticated_user_cannot_read_unrelated_users_without_an_application_filter()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var userA = await factory.CreateUserAsync("UserDirA");
        var userB = await factory.CreateUserAsync("UserDirB");

        await using var db = await _fixture.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userA, CancellationToken.None);

        var visible = await db.Users.ToListAsync();

        Assert.Contains(visible, u => u.Id == userA);
        Assert.DoesNotContain(visible, u => u.Id == userB);
    }

    [SkippableFact]
    public async Task User_can_read_only_their_own_memberships_without_an_application_filter()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("MemA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("MemB");

        await using var db = await _fixture.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userA, CancellationToken.None);

        var visible = await db.Memberships.ToListAsync();

        Assert.All(visible, m => Assert.Equal(userA, m.UserId));
        Assert.DoesNotContain(visible, m => m.UserId == userB);
        Assert.DoesNotContain(visible, m => m.TenantId == tenantB && m.UserId == userB);
        Assert.Contains(visible, m => m.TenantId == tenantA);
    }

    [SkippableFact]
    public async Task User_cannot_read_a_tenant_they_have_no_active_membership_for_just_by_knowing_its_id()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("TenA");
        var tenantB = await factory.CreateTenantAsync("TenBHidden");

        await using var db = await _fixture.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userA, CancellationToken.None);

        var visible = await db.Tenants.ToListAsync();

        Assert.Contains(visible, t => t.Id == tenantA);
        Assert.DoesNotContain(visible, t => t.Id == tenantB);
        Assert.Null(await db.Tenants.FindAsync(tenantB));
    }

    [SkippableFact]
    public async Task Suspended_membership_does_not_make_the_tenant_visible()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var userId = await factory.CreateUserAsync("SuspendedVisible");
        var tenantId = await factory.CreateTenantAsync("SuspendedVisibleTenant");
        await factory.CreateMembershipAsync(userId, tenantId, MembershipStatus.Suspended);

        await using var db = await _fixture.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, CancellationToken.None);

        var visible = await db.Tenants.ToListAsync();
        Assert.DoesNotContain(visible, t => t.Id == tenantId);
    }

    [SkippableFact]
    public async Task Tenant_peer_select_requires_current_tenant_context()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("PeerA");
        var userB = await factory.CreateUserAsync("PeerB");
        await factory.CreateActiveMembershipAsync(userB, tenantA);
        var (userC, _) = await factory.CreateUserWithTenantAsync("PeerC");

        await using (var db = await _fixture.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await PostgresRlsSettings.SetCurrentUserIdAsync(db, userA, CancellationToken.None);
            var visible = await db.Users.ToListAsync();
            Assert.Contains(visible, user => user.Id == userA);
            Assert.DoesNotContain(visible, user => user.Id == userB);
            Assert.DoesNotContain(visible, user => user.Id == userC);
        }

        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
        var peers = await session.DbContext.Users.ToListAsync();
        Assert.Contains(peers, user => user.Id == userA);
        Assert.Contains(peers, user => user.Id == userB);
        Assert.DoesNotContain(peers, user => user.Id == userC);
        var memberships = await session.DbContext.Memberships.ToListAsync();
        Assert.All(memberships, membership => Assert.Equal(tenantA, membership.TenantId));
        Assert.Contains(memberships, membership => membership.UserId == userB);
    }
}
