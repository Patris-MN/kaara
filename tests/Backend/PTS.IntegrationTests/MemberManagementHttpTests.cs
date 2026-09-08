using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PTS.Host.Http;
using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MemberManagementHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public MemberManagementHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_can_change_member_role_without_server_error()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("mgr-own"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(ownerClient, "ManageOrg");

        var memberEmail = Email("mgr-mem");
        await RegisterAndLoginAsync(_web.CreateClient(), memberEmail, "Member User");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email = memberEmail, role = "Member" });
        var memberClient = _web.CreateClient();
        var member = await LoginAsync(memberClient, memberEmail);
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenant.TenantId}/invitations/accept", null);

        Authorize(ownerClient, owner.AccessToken);
        var members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        var target = Assert.Single(members!, item => item.Email == memberEmail);

        var response = await ownerClient.PatchAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{target.MembershipId}/role",
            new { role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<MemberStatusResponse>();
        Assert.Equal("Admin", updated!.Role);
    }

    [SkippableFact]
    public async Task Member_cannot_change_roles_and_gets_forbidden_not_server_error()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("mgr-own2"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(ownerClient, "Org");

        var memberEmail = Email("mgr-mem2");
        await RegisterAndLoginAsync(_web.CreateClient(), memberEmail, "Member");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email = memberEmail, role = "Member" });
        var memberClient = _web.CreateClient();
        var member = await LoginAsync(memberClient, memberEmail);
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenant.TenantId}/invitations/accept", null);

        Authorize(ownerClient, owner.AccessToken);
        var members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        var target = Assert.Single(members!, item => item.Email == memberEmail);

        Authorize(memberClient, member.AccessToken);
        var response = await memberClient.PatchAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{target.MembershipId}/role",
            new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task Member_list_includes_tenant_scoped_task_aggregates()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("agg-own"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(ownerClient, "AggOrg");
        var workspace = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces",
            new { name = "Main" });
        workspace.EnsureSuccessStatusCode();
        var workspaceBody = await workspace.Content.ReadFromJsonAsync<WorkspaceResponse>();

        var memberEmail = Email("agg-mem");
        await RegisterAndLoginAsync(_web.CreateClient(), memberEmail, "Assignee");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email = memberEmail, role = "Member" });
        var memberClient = _web.CreateClient();
        var member = await LoginAsync(memberClient, memberEmail);
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenant.TenantId}/invitations/accept", null);

        Authorize(ownerClient, owner.AccessToken);
        var members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        var assignee = Assert.Single(members!, item => item.Email == memberEmail);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{assignee.MembershipId}/workspace-access/{workspaceBody!.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var project = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspaceBody.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Project"));
        project.EnsureSuccessStatusCode();
        var projectBody = await project.Content.ReadFromJsonAsync<ProjectResponse>();

        var openTask = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspaceBody.WorkspaceId}/projects/{projectBody!.ProjectId}/tasks",
            new CreateWorkTaskRequest("Open task", null, null, null, null, null, assignee.MembershipId));
        openTask.EnsureSuccessStatusCode();
        var closedTask = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspaceBody.WorkspaceId}/projects/{projectBody.ProjectId}/tasks",
            new CreateWorkTaskRequest("Done task", null, null, null, null, null, assignee.MembershipId));
        closedTask.EnsureSuccessStatusCode();
        var closedBody = await closedTask.Content.ReadFromJsonAsync<WorkTaskResponse>();
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspaceBody.WorkspaceId}/projects/{projectBody.ProjectId}/tasks/{closedBody!.TaskId}",
            new UpdateWorkTaskRequest("Done task", null, WorkTaskStatus.Closed.ToString(), "Normal", null, assignee.MembershipId)))
            .EnsureSuccessStatusCode();

        members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        var refreshed = Assert.Single(members!, item => item.Email == memberEmail);
        Assert.Equal(2, refreshed.TotalAssignedTaskCount);
        Assert.Equal(1, refreshed.ActiveTaskCount);
        Assert.Equal(1, refreshed.CompletedTaskCount);
        Assert.Equal(50.0m, refreshed.CompletionRate);
    }

    [SkippableFact]
    public async Task Owner_can_remove_member_and_final_owner_is_protected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("rm-own"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(ownerClient, "RemoveOrg");

        var memberEmail = Email("rm-mem");
        await RegisterAndLoginAsync(_web.CreateClient(), memberEmail, "Member");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email = memberEmail, role = "Member" });
        var memberClient = _web.CreateClient();
        var member = await LoginAsync(memberClient, memberEmail);
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenant.TenantId}/invitations/accept", null);

        Authorize(ownerClient, owner.AccessToken);
        var members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        var target = Assert.Single(members!, item => item.Email == memberEmail);

        var remove = await ownerClient.PostAsync(
            $"/tenants/{tenant.TenantId}/members/{target.MembershipId}/remove",
            null);
        remove.EnsureSuccessStatusCode();

        members = await ownerClient.GetFromJsonAsync<List<TenantMemberResponse>>(
            $"/tenants/{tenant.TenantId}/members");
        Assert.Contains(members!, item => item.Email == memberEmail && item.Status == "Removed");

        var ownerMembership = Assert.Single(members!, item => item.Email == owner.Email);
        var selfRemove = await ownerClient.PostAsync(
            $"/tenants/{tenant.TenantId}/members/{ownerMembership.MembershipId}/remove",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, selfRemove.StatusCode);
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName)))
            .EnsureSuccessStatusCode();
        return await LoginAsync(client, email);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<TenantResponse> CreateTenantOkAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"t-{Guid.NewGuid():N}"[..12]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }
}
