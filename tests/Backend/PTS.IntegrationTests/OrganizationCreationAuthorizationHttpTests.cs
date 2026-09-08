using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class OrganizationCreationAuthorizationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public OrganizationCreationAuthorizationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Fresh_independent_user_can_create_first_organization_and_becomes_owner()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, $"fresh-{Guid.NewGuid():N}@example.test", "Fresh User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var capabilities = await client.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.True(capabilities!.CanCreateOrganization);
        Assert.Equal(0, capabilities.CurrentOrganizationCount);
        Assert.Equal(0, capabilities.ActiveMembershipCount);
        Assert.Equal(0, capabilities.PendingInvitationCount);

        var slug = $"fresh-{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync("/tenants", new CreateTenantRequest("Fresh Org", slug));
        created.EnsureSuccessStatusCode();

        var memberships = await client.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        var membership = Assert.Single(memberships!);
        Assert.Equal("Owner", membership.Role);
        Assert.Equal("Active", membership.Status);
    }

    [SkippableFact]
    public async Task Tenant_member_cannot_create_organization_via_api()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"own-{Guid.NewGuid():N}@example.test", "Owner");
        var member = await RegisterAndLoginAsync(memberClient, $"mem-{Guid.NewGuid():N}@example.test", "Member");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Member Org", $"memorg-{Guid.NewGuid():N}"[..20]);
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(member.Email))).EnsureSuccessStatusCode();
        (await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();

        var capabilities = await memberClient.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.False(capabilities!.CanCreateOrganization);
        Assert.Equal(0, capabilities.CurrentOrganizationCount);

        var denied = await memberClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Hijack Org", $"hij-{Guid.NewGuid():N}"[..20]));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task Tenant_admin_without_owner_membership_cannot_create_organization()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"admown-{Guid.NewGuid():N}@example.test", "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, $"adm-{Guid.NewGuid():N}@example.test", "Admin");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Admin Org", $"admorg-{Guid.NewGuid():N}"[..20]);
        await factory.CreateMembershipAsync(admin.UserId, tenant.TenantId, MembershipStatus.Active, MembershipRole.Admin);

        var capabilities = await adminClient.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.False(capabilities!.CanCreateOrganization);

        var denied = await adminClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Admin Hijack", $"ahj-{Guid.NewGuid():N}"[..20]));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task Owner_can_create_additional_organizations_under_development_policy()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, $"multi-{Guid.NewGuid():N}@example.test", "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);

        var first = await CreateTenantAsync(client, "Org One", $"one-{Guid.NewGuid():N}"[..20]);
        var capabilities = await client.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.True(capabilities!.CanCreateOrganization);
        Assert.Equal(1, capabilities.CurrentOrganizationCount);
        Assert.Equal(-1, capabilities.OrganizationLimit);

        var second = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Org Two", $"two-{Guid.NewGuid():N}"[..20]));
        second.EnsureSuccessStatusCode();

        var memberships = await client.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        Assert.Equal(2, memberships!.Length);
        Assert.All(memberships, membership => Assert.Equal("Owner", membership.Role));
        Assert.Contains(memberships, membership => membership.TenantId == first.TenantId);
    }

    [SkippableFact]
    public async Task User_with_pending_invitation_cannot_create_organization_before_acceptance()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var inviteeClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"pown-{Guid.NewGuid():N}@example.test", "Owner");
        var inviteeEmail = $"pinv-{Guid.NewGuid():N}@example.test";
        var invitee = await RegisterAndLoginAsync(inviteeClient, inviteeEmail, "Invitee");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        inviteeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invitee.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Pending Org", $"porg-{Guid.NewGuid():N}"[..20]);
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(inviteeEmail))).EnsureSuccessStatusCode();

        var capabilities = await inviteeClient.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.False(capabilities!.CanCreateOrganization);
        Assert.Equal(1, capabilities.PendingInvitationCount);

        var denied = await inviteeClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Skip Invite", $"ski-{Guid.NewGuid():N}"[..20]));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task Active_member_cannot_create_workspace()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"wsown-{Guid.NewGuid():N}@example.test", "Owner");
        var member = await RegisterAndLoginAsync(memberClient, $"wsmem-{Guid.NewGuid():N}@example.test", "Member");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Workspace Org", $"wsorg-{Guid.NewGuid():N}"[..20]);
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(member.Email))).EnsureSuccessStatusCode();
        (await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();

        var denied = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces",
            new CreateWorkspaceRequest("Member Workspace"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [SkippableFact]
    public async Task Owner_of_org_A_cannot_create_workspace_in_org_B_where_same_user_is_member_only()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var multiOrgUserClient = _web.CreateClient();
        var orgBOwnerClient = _web.CreateClient();
        var multiOrgUser = await RegisterAndLoginAsync(
            multiOrgUserClient,
            $"multiiso-{Guid.NewGuid():N}@example.test",
            "Multi Org User");
        var orgBOwner = await RegisterAndLoginAsync(
            orgBOwnerClient,
            $"bown-{Guid.NewGuid():N}@example.test",
            "Org B Owner");
        multiOrgUserClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", multiOrgUser.AccessToken);
        orgBOwnerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", orgBOwner.AccessToken);

        var orgA = await CreateTenantAsync(multiOrgUserClient, "Org A", $"orga-{Guid.NewGuid():N}"[..20]);
        var orgB = await CreateTenantAsync(orgBOwnerClient, "Org B", $"orgb-{Guid.NewGuid():N}"[..20]);
        await factory.CreateMembershipAsync(
            multiOrgUser.UserId,
            orgB.TenantId,
            MembershipStatus.Active,
            MembershipRole.Member);

        (await multiOrgUserClient.PostAsJsonAsync(
            $"/tenants/{orgA.TenantId}/workspaces",
            new CreateWorkspaceRequest("Allowed In A"))).EnsureSuccessStatusCode();

        var deniedInB = await multiOrgUserClient.PostAsJsonAsync(
            $"/tenants/{orgB.TenantId}/workspaces",
            new CreateWorkspaceRequest("Blocked In B"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedInB.StatusCode);
    }

    [SkippableFact]
    public async Task Interrupted_invitation_recovery_accepts_pending_invitation_without_raw_token()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var inviteeClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"recown-{Guid.NewGuid():N}@example.test", "Owner");
        var inviteeEmail = $"recinv-{Guid.NewGuid():N}@example.test";
        var invitee = await RegisterAndLoginAsync(inviteeClient, inviteeEmail, "Invitee");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        inviteeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invitee.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Recovery Org", $"recorg-{Guid.NewGuid():N}"[..20]);
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(inviteeEmail))).EnsureSuccessStatusCode();

        var pending = await inviteeClient.GetFromJsonAsync<TenantMembershipResponse[]>("/invitations");
        Assert.NotNull(pending);
        Assert.Contains(pending!, item => item.TenantId == tenant.TenantId);

        (await inviteeClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();

        var memberships = await inviteeClient.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        var membership = Assert.Single(memberships!);
        Assert.Equal(tenant.TenantId, membership.TenantId);
        Assert.Equal("Member", membership.Role);
        Assert.Equal("Active", membership.Status);

        var capabilities = await inviteeClient.GetFromJsonAsync<AccountCapabilitiesResponse>("/account/capabilities");
        Assert.NotNull(capabilities);
        Assert.False(capabilities!.CanCreateOrganization);
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, string name, string slug)
    {
        var response = await client.PostAsJsonAsync("/tenants", new CreateTenantRequest(name, slug));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }
}
