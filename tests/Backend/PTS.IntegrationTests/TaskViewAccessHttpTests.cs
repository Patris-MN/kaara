using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskViewAccessHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskViewAccessHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task View_member_can_read_but_cannot_mutate_tasks_including_as_creator_or_assignee()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("view-own"), "Owner");
        var memberEmail = Email("view-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "View Task Org", "vto");
        await InviteAndAcceptAsync(ownerClient, memberClient, tenant.TenantId, memberEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Read Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Inbox");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "View");

        var ownerTask = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Owner owned");
        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "Edit");
        var memberTask = await CreateTaskAsync(
            memberClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Member owned");
        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "View");
        var assigned = await CreateTaskAsync(
            ownerClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            "Assigned to member",
            memberRecord.MembershipId);

        var ownerPath = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerTask.TaskId);
        var memberPath = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, memberTask.TaskId);
        var assignedPath = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, assigned.TaskId);

        (await memberClient.GetAsync(ownerPath)).EnsureSuccessStatusCode();
        (await memberClient.GetAsync(memberPath)).EnsureSuccessStatusCode();
        (await memberClient.GetAsync(assignedPath)).EnsureSuccessStatusCode();

        var ownerRead = await memberClient.GetFromJsonAsync<WorkTaskResponse>(ownerPath);
        Assert.False(ownerRead!.Capabilities!.CanEditDefinition);
        Assert.False(ownerRead.Capabilities.CanComment);
        Assert.False(ownerRead.Capabilities.CanDelete);

        var memberRead = await memberClient.GetFromJsonAsync<WorkTaskResponse>(memberPath);
        Assert.False(memberRead!.Capabilities!.CanEditDefinition);
        Assert.False(memberRead.Capabilities.CanComment);

        var assignedRead = await memberClient.GetFromJsonAsync<WorkTaskResponse>(assignedPath);
        Assert.False(assignedRead!.Capabilities!.CanManageTags);
        Assert.False(assignedRead.Capabilities.CanReassign);

        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            memberPath,
            Update("Rewrite", "desc", "InProgress", "High", new DateOnly(2026, 9, 1), memberRecord.MembershipId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            assignedPath,
            Update("Assigned to member", null, "Waiting", "Normal", null, memberRecord.MembershipId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync(
            CommentPath(assignedPath),
            new CreateWorkTaskCommentRequest("Blocked"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.DeleteAsync(memberPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            assignedPath,
            Update("Assigned to member", null, "Open", "Normal", null, memberRecord.MembershipId, null, ["Blocked"]))).StatusCode);
    }

    [SkippableFact]
    public async Task Edit_member_follows_creator_and_assignee_rules_after_workspace_edit_prerequisite()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var creatorClient = _web.CreateClient();
        var assigneeClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("edit-own"), "Owner");
        var creatorEmail = Email("edit-cr");
        var creator = await RegisterAndLoginAsync(creatorClient, creatorEmail, "Creator");
        var assigneeEmail = Email("edit-as");
        var assignee = await RegisterAndLoginAsync(assigneeClient, assigneeEmail, "Assignee");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(creatorClient, creator.AccessToken);
        Authorize(assigneeClient, assignee.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Edit Task Org", "eto");
        await InviteAndAcceptAsync(ownerClient, creatorClient, tenant.TenantId, creatorEmail);
        await InviteAndAcceptAsync(ownerClient, assigneeClient, tenant.TenantId, assigneeEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Edit Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Queue");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var creatorMembership = Assert.Single(members!, item => item.UserId == creator.UserId);
        var assigneeMembership = Assert.Single(members!, item => item.UserId == assignee.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, creatorMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, assigneeMembership.MembershipId, workspace.WorkspaceId, "Edit");

        var created = await CreateTaskAsync(
            creatorClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            "Creator ticket",
            assigneeMembership.MembershipId);
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);

        var creatorRead = await creatorClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.True(creatorRead!.Capabilities!.CanEditDefinition);
        Assert.True(creatorRead.Capabilities.CanDelete);

        var assigneeRead = await assigneeClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.False(assigneeRead!.Capabilities!.CanEditDefinition);
        Assert.True(assigneeRead.Capabilities.CanManageTags);
        Assert.True(assigneeRead.Capabilities.CanComment);
        Assert.True(assigneeRead.Capabilities.CanDelete);

        (await assigneeClient.PutAsJsonAsync(
            path,
            Update("Creator ticket", null, "Waiting", "Normal", null, assigneeMembership.MembershipId))).EnsureSuccessStatusCode();
        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await assigneeClient.PutAsJsonAsync(
                path,
                Update("Hijacked", null, "Waiting", "Normal", null, assigneeMembership.MembershipId))));
    }

    [SkippableFact]
    public async Task Workspace_access_change_from_edit_to_view_removes_task_mutation()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("flip-own"), "Owner");
        var memberEmail = Email("flip-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Flip Org", "flip");
        await InviteAndAcceptAsync(ownerClient, memberClient, tenant.TenantId, memberEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Flip Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Queue");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "Edit");

        var created = await CreateTaskAsync(
            memberClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Editable");
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);
        (await memberClient.PutAsJsonAsync(
            path,
            Update("Editable now", null, "InProgress", "Normal", null, null))).EnsureSuccessStatusCode();

        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "View");
        var afterView = await memberClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.False(afterView!.Capabilities!.CanEditDefinition);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            path,
            Update("Blocked again", null, "Waiting", "Normal", null, null))).StatusCode);

        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "Edit");
        var afterEdit = await memberClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.True(afterEdit!.Capabilities!.CanEditDefinition);
    }

    [SkippableFact]
    public async Task Suspended_and_removed_members_cannot_mutate_tasks()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("mem-own"), "Owner");
        var memberEmail = Email("mem-sus");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Member Org", "mem");
        await InviteAndAcceptAsync(ownerClient, memberClient, tenant.TenantId, memberEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Queue");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, memberRecord.MembershipId, workspace.WorkspaceId, "Edit");

        var created = await CreateTaskAsync(
            memberClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Shared");
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);

        await factory.SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Suspended);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            path,
            Update("Suspended", null, "InProgress", "Normal", null, null))).StatusCode);

        await factory.SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Active);
        (await memberClient.PutAsJsonAsync(
            path,
            Update("Active again", null, "InProgress", "Normal", null, null))).EnsureSuccessStatusCode();

        await factory.SetMembershipStatusAsync(member.UserId, tenant.TenantId, MembershipStatus.Removed);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(
            path,
            Update("Removed", null, "Waiting", "Normal", null, null))).StatusCode);
    }

    [SkippableFact]
    public async Task Cross_tenant_task_mutation_remains_forbidden()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var foreignClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("x-own"), "Owner");
        var memberEmail = Email("x-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        var foreign = await RegisterAndLoginAsync(foreignClient, Email("x-for"), "Foreign");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);
        Authorize(foreignClient, foreign.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Cross Org", "xorg");
        await InviteAndAcceptAsync(ownerClient, memberClient, tenant.TenantId, memberEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Queue");
        var created = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Protected");
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);

        var foreignTenant = await CreateTenantAsync(foreignClient, "Foreign Org", "forg");
        _ = foreignTenant;

        Assert.Equal(HttpStatusCode.Forbidden, (await foreignClient.PutAsJsonAsync(
            path,
            Update("Foreign hijack", null, "Closed", "Low", null, null))).StatusCode);
    }

    private static UpdateWorkTaskRequest Update(
        string title,
        string? description,
        string status,
        string priority,
        DateOnly? dueDate,
        Guid? assignee,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? newTags = null)
        => new(title, description, status, priority, dueDate, assignee, tagIds, newTags);

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId, Guid? taskId = null)
        => taskId is { } id
            ? $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{id}"
            : $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks";

    private static string CommentPath(string taskPath) => $"{taskPath}/comments";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task GrantAccessAsync(
        HttpClient client,
        Guid tenantId,
        Guid membershipId,
        Guid workspaceId,
        string level)
    {
        (await client.PutAsJsonAsync(
            $"/tenants/{tenantId}/members/{membershipId}/workspace-access/{workspaceId}",
            new SetWorkspaceAccessRequest(level))).EnsureSuccessStatusCode();
    }

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, string name, string slugPrefix)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"{slugPrefix}-{Guid.NewGuid():N}"[..20]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }

    private static async Task InviteAndAcceptAsync(
        HttpClient ownerClient,
        HttpClient memberClient,
        Guid tenantId,
        string email)
    {
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenantId}/invitations",
            new InviteMemberRequest(email))).EnsureSuccessStatusCode();
        (await memberClient.PostAsJsonAsync(
            $"/tenants/{tenantId}/invitations/accept",
            new { })).EnsureSuccessStatusCode();
    }

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
            TestProjectFactory.CreateRequest(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private static async Task<WorkTaskResponse> CreateTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title,
        Guid? assigneeMembershipId = null)
    {
        var response = await client.PostAsJsonAsync(
            TaskPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title, null, "Open", "Normal", null, null, assigneeMembershipId));
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
