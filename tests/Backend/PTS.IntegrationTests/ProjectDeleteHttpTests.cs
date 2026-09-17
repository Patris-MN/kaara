using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectDeleteHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public ProjectDeleteHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_and_admin_can_delete_empty_project()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pd-own"), "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, Email("pd-adm"), "Admin");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Delete Org");
        await factory.CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Empty");

        (await ownerClient.DeleteAsync(
            ProjectPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId))).EnsureSuccessStatusCode();

        var recreated = await CreateProjectAsync(adminClient, tenant.TenantId, workspace.WorkspaceId, "Also empty");
        (await adminClient.DeleteAsync(
            ProjectPath(tenant.TenantId, workspace.WorkspaceId, recreated.ProjectId))).EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Project_with_tasks_cannot_be_deleted()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pd-task"), "Owner");
        Authorize(ownerClient, owner.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Task Block Org");
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Busy");
        await CreateTaskAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Still here");

        var deleteAttempt = await ownerClient.DeleteAsync(
            ProjectPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(HttpStatusCode.Conflict, deleteAttempt.StatusCode);
        Assert.Equal("project_has_tasks", await ReadErrorAsync(deleteAttempt));

        var listed = await ownerClient.GetFromJsonAsync<ProjectResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects");
        Assert.Contains(listed!, item => item.ProjectId == project.ProjectId);
    }

    [SkippableFact]
    public async Task Member_with_workspace_edit_cannot_delete_project()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pd-mem-own"), "Owner");
        var memberEmail = Email("pd-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Member Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Protected");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var deleteAttempt = await memberClient.DeleteAsync(
            ProjectPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(HttpStatusCode.Forbidden, deleteAttempt.StatusCode);
        Assert.Equal("project_delete_forbidden", await ReadErrorAsync(deleteAttempt));
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
            new CreateTenantRequest(name, $"pd-{Guid.NewGuid():N}"[..20]));
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

    private static async Task<WorkTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks",
            new CreateWorkTaskRequest(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkTaskResponse>())!;
    }

    private static string ProjectPath(Guid tenantId, Guid workspaceId, Guid projectId)
        => $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}";

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
