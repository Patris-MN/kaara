using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskResourceAuthorizationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskResourceAuthorizationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Task_access_inherits_workspace_authorization_without_a_new_jwt()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, $"tk-own-{Guid.NewGuid():N}@example.test", "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, $"tk-adm-{Guid.NewGuid():N}@example.test", "Admin");
        var memberEmail = $"tk-mem-{Guid.NewGuid():N}@example.test";
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        var stranger = await RegisterAndLoginAsync(strangerClient, $"tk-str-{Guid.NewGuid():N}@example.test", "Stranger");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);
        Authorize(memberClient, member.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);

        var tenantResponse = await ownerClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Task Org", $"tasko-{Guid.NewGuid():N}"[..20]));
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

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Leopard");
        var otherWorkspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Tiger");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Spots");
        var otherProject = await CreateProjectAsync(ownerClient, tenant.TenantId, otherWorkspace.WorkspaceId, "Stripes");

        var ownerCreated = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Owner task");
        var adminCreated = await CreateTaskAsync(
            adminClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Admin task");

        var ownerListed = await ownerClient.GetFromJsonAsync<WorkTaskResponse[]>(TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Contains(ownerListed!, task => task.TaskId == ownerCreated.TaskId);
        Assert.Contains(ownerListed!, task => task.TaskId == adminCreated.TaskId);

        var ownerRead = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerCreated.TaskId));
        Assert.Equal("Owner task", ownerRead!.Title);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ownerClient.GetAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, Guid.NewGuid()))).StatusCode);

        var ownerUpdated = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerCreated.TaskId),
            new UpdateWorkTaskRequest("Owner task updated", "notes", "InProgress", "High", new DateOnly(2026, 9, 1)));
        ownerUpdated.EnsureSuccessStatusCode();

        var noneList = await memberClient.GetAsync(TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(HttpStatusCode.NotFound, noneList.StatusCode);

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var viewList = await memberClient.GetFromJsonAsync<WorkTaskResponse[]>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Contains(viewList!, task => task.TaskId == ownerCreated.TaskId);

        var viewRead = await memberClient.GetAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerCreated.TaskId));
        viewRead.EnsureSuccessStatusCode();

        var viewCreate = await memberClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest("Nope"));
        Assert.Equal(HttpStatusCode.Forbidden, viewCreate.StatusCode);
        Assert.Equal("task_edit_forbidden", await ReadErrorAsync(viewCreate));

        var viewUpdate = await memberClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerCreated.TaskId),
            new UpdateWorkTaskRequest("Hijack", null, "Done", "Low", null));
        Assert.Equal(HttpStatusCode.Forbidden, viewUpdate.StatusCode);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var editCreated = await CreateTaskAsync(
            memberClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Member task");
        var editUpdate = await memberClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, editCreated.TaskId),
            new UpdateWorkTaskRequest("Member task edited", "desc", "Done", "Low", null));
        editUpdate.EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, otherProject.ProjectId, editCreated.TaskId))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                TaskPath(tenant.TenantId, otherWorkspace.WorkspaceId, project.ProjectId, editCreated.TaskId))).StatusCode);

        var foreignTenant = await strangerClient.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Foreign Tasks", $"ftask-{Guid.NewGuid():N}"[..20]));
        foreignTenant.EnsureSuccessStatusCode();
        var foreign = await foreignTenant.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(foreign);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.GetAsync(
                TaskPath(foreign.TenantId, workspace.WorkspaceId, project.ProjectId))).StatusCode);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();
        var backToView = await memberClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest("Blocked again"));
        Assert.Equal(HttpStatusCode.Forbidden, backToView.StatusCode);

        (await ownerClient.DeleteAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}"))
            .EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberClient.GetAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, editCreated.TaskId))).StatusCode);

        await factory.SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Suspended);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.GetAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId))).StatusCode);
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
        string title)
    {
        var response = await client.PostAsJsonAsync(
            TaskPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title, null, "Todo", "Normal", null));
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
