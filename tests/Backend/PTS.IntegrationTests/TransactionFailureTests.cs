using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence.Testing;

namespace PTS.IntegrationTests;

/// <summary>
/// Step 12 — proves tenant context cannot survive incorrectly across
/// requests, specifically around commit, rollback, and exceptions, and that
/// the connection pool remains healthy and correctly isolated afterward.
/// Each "request" opens its own <see cref="ScopedTenantSession"/> (its own DI
/// scope), exactly like a real ASP.NET Core request would.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TransactionFailureTests
{
    private readonly PostgresFixture _fixture;

    public TransactionFailureTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Committed_insert_is_visible_in_a_later_session_for_the_same_tenant()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userId, tenantId) = await factory.CreateUserWithTenantAsync("TxCommit");

        await using (var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId))
        {
            session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Value = "committed-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await session.DbContext.SaveChangesAsync();
            await session.CommitAsync();
        }

        await using var verify = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
        var rows = await verify.DbContext.TenantIsolationTestRecords.ToListAsync();
        await verify.CommitAsync();

        Assert.Contains(rows, r => r.Value == "committed-row");
    }

    [SkippableFact]
    public async Task Explicitly_rolled_back_insert_is_not_visible_afterward()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userId, tenantId) = await factory.CreateUserWithTenantAsync("TxRollback");

        await using (var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId))
        {
            session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Value = "rolled-back-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await session.DbContext.SaveChangesAsync();
            await session.RollbackAsync();
        }

        await using var verify = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
        var rows = await verify.DbContext.TenantIsolationTestRecords.ToListAsync();
        await verify.CommitAsync();

        Assert.DoesNotContain(rows, r => r.Value == "rolled-back-row");
    }

    [SkippableFact]
    public async Task Disposing_without_committing_after_an_exception_rolls_back_automatically()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userId, tenantId) = await factory.CreateUserWithTenantAsync("TxException");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
            session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Value = "never-committed-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await session.DbContext.SaveChangesAsync();

            // Simulate application code failing before it ever calls
            // CommitAsync — TenantRlsSession.DisposeAsync (via `await using`)
            // must roll back, and PostgreSQL must clear SET LOCAL on rollback
            // exactly as it does on commit.
            throw new InvalidOperationException("Simulated failure before commit.");
        });

        await using var verify = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, tenantId);
        var rows = await verify.DbContext.TenantIsolationTestRecords.ToListAsync();
        await verify.CommitAsync();

        Assert.DoesNotContain(rows, r => r.Value == "never-committed-row");
    }

    [SkippableFact]
    public async Task Pool_remains_healthy_and_correctly_isolated_after_a_rollback_and_an_exception()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("PoolHealthA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("PoolHealthB");

        // 1. Tenant A: insert + explicit rollback.
        await using (var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA))
        {
            session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Value = "a-abandoned-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await session.DbContext.SaveChangesAsync();
            await session.RollbackAsync();
        }

        // 2. Tenant A again: insert, then throw before commit (auto-rollback on dispose).
        try
        {
            await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
            session.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Value = "a-exception-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await session.DbContext.SaveChangesAsync();
            throw new InvalidOperationException("Simulated failure.");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // 3. Tenant B: on a connection that may well be the same physical one
        //    tenant A's two failed operations just used — must see nothing
        //    from tenant A, committed or not.
        await using (var sessionB = await ScopedTenantSession.OpenAsync(_fixture.Services, userB, tenantB))
        {
            sessionB.DbContext.TenantIsolationTestRecords.Add(new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                Value = "b-clean-row",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await sessionB.DbContext.SaveChangesAsync();
            var rowsForB = await sessionB.DbContext.TenantIsolationTestRecords.ToListAsync();
            await sessionB.CommitAsync();

            Assert.All(rowsForB, r => Assert.Equal(tenantB, r.TenantId));
            Assert.DoesNotContain(rowsForB, r => r.Value.StartsWith("a-", StringComparison.Ordinal));
        }

        // 4. Tenant A, one more time: confirm neither abandoned row exists,
        //    and the pool/connection is still perfectly usable for A.
        await using var finalCheck = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
        var rowsForA = await finalCheck.DbContext.TenantIsolationTestRecords.ToListAsync();
        await finalCheck.CommitAsync();

        Assert.All(rowsForA, r => Assert.Equal(tenantA, r.TenantId));
        Assert.DoesNotContain(rowsForA, r => r.Value is "a-abandoned-row" or "a-exception-row");
    }
}
