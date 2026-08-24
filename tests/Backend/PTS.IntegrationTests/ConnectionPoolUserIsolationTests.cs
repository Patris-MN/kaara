using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence.Testing;
using PTS.Modules.Identity;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ConnectionPoolUserIsolationTests
{
    private readonly PostgresFixture _fixture;

    public ConnectionPoolUserIsolationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Alternating_users_on_pooled_sessions_do_not_leak_identity()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("PoolUserA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("PoolUserB");

        await SeedAsync(userA, tenantA, "pool-user-A");
        await SeedAsync(userB, tenantB, "pool-user-B");

        for (var i = 0; i < 20; i++)
        {
            await AssertOnlyOwnAsync(userA, tenantA, "pool-user-A");
            await AssertOnlyOwnAsync(userB, tenantB, "pool-user-B");
        }
    }

    [SkippableFact]
    public async Task Unknown_authenticated_user_id_is_rejected()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var tenantId = await factory.CreateTenantAsync("UnknownUserTenant");
        var unknownUserId = Guid.NewGuid();

        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(unknownUserId);
        var sessions = scope.ServiceProvider.GetRequiredService<PTS.Host.TenantAccess.ITenantRlsSessionFactory>();

        await Assert.ThrowsAsync<UnknownAuthenticatedUserException>(
            () => sessions.OpenAsync(tenantId));
    }

    [SkippableFact]
    public async Task Failed_session_does_not_leak_identity_into_the_next_request()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("AfterFailA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("AfterFailB");
        await SeedAsync(userB, tenantB, "after-fail-B");

        try
        {
            await using var failed = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantB);
        }
        catch (PTS.Host.TenantAccess.TenantAccessDeniedException)
        {
            // expected
        }

        await AssertOnlyOwnAsync(userB, tenantB, "after-fail-B");
    }

    private async Task SeedAsync(Guid userId, Guid tenantId, string value)
    {
        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
        session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Value = value,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await session.DbContext.SaveChangesAsync();
        await session.CommitAsync();
    }

    private async Task AssertOnlyOwnAsync(Guid userId, Guid tenantId, string expected)
    {
        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            session.DbContext.TenantIsolationTestRecords);
        await session.CommitAsync();
        Assert.All(rows, r => Assert.Equal(tenantId, r.TenantId));
        Assert.Contains(rows, r => r.Value == expected);
    }
}
