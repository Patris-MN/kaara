using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class WorkspaceResourceAuthorizationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public WorkspaceResourceAuthorizationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_admin_and_member_workspace_access_follow_the_resource_authorization_matrix()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var invitedClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, $"wa-own-{Guid.NewGuid():N}@example.test", "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, $"wa-adm-{Guid.NewGuid():N}@example.test", "Admin");
        var memberEmail = $"wa-mem-{Guid.NewGuid():N}@example.test";
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        var invitedEmail = $"wa-inv-{Guid.NewGuid():N}@example.test";
        var invited = await RegisterAndLoginAsync(invitedClient, invitedEmail, "Invited");
        var stranger = await RegisterAndLoginAsync(strangerClient, $"wa-str-{Guid.NewGuid():N}@example.test", "Stranger");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);
        Authorize(memberClient, member.AccessToken);
        Authorize(invitedClient, invited.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);

        var tenantResponse = await ownerClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Authz Org", $"authz-{Guid.NewGuid():N}"[..20]));
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        await factory.CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);

        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail))).EnsureSuccessStatusCode();
        (await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();

        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(invitedEmail))).EnsureSuccessStatusCode();

        var leopard = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Leopard");
        var tiger = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Tiger");
        var leopardProject = await CreateProjectAsync(ownerClient, tenant.TenantId, leopard.WorkspaceId, "Spots");

        var ownerListed = await ownerClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Equal(2, ownerListed!.Length);
        Assert.Contains(ownerListed, workspace => workspace.WorkspaceId == leopard.WorkspaceId);
        Assert.Contains(ownerListed, workspace => workspace.WorkspaceId == tiger.WorkspaceId);

        var adminListed = await adminClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Equal(2, adminListed!.Length);

        var memberNone = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Empty(memberNone!);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                $"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}/projects")).StatusCode);

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        var invitedRecord = Assert.Single(members!, item => item.UserId == invited.UserId);

        var memberGrantDenied = await memberClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{leopard.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"));
        Assert.Equal(HttpStatusCode.Forbidden, memberGrantDenied.StatusCode);
        Assert.Equal("workspace_access_manage_forbidden", await ReadErrorAsync(memberGrantDenied));

        var invitedAccessDenied = await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{invitedRecord.MembershipId}/workspace-access/{leopard.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"));
        Assert.Equal(HttpStatusCode.BadRequest, invitedAccessDenied.StatusCode);
        Assert.Equal("workspace_access_requires_active_member", await ReadErrorAsync(invitedAccessDenied));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await invitedClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces")).StatusCode);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{leopard.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var memberViewList = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        var viewed = Assert.Single(memberViewList!);
        Assert.Equal(leopard.WorkspaceId, viewed.WorkspaceId);
        Assert.Equal("View", viewed.AccessLevel);

        var memberProjects = await memberClient.GetFromJsonAsync<ProjectResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}/projects");
        Assert.Contains(memberProjects!, project => project.ProjectId == leopardProject.ProjectId);

        var viewCreate = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}/projects",
            new CreateProjectRequest("Nope"));
        Assert.Equal(HttpStatusCode.Forbidden, viewCreate.StatusCode);
        Assert.Equal("workspace_edit_forbidden", await ReadErrorAsync(viewCreate));

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces/{tiger.WorkspaceId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                $"/tenants/{tenant.TenantId}/workspaces/{tiger.WorkspaceId}/projects")).StatusCode);

        (await adminClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{leopard.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var editCreate = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}/projects",
            new CreateProjectRequest("Allowed"));
        editCreate.EnsureSuccessStatusCode();

        (await ownerClient.DeleteAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{leopard.WorkspaceId}"))
            .EnsureSuccessStatusCode();

        var afterRemoval = await memberClient.GetFromJsonAsync<WorkspaceResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces");
        Assert.Empty(afterRemoval!);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                $"/tenants/{tenant.TenantId}/workspaces/{leopard.WorkspaceId}/projects")).StatusCode);

        var otherTenant = await strangerClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Other Org", $"other-{Guid.NewGuid():N}"[..20]));
        otherTenant.EnsureSuccessStatusCode();
        var foreign = await otherTenant.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(foreign);
        var foreignWorkspace = await CreateWorkspaceAsync(strangerClient, foreign.TenantId, "Foreign");

        var crossTenantGrant = await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{foreignWorkspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"));
        Assert.Equal(HttpStatusCode.NotFound, crossTenantGrant.StatusCode);

        await factory.SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Suspended);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.GetAsync($"/tenants/{tenant.TenantId}/workspaces")).StatusCode);
    }

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<WorkspaceResponse> CreateWorkspaceAsync(HttpClient client, Guid tenantId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/tenants/{tenantId}/workspaces",
            new CreateWorkspaceRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceResponse>())!;
    }

    private static async Task<ProjectResponse> CreateProjectAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/tenants/{tenantId}/workspaces/{workspaceId}/projects",
            new CreateProjectRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
