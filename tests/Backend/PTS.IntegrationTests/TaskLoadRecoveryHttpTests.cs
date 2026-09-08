using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskLoadRecoveryHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskLoadRecoveryHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Task_list_returns_200_with_existing_tasks()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, Email("tl-own"), "Owner");
        Authorize(client, owner.AccessToken);

        var tenant = await CreateTenantAsync(client, "List Org");
        var workspace = await CreateWorkspaceAsync(client, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(client, tenant.TenantId, workspace.WorkspaceId, "List Project");
        var created = await CreateTaskAsync(client, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Visible task");

        var listResponse = await client.GetAsync(TasksPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        listResponse.EnsureSuccessStatusCode();
        var tasks = await listResponse.Content.ReadFromJsonAsync<WorkTaskResponse[]>();
        var listed = Assert.Single(tasks!);
        Assert.Equal(created.TaskId, listed.TaskId);
        Assert.Equal("Visible task", listed.Title);
    }

    [SkippableFact]
    public async Task Task_list_does_not_mark_tasks_seen_or_block_creator_delete()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("tl-list-own"), "Owner");
        var memberEmail = Email("tl-list-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "List Seen Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Only One");
        await GrantEditAccessAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, member.UserId);

        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "List only");

        var listResponse = await memberClient.GetAsync(TasksPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        listResponse.EnsureSuccessStatusCode();
        var listed = await listResponse.Content.ReadFromJsonAsync<WorkTaskResponse[]>();
        Assert.Single(listed!);

        var ownerRead = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.True(ownerRead!.Capabilities!.CanDelete);

        (await ownerClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId))).EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Task_detail_seen_endpoint_records_engagement_and_blocks_delete()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("tl-seen-own"), "Owner");
        var memberEmail = Email("tl-seen-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Seen Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Detail");
        await GrantEditAccessAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, member.UserId);

        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Detail task");

        (await memberClient.PostAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId, "/seen"),
            null)).EnsureSuccessStatusCode();

        var ownerRead = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.False(ownerRead!.Capabilities!.CanDelete);
        Assert.Equal("task_seen_by_another_member", ownerRead.Capabilities.DeleteBlockedReason);
    }

    private static async Task GrantEditAccessAsync(
        HttpClient ownerClient,
        Guid tenantId,
        Guid workspaceId,
        Guid memberUserId)
    {
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == memberUserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string AccessToken, Guid UserId)> RegisterAndLoginAsync(
        HttpClient client,
        string email,
        string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new { email, password = "Password1!", displayName }))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password = "Password1!" });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("accessToken").GetString()!, doc.RootElement.GetProperty("userId").GetGuid());
    }

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"tl-{Guid.NewGuid():N}"[..20]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }

    private static async Task<WorkspaceResponse> CreateWorkspaceAsync(HttpClient client, Guid tenantId, string name)
    {
        var response = await client.PostAsJsonAsync($"/tenants/{tenantId}/workspaces", new { name });
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
            TestProjectFactory.CreateRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private static string TasksPath(Guid tenantId, Guid workspaceId, Guid projectId)
        => $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks";

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId, Guid taskId, string suffix = "")
        => $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{taskId}{suffix}";

    private static async Task<WorkTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title)
    {
        var response = await client.PostAsJsonAsync(
            TasksPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkTaskResponse>())!;
    }
}
