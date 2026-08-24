using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SecurityTests
{
    private readonly PostgresFixture _fixture;

    public SecurityTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task User_requesting_a_tenant_they_have_no_membership_for_is_denied()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, _) = await factory.CreateUserWithTenantAsync("SpoofUserA");
        var tenantB = await factory.CreateTenantAsync("SpoofTenantB");

        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(userA);
        var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();

        var ex = await Assert.ThrowsAsync<TenantAccessDeniedException>(
            () => sessions.OpenAsync(tenantB));

        Assert.Equal(userA, ex.UserId);
        Assert.Equal(tenantB, ex.RequestedTenantId);
    }

    [SkippableFact]
    public async Task Resolver_denies_access_without_creating_or_relying_on_any_ambient_state()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var userId = await factory.CreateUserAsync("NoMembershipUser");
        var tenantId = await factory.CreateTenantAsync("NoMembershipTenant");

        using var scope = _fixture.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITenantContextResolver>();
        var result = await resolver.ResolveAsync(userId, tenantId);

        Assert.False(result.Success);
        Assert.Null(result.TenantId);
        Assert.NotNull(result.FailureReason);
    }

    [SkippableFact]
    public async Task A_suspended_membership_does_not_grant_tenant_access()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var tenantId = await factory.CreateTenantAsync("SuspendedTenant");
        var userId = await factory.CreateUserAsync("SuspendedUser");
        await factory.CreateMembershipAsync(userId, tenantId, MembershipStatus.Suspended);

        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(userId);
        var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => sessions.OpenAsync(tenantId));
    }

    [SkippableFact]
    public async Task An_invited_membership_does_not_grant_tenant_access()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var tenantId = await factory.CreateTenantAsync("InvitedTenant");
        var userId = await factory.CreateUserAsync("InvitedUser");
        await factory.CreateMembershipAsync(userId, tenantId, MembershipStatus.Invited);

        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(userId);
        var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => sessions.OpenAsync(tenantId));
    }

    [SkippableFact]
    public async Task Unauthenticated_caller_cannot_open_a_tenant_session()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var tenantId = await factory.CreateTenantAsync("UnauthTenant");

        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();

        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => sessions.OpenAsync(tenantId));
    }

    [SkippableFact]
    public async Task Caller_cannot_become_another_user_by_supplying_their_id()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userA, tenantA) = await factory.CreateUserWithTenantAsync("CannotSpoofA");
        var (userB, tenantB) = await factory.CreateUserWithTenantAsync("CannotSpoofB");

        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(userA);
        var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();

        await Assert.ThrowsAsync<TenantAccessDeniedException>(() => sessions.OpenAsync(tenantB));

        await using var sessionA = await sessions.OpenAsync(tenantA);
        var visible = await sessionA.DbContext.TenantIsolationTestRecords.ToListAsync();
        Assert.DoesNotContain(visible, r => r.TenantId == tenantB);
        await sessionA.RollbackAsync();
    }

    [SkippableFact]
    public async Task Requested_tenant_id_is_only_ever_a_hint_never_trusted_without_membership_proof()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var factory = _fixture.Services.GetRequiredService<TestDataFactory>();
        var (userId, ownTenantId) = await factory.CreateUserWithTenantAsync("HintUser");
        var someoneElsesTenantId = await factory.CreateTenantAsync("HintOtherTenant");

        using (var deniedScope = _fixture.Services.CreateScope())
        {
            deniedScope.ServiceProvider.GetRequiredService<TestCurrentUser>().AuthenticateAs(userId);
            var sessions = deniedScope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();
            await Assert.ThrowsAsync<TenantAccessDeniedException>(
                () => sessions.OpenAsync(someoneElsesTenantId));
        }

        await using var legitimate = await ScopedTenantSession.OpenAsync(_fixture.Services, userId, ownTenantId);
        Assert.Equal(ownTenantId, legitimate.TenantId);
        await legitimate.CommitAsync();
    }
}
