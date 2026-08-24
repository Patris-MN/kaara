using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence.Testing;

namespace PTS.IntegrationTests;

/// <summary>
/// Step 10 — RLS isolation tests. Every query below deliberately omits any
/// <c>WHERE tenant_id = ...</c> predicate. Isolation must come from
/// PostgreSQL Row-Level Security alone, or these tests fail.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantIsolationRlsTests
{
    private readonly PostgresFixture _fixture;

    public TenantIsolationRlsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Tenant_A_sees_only_its_own_record_with_no_application_filter()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("RlsTenantA");
        await SeedIsolationRecordAsync(userA, tenantA, "record-A");

        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
        var records = await session.DbContext.TenantIsolationTestRecords.ToListAsync();
        await session.CommitAsync();

        Assert.Single(records);
        Assert.Equal("record-A", records[0].Value);
        Assert.Equal(tenantA, records[0].TenantId);
    }

    [SkippableFact]
    public async Task Tenant_B_sees_only_its_own_record_with_no_application_filter()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("RlsTenantB");
        await SeedIsolationRecordAsync(userB, tenantB, "record-B");

        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userB, tenantB);
        var records = await session.DbContext.TenantIsolationTestRecords.ToListAsync();
        await session.CommitAsync();

        Assert.Single(records);
        Assert.Equal("record-B", records[0].Value);
        Assert.Equal(tenantB, records[0].TenantId);
    }

    [SkippableFact]
    public async Task Tenant_A_cannot_see_tenant_B_when_both_have_data_and_no_filter_is_applied()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("CrossA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("CrossB");
        await SeedIsolationRecordAsync(userA, tenantA, "cross-record-A");
        await SeedIsolationRecordAsync(userB, tenantB, "cross-record-B");

        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
        var records = await session.DbContext.TenantIsolationTestRecords.ToListAsync();
        await session.CommitAsync();

        Assert.Single(records);
        Assert.Equal("cross-record-A", records[0].Value);
        Assert.DoesNotContain(records, r => r.TenantId == tenantB);
    }

    [SkippableFact]
    public async Task Tenant_B_cannot_see_tenant_A_when_both_have_data_and_no_filter_is_applied()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("ReverseA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("ReverseB");
        await SeedIsolationRecordAsync(userA, tenantA, "reverse-record-A");
        await SeedIsolationRecordAsync(userB, tenantB, "reverse-record-B");

        await using var session = await ScopedTenantSession.OpenAsync(_fixture.Services, userB, tenantB);
        var records = await session.DbContext.TenantIsolationTestRecords.ToListAsync();
        await session.CommitAsync();

        Assert.Single(records);
        Assert.Equal("reverse-record-B", records[0].Value);
        Assert.DoesNotContain(records, r => r.TenantId == tenantA);
    }

    [SkippableFact]
    public async Task Query_with_no_tenant_predicate_at_all_is_still_isolated_by_RLS_not_application_code()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("NoFilterA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("NoFilterB");
        await SeedIsolationRecordAsync(userA, tenantA, "no-filter-A");
        await SeedIsolationRecordAsync(userB, tenantB, "no-filter-B");

        // This is the critical assertion from Step 10 Test 5: the LINQ query
        // below has ZERO tenant predicate anywhere in application code. If
        // this test passes, isolation is coming from PostgreSQL RLS alone.
        await using var sessionA = await ScopedTenantSession.OpenAsync(_fixture.Services, userA, tenantA);
        var allVisibleToA = await sessionA.DbContext.TenantIsolationTestRecords.ToListAsync();
        await sessionA.CommitAsync();

        Assert.All(allVisibleToA, r => Assert.Equal(tenantA, r.TenantId));
        Assert.DoesNotContain(allVisibleToA, r => r.TenantId == tenantB);
    }

    private async Task SeedIsolationRecordAsync(Guid userId, Guid tenantId, string value)
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
}
