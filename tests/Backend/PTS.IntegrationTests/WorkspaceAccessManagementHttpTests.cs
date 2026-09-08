using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WorkspaceAccessManagementHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public WorkspaceAccessManagementHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_can_batch_grant_and_change_member_workspace_access()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("wa815-own"), "Owner");
        var memberEmail = Email("wa815-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Sara");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantOkAsync(ownerClient, "Patris");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var development = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Development");
        var marketing = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Marketing");
        var finance = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Finance");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        Assert.False(memberRecord.HasImplicitWorkspaceAccess);

        var batchGrant = await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access",
            new ReplaceWorkspaceAccessRequest([
                new WorkspaceAccessGrantRequest(development.WorkspaceId, "Edit"),
                new WorkspaceAccessGrantRequest(marketing.WorkspaceId, "View"),
                new WorkspaceAccessGrantRequest(finance.WorkspaceId, null),
            ]));
        batchGrant.EnsureSuccessStatusCode();

        var listed = await ownerClient.GetFromJsonAsync<WorkspaceAccessResponse[]>(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access");
        Assert.Equal(2, listed!.Length);
        Assert.Contains(listed, item => item.WorkspaceId == development.WorkspaceId && item.AccessLevel == "Edit");
        Assert.Contains(listed, item => item.WorkspaceId == marketing.WorkspaceId && item.AccessLevel == "View");

        var memberWorkspaces = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Equal(2, memberWorkspaces!.Length);

        var batchChange = await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access",
            new ReplaceWorkspaceAccessRequest([
                new WorkspaceAccessGrantRequest(development.WorkspaceId, null),
                new WorkspaceAccessGrantRequest(marketing.WorkspaceId, "Edit"),
            ]));
        batchChange.EnsureSuccessStatusCode();

        var afterRemoval = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        var single = Assert.Single(afterRemoval!);
        Assert.Equal(marketing.WorkspaceId, single.WorkspaceId);
        Assert.Equal("Edit", single.AccessLevel);

        var editCreate = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{marketing.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Campaign"));
        editCreate.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Admin_can_manage_ordinary_member_access_and_member_cannot_self_grant()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("wa815-adm-own"), "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, Email("wa815-adm"), "Admin");
        var memberEmail = Email("wa815-adm-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Ali");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantOkAsync(ownerClient, "AdminOrg");
        await _postgres.Services.GetRequiredService<TestDataFactory>()
            .CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Ops");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);

        (await adminClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var selfGrant = await memberClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"));
        Assert.Equal(HttpStatusCode.Forbidden, selfGrant.StatusCode);
    }

    [SkippableFact]
    public async Task Workspace_member_access_list_and_grant_from_workspace_perspective()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("wa815-ws-own"), "Owner");
        var memberEmail = Email("wa815-ws-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Lana");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantOkAsync(ownerClient, "WorkspaceOrg");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Development");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId && item.Role == "Member");
        var ownerRecord = Assert.Single(members!, item => item.UserId == owner.UserId);

        var list = await ownerClient.GetFromJsonAsync<WorkspaceMemberAccessResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/member-access");
        Assert.NotNull(list);
        Assert.Contains(list!, item => item.MembershipId == ownerRecord.MembershipId && item.EffectiveAccess == "Full");
        Assert.Contains(list!, item => item.MembershipId == memberRecord.MembershipId && item.EffectiveAccess == "None");

        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/member-access",
            new GrantWorkspaceMemberAccessRequest([memberRecord.MembershipId], "Edit"))).EnsureSuccessStatusCode();

        var afterGrant = await ownerClient.GetFromJsonAsync<WorkspaceMemberAccessResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/member-access");
        var granted = Assert.Single(afterGrant!, item => item.MembershipId == memberRecord.MembershipId);
        Assert.Equal("Edit", granted.EffectiveAccess);

        var memberListed = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Contains(memberListed!, item => item.WorkspaceId == workspace.WorkspaceId);
    }

    [SkippableFact]
    public async Task Cross_tenant_workspace_pairing_is_rejected_and_suspended_member_has_no_effective_access()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("wa815-x-own"), "Owner");
        var stranger = await RegisterAndLoginAsync(strangerClient, Email("wa815-x-str"), "Stranger");
        var memberEmail = Email("wa815-x-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Sam");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantOkAsync(ownerClient, "Patris");
        var foreignTenant = await CreateTenantOkAsync(strangerClient, "FastPay");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var localWorkspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Local");
        var foreignWorkspace = await CreateWorkspaceAsync(strangerClient, foreignTenant.TenantId, "Foreign");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ownerClient.PutAsJsonAsync(
                $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{foreignWorkspace.WorkspaceId}",
                new SetWorkspaceAccessRequest("View"))).StatusCode);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{localWorkspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        await _postgres.Services.GetRequiredService<TestDataFactory>()
            .SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Suspended);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces")).StatusCode);
    }

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<TenantResponse> CreateTenantOkAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"org-{Guid.NewGuid():N}"[..20]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }

    private static async Task<WorkspaceResponse> CreateWorkspaceAsync(HttpClient client, Guid tenantId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/tenants/{tenantId}/workspaces",
            new CreateWorkspaceRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceResponse>())!;
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";
}
