using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence.Testing;

namespace PTS.IntegrationTests;

/// <summary>
/// Step 11 — connection-pool reuse tests. Each "request" below opens a brand
/// new <see cref="ScopedTenantSession"/> (a new DI scope, and a new pooled
/// Npgsql connection — or a reused one, we don't control which, which is the
/// point) and asserts it only ever sees its own tenant's row, regardless of
/// what any earlier "request" on a possibly-shared physical connection did.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConnectionPoolIsolationTests
{
    private readonly PostgresFixture _fixture;

    public ConnectionPoolIsolationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid UserId, Guid TenantId, string ExpectedValue)> SeedTenantAsync(string prefix)
    {
        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userId, tenantId) = await factory.CreateUserWithTenantAsync(prefix);
        var value = $"{prefix}-value";

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

        return (userId, tenantId, value);
    }

    private async Task AssertRequestSeesOnlyItsOwnTenantAsync((Guid UserId, Guid TenantId, string ExpectedValue) tenant)
    {
        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, tenant.UserId, tenant.TenantId);
        var records = await session.DbContext.TenantIsolationTestRecords.ToListAsync();
        await session.CommitAsync();

        Assert.All(records, r => Assert.Equal(tenant.TenantId, r.TenantId));
        Assert.Contains(records, r => r.Value == tenant.ExpectedValue);
    }

    [SkippableFact]
    public async Task A_then_B_then_A_again_never_leaks_across_pooled_connections()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var tenantA = await SeedTenantAsync("PoolAthenB-A");
        var tenantB = await SeedTenantAsync("PoolAthenB-B");

        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantA); // A
        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantB); // B
        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantA); // A again
    }

    [SkippableFact]
    public async Task B_then_A_then_B_again_never_leaks_across_pooled_connections()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var tenantA = await SeedTenantAsync("PoolBthenA-A");
        var tenantB = await SeedTenantAsync("PoolBthenA-B");

        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantB); // B
        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantA); // A
        await AssertRequestSeesOnlyItsOwnTenantAsync(tenantB); // B again
    }

    [SkippableFact]
    public async Task Repeated_alternation_over_many_iterations_never_leaks()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var tenantA = await SeedTenantAsync("PoolRepeatA");
        var tenantB = await SeedTenantAsync("PoolRepeatB");

        // Enough iterations to exercise real Npgsql connection-pool reuse
        // (the pool has far fewer physical connections than iterations here).
        for (var i = 0; i < 40; i++)
        {
            await AssertRequestSeesOnlyItsOwnTenantAsync(i % 2 == 0 ? tenantA : tenantB);
        }
    }

    [SkippableFact]
    public async Task Concurrent_tenant_A_and_tenant_B_requests_never_cross_contaminate()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var tenantA = await SeedTenantAsync("PoolConcurrentA");
        var tenantB = await SeedTenantAsync("PoolConcurrentB");

        // Many concurrent "requests" sharing the same Npgsql connection pool —
        // each must independently resolve/set its own SET LOCAL tenant context
        // on whichever physical connection it happens to be handed, and each
        // has its own DI scope (like a real concurrent set of HTTP requests).
        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            var tenant = i % 2 == 0 ? tenantA : tenantB;
            tasks.Add(AssertRequestSeesOnlyItsOwnTenantAsync(tenant));
        }

        await Task.WhenAll(tasks);
    }
}
