using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskPriorityDeadlineHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskPriorityDeadlineHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Priority_and_deadline_persist_through_create_update_and_reload()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, $"prio-own-{Guid.NewGuid():N}@example.test", "Owner");
        var memberEmail = $"prio-mem-{Guid.NewGuid():N}@example.test";
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenantResponse = await ownerClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Priority Org", $"prio-{Guid.NewGuid():N}"[..20]));
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail))).EnsureSuccessStatusCode();
        (await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Priority Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Priority Project");
        var deadline = new DateOnly(2026, 9, 15);

        foreach (var priority in new[] { "Low", "Normal", "High", "Urgent" })
        {
            var created = await CreateTaskAsync(
                ownerClient,
                tenant.TenantId,
                workspace.WorkspaceId,
                project.ProjectId,
                $"{priority} task",
                priority,
                deadline);
            var reloaded = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId));
            Assert.Equal(priority, created.Priority);
            Assert.Equal(priority, reloaded!.Priority);
            Assert.Equal(deadline, reloaded.DueDate);
        }

        var noDeadline = await CreateTaskAsync(
            ownerClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            "No deadline",
            "Normal",
            null);
        var noDeadlineReload = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId));
        Assert.Null(noDeadline.DueDate);
        Assert.Null(noDeadlineReload!.DueDate);

        var legacyMedium = await ownerClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest("Legacy medium", null, "Todo", "Medium", null));
        legacyMedium.EnsureSuccessStatusCode();
        var mapped = await legacyMedium.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal("Normal", mapped!.Priority);

        var invalid = await ownerClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest("Bad priority", null, "Todo", "Critical", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_task_priority", await ReadErrorAsync(invalid));

        var updated = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId),
            new UpdateWorkTaskRequest("No deadline", null, "InProgress", "Urgent", deadline));
        updated.EnsureSuccessStatusCode();
        var afterUpdate = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId));
        Assert.Equal("Urgent", afterUpdate!.Priority);
        Assert.Equal(deadline, afterUpdate.DueDate);

        var cleared = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId),
            new UpdateWorkTaskRequest("No deadline", null, "InProgress", "High", null));
        cleared.EnsureSuccessStatusCode();
        var afterClear = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId));
        Assert.Equal("High", afterClear!.Priority);
        Assert.Null(afterClear.DueDate);

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var viewUpdate = await memberClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId),
            new UpdateWorkTaskRequest("Hijack", null, "Done", "Urgent", deadline));
        Assert.Equal(HttpStatusCode.Forbidden, viewUpdate.StatusCode);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var editUpdate = await memberClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, noDeadline.TaskId),
            new UpdateWorkTaskRequest("Member edited", null, "Todo", "Low", deadline));
        Assert.Equal(HttpStatusCode.Forbidden, editUpdate.StatusCode);
        Assert.Equal("task_field_forbidden", await ReadErrorAsync(editUpdate));
    }

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId, Guid? taskId = null)
        => taskId is { } id
            ? $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{id}"
            : $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks";

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

    private static async Task<WorkTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title,
        string priority,
        DateOnly? dueDate)
    {
        var response = await client.PostAsJsonAsync(
            TaskPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title, null, "Todo", priority, dueDate));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkTaskResponse>())!;
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
