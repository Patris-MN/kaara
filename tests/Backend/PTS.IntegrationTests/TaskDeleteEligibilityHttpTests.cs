using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskDeleteEligibilityHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskDeleteEligibilityHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Edit_member_can_delete_unseen_task()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("td-own"), "Owner");
        var memberEmail = Email("td-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Delete Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Alpha");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Delete me");

        (await memberClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId))).EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Creator_cannot_delete_after_another_member_views_task()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("td-view-own"), "Owner");
        var memberEmail = Email("td-view-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "View Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Beta");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Seen task");

        (await memberClient.PostAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId, "/seen"),
            null)).EnsureSuccessStatusCode();

        var ownerRead = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.NotNull(ownerRead);
        Assert.False(ownerRead!.Capabilities!.CanDelete);
        Assert.Equal("task_seen_by_another_member", ownerRead.Capabilities.DeleteBlockedReason);

        var deleteAttempt = await ownerClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.Equal(HttpStatusCode.Conflict, deleteAttempt.StatusCode);
        Assert.Equal("task_already_seen_cannot_delete", await ReadErrorAsync(deleteAttempt));
    }

    [SkippableFact]
    public async Task Creator_cannot_delete_after_non_creator_comment()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("td-com-own"), "Owner");
        var memberEmail = Email("td-com-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Comment Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Gamma");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Commented task");
        (await memberClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId, "/comments"),
            new CreateWorkTaskCommentRequest("Hello"))).EnsureSuccessStatusCode();

        var deleteAttempt = await ownerClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.Equal(HttpStatusCode.Conflict, deleteAttempt.StatusCode);
        Assert.Equal("task_already_seen_cannot_delete", await ReadErrorAsync(deleteAttempt));
    }

    [SkippableFact]
    public async Task Mark_seen_is_idempotent_and_creator_view_does_not_block_delete()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("td-idem"), "Owner");
        Authorize(ownerClient, owner.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Idempotent Org");
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Delta");
        var task = await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Creator only");

        var seenPath = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId, "/seen");
        (await ownerClient.PostAsync(seenPath, null)).EnsureSuccessStatusCode();
        (await ownerClient.PostAsync(seenPath, null)).EnsureSuccessStatusCode();

        var read = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId));
        Assert.True(read!.Capabilities!.CanDelete);

        (await ownerClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, task.TaskId))).EnsureSuccessStatusCode();
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
            new CreateTenantRequest(name, $"td-{Guid.NewGuid():N}"[..20]));
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

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId, Guid? taskId = null, string suffix = "")
        => taskId is { } id
            ? $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{id}{suffix}"
            : $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks{suffix}";

    private static async Task<WorkTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title)
    {
        var response = await client.PostAsJsonAsync(
            TaskPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkTaskResponse>())!;
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
