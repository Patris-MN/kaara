using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TicketCollaborationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TicketCollaborationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Creator_assignee_and_previous_assignee_follow_the_permission_matrix()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var saraClient = _web.CreateClient();
        var aliClient = _web.CreateClient();
        var viewClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("own"), "Mohammad");
        var saraEmail = Email("sara");
        var sara = await RegisterAndLoginAsync(saraClient, saraEmail, "Sara");
        var aliEmail = Email("ali");
        var ali = await RegisterAndLoginAsync(aliClient, aliEmail, "Ali");
        var viewEmail = Email("view");
        var viewer = await RegisterAndLoginAsync(viewClient, viewEmail, "Viewer");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(saraClient, sara.AccessToken);
        Authorize(aliClient, ali.AccessToken);
        Authorize(viewClient, viewer.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Ticket Org", "tix");
        await InviteAndAcceptAsync(ownerClient, saraClient, tenant.TenantId, saraEmail);
        await InviteAndAcceptAsync(ownerClient, aliClient, tenant.TenantId, aliEmail);
        await InviteAndAcceptAsync(ownerClient, viewClient, tenant.TenantId, viewEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Desk");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Inbox");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var saraMembership = Assert.Single(members!, item => item.UserId == sara.UserId);
        var aliMembership = Assert.Single(members!, item => item.UserId == ali.UserId);
        var viewMembership = Assert.Single(members!, item => item.UserId == viewer.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, saraMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, aliMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, viewMembership.MembershipId, workspace.WorkspaceId, "View");

        var created = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Printer jam", saraMembership.MembershipId);
        Assert.Equal("Open", created.Status);
        Assert.NotNull(created.CreatedByMembershipId);
        Assert.True(created.Capabilities!.CanDelete);
        Assert.True(created.Capabilities.CanEditDefinition);
        Assert.Contains("Closed", created.Capabilities.AllowedStatuses);

        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);
        (await ownerClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "InProgress", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId)))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Hijacked title", "Please clear tray 2", "InProgress", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId))));
        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Rewritten request", "InProgress", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId))));
        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "InProgress", "Urgent", new DateOnly(2026, 9, 1), saraMembership.MembershipId))));
        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "InProgress", "High", new DateOnly(2026, 9, 2), saraMembership.MembershipId))));
        Assert.Equal(
            "task_status_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "Closed", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId))));
        Assert.Equal(
            "task_delete_forbidden",
            await ReadErrorAsync(await saraClient.DeleteAsync(path)));

        var operational = await saraClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Waiting", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId));
        operational.EnsureSuccessStatusCode();

        var tagged = await saraClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Waiting", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId, null, ["Printer"]));
        tagged.EnsureSuccessStatusCode();

        var comment = await saraClient.PostAsJsonAsync(CommentPath(path), new CreateWorkTaskCommentRequest("Cleared tray 1"));
        comment.EnsureSuccessStatusCode();
        var commentBody = await comment.Content.ReadFromJsonAsync<WorkTaskCommentResponse>();
        Assert.True(commentBody!.IsOwn);

        var editedComment = await saraClient.PutAsJsonAsync(
            $"{CommentPath(path)}/{commentBody.CommentId}",
            new CreateWorkTaskCommentRequest("Cleared tray 1 and 2"));
        editedComment.EnsureSuccessStatusCode();

        var viewComment = await viewClient.PostAsJsonAsync(CommentPath(path), new CreateWorkTaskCommentRequest("I can see this"));
        viewComment.EnsureSuccessStatusCode();

        var resolved = await saraClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Resolved", "High", new DateOnly(2026, 9, 1), saraMembership.MembershipId));
        resolved.EnsureSuccessStatusCode();

        var reassigned = await saraClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Resolved", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId));
        reassigned.EnsureSuccessStatusCode();
        var afterReassign = await reassigned.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal(aliMembership.MembershipId, afterReassign!.AssigneeMembershipId);
        Assert.False(afterReassign.Capabilities!.CanReassign);
        Assert.False(afterReassign.Capabilities.CanManageTags);

        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "InProgress", "High", new DateOnly(2026, 9, 1), viewMembership.MembershipId))));
        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "Resolved", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId, null, ["Old"]))));
        Assert.Equal(
            "task_status_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "InProgress", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId))));

        var saraStillSees = await saraClient.GetAsync(path);
        saraStillSees.EnsureSuccessStatusCode();
        var previousComment = await saraClient.PostAsJsonAsync(CommentPath(path), new CreateWorkTaskCommentRequest("Handed to Ali"));
        previousComment.EnsureSuccessStatusCode();

        var closed = await ownerClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Closed", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId));
        closed.EnsureSuccessStatusCode();
        var closedBody = await closed.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal("Closed", closedBody!.Status);
        Assert.False(closedBody.Capabilities!.CanEditDefinition);

        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await ownerClient.PutAsJsonAsync(
                path,
                Update("Closed rewrite", "Please clear tray 2", "Closed", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId))));
        Assert.Equal(
            "task_status_forbidden",
            await ReadErrorAsync(await aliClient.PutAsJsonAsync(
                path,
                Update("Printer jam fixed", "Please clear tray 2", "Open", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId))));

        var reopened = await ownerClient.PutAsJsonAsync(
            path,
            Update("Printer jam fixed", "Please clear tray 2", "Open", "High", new DateOnly(2026, 9, 1), aliMembership.MembershipId));
        reopened.EnsureSuccessStatusCode();
        Assert.Equal("Open", (await reopened.Content.ReadFromJsonAsync<WorkTaskResponse>())!.Status);

        var otherTask = await CreateTaskAsync(
            saraClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Sara owned", null);
        Assert.Equal(
            "task_delete_forbidden",
            await ReadErrorAsync(await ownerClient.DeleteAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, otherTask.TaskId))));
        (await saraClient.DeleteAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, otherTask.TaskId)))
            .EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ownerClient.GetAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, otherTask.TaskId))).StatusCode);
    }

    [SkippableFact]
    public async Task Stale_assignee_cannot_reassign_after_creator_handoff()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var saraClient = _web.CreateClient();
        var aliClient = _web.CreateClient();
        var rezaClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("stale-o"), "Mohammad");
        var saraEmail = Email("stale-s");
        var sara = await RegisterAndLoginAsync(saraClient, saraEmail, "Sara");
        var aliEmail = Email("stale-a");
        var ali = await RegisterAndLoginAsync(aliClient, aliEmail, "Ali");
        var rezaEmail = Email("stale-r");
        var reza = await RegisterAndLoginAsync(rezaClient, rezaEmail, "Reza");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(saraClient, sara.AccessToken);
        Authorize(aliClient, ali.AccessToken);
        Authorize(rezaClient, reza.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Stale Org", "stale");
        await InviteAndAcceptAsync(ownerClient, saraClient, tenant.TenantId, saraEmail);
        await InviteAndAcceptAsync(ownerClient, aliClient, tenant.TenantId, aliEmail);
        await InviteAndAcceptAsync(ownerClient, rezaClient, tenant.TenantId, rezaEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Handoff");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Queue");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var saraMembership = Assert.Single(members!, item => item.UserId == sara.UserId);
        var aliMembership = Assert.Single(members!, item => item.UserId == ali.UserId);
        var rezaMembership = Assert.Single(members!, item => item.UserId == reza.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, saraMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, aliMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, rezaMembership.MembershipId, workspace.WorkspaceId, "Edit");

        var created = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Stale ticket", saraMembership.MembershipId);
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);

        (await ownerClient.PutAsJsonAsync(
            path,
            Update("Stale ticket", null, "Open", "Normal", null, aliMembership.MembershipId)))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            "task_field_forbidden",
            await ReadErrorAsync(await saraClient.PutAsJsonAsync(
                path,
                Update("Stale ticket", null, "Open", "Normal", null, rezaMembership.MembershipId))));

        var persisted = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.Equal(aliMembership.MembershipId, persisted!.AssigneeMembershipId);
    }

    [SkippableFact]
    public async Task Activity_last_seen_and_notifications_follow_ticket_participants()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var saraClient = _web.CreateClient();
        var aliClient = _web.CreateClient();
        var otherClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("act-o"), "Mohammad");
        var saraEmail = Email("act-s");
        var sara = await RegisterAndLoginAsync(saraClient, saraEmail, "Sara");
        var aliEmail = Email("act-a");
        var ali = await RegisterAndLoginAsync(aliClient, aliEmail, "Ali");
        var other = await RegisterAndLoginAsync(otherClient, Email("act-x"), "Other");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(saraClient, sara.AccessToken);
        Authorize(aliClient, ali.AccessToken);
        Authorize(otherClient, other.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "History Org", "hist");
        await InviteAndAcceptAsync(ownerClient, saraClient, tenant.TenantId, saraEmail);
        await InviteAndAcceptAsync(ownerClient, aliClient, tenant.TenantId, aliEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "History");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Log");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var saraMembership = Assert.Single(members!, item => item.UserId == sara.UserId);
        var aliMembership = Assert.Single(members!, item => item.UserId == ali.UserId);
        await GrantAccessAsync(ownerClient, tenant.TenantId, saraMembership.MembershipId, workspace.WorkspaceId, "Edit");
        await GrantAccessAsync(ownerClient, tenant.TenantId, aliMembership.MembershipId, workspace.WorkspaceId, "View");

        var created = await CreateTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Toner", saraMembership.MembershipId);
        var path = TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId);

        var listedBeforeView = await saraClient.GetFromJsonAsync<WorkTaskResponse[]>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        var listedTask = Assert.Single(listedBeforeView!, item => item.TaskId == created.TaskId);
        Assert.True(listedTask.UnseenActivityCount >= 1);

        var firstView = await saraClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.True(firstView!.UnseenActivityCount >= 1);
        var listedAfterView = await saraClient.GetFromJsonAsync<WorkTaskResponse[]>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(0, Assert.Single(listedAfterView!, item => item.TaskId == created.TaskId).UnseenActivityCount);

        (await ownerClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "Open", "Urgent", new DateOnly(2026, 9, 2), saraMembership.MembershipId)))
            .EnsureSuccessStatusCode();

        var secondList = await saraClient.GetFromJsonAsync<WorkTaskResponse[]>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(2, Assert.Single(secondList!, item => item.TaskId == created.TaskId).UnseenActivityCount);

        var ownChange = await saraClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "InProgress", "Urgent", new DateOnly(2026, 9, 2), saraMembership.MembershipId));
        ownChange.EnsureSuccessStatusCode();
        var afterOwn = await saraClient.GetFromJsonAsync<WorkTaskResponse[]>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId));
        Assert.Equal(2, Assert.Single(afterOwn!, item => item.TaskId == created.TaskId).UnseenActivityCount);

        var ownerSeen = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(path);
        Assert.True(ownerSeen!.UnseenActivityCount >= 1);

        var activity = await saraClient.GetFromJsonAsync<WorkTaskActivityResponse[]>($"{path}/activity");
        Assert.Contains(activity!, item => item.EventType == "TaskCreated" && item.ActorMembershipId != Guid.Empty);
        Assert.Contains(activity, item => item.EventType == "PriorityChanged" && item.OldValue == "Normal" && item.NewValue == "Urgent");
        Assert.Contains(activity, item => item.EventType == "DeadlineChanged");
        Assert.Contains(activity, item => item.EventType == "StatusChanged" && item.OldValue == "Open" && item.NewValue == "InProgress");
        Assert.All(activity, item => Assert.Equal(DateTimeOffset.UtcNow.Offset, item.CreatedAtUtc.Offset));

        (await saraClient.PostAsJsonAsync($"{path}/comments", new CreateWorkTaskCommentRequest("Working on it")))
            .EnsureSuccessStatusCode();
        (await saraClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "Resolved", "Urgent", new DateOnly(2026, 9, 2), saraMembership.MembershipId, null, ["Office"])))
            .EnsureSuccessStatusCode();
        (await saraClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "Resolved", "Urgent", new DateOnly(2026, 9, 2), aliMembership.MembershipId)))
            .EnsureSuccessStatusCode();

        var ownerNotes = await ownerClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Contains(ownerNotes!, item => item.Type == "TaskCommentAdded");
        Assert.Contains(ownerNotes, item => item.Type == "TaskStatusChanged");
        Assert.Contains(ownerNotes, item => item.Type == "TaskTagChanged");
        Assert.Contains(ownerNotes, item => item.Type == "TaskReassigned");
        Assert.DoesNotContain(ownerNotes, item => item.Type == "TaskAssigned");

        var saraNotes = await saraClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Contains(saraNotes!, item => item.Type == "TaskAssigned");
        Assert.Contains(saraNotes, item => item.Type == "TaskPriorityChanged");
        Assert.Contains(saraNotes, item => item.Type == "TaskDeadlineChanged");
        Assert.DoesNotContain(saraNotes, item => item.Type == "TaskCommentAdded");

        var aliNotes = await aliClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Contains(aliNotes!, item => item.Type is "TaskAssigned" or "TaskReassigned");

        (await ownerClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "Closed", "Urgent", new DateOnly(2026, 9, 2), aliMembership.MembershipId)))
            .EnsureSuccessStatusCode();
        var aliAfterClose = await aliClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Contains(aliAfterClose!, item => item.Type == "TaskClosed");
        var saraAfterClose = await saraClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.DoesNotContain(saraAfterClose!, item => item.Type == "TaskClosed");

        (await ownerClient.PutAsJsonAsync(
            path,
            Update("Toner", null, "Open", "Urgent", new DateOnly(2026, 9, 2), aliMembership.MembershipId)))
            .EnsureSuccessStatusCode();
        var reopenActivity = await ownerClient.GetFromJsonAsync<WorkTaskActivityResponse[]>($"{path}/activity");
        Assert.Contains(reopenActivity!, item => item.EventType == "TaskReopened");

        var foreign = await CreateTenantAsync(otherClient, "Foreign History", "fhis");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await otherClient.GetAsync($"{path}/activity")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await saraClient.GetAsync(
                $"/tenants/{foreign.TenantId}/workspaces/{workspace.WorkspaceId}/projects/{project.ProjectId}/tasks/{created.TaskId}/activity"))
                .StatusCode);
        _ = foreign;
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
        (await memberClient.PostAsJsonAsync($"/tenants/{tenantId}/invitations/accept", new { }))
            .EnsureSuccessStatusCode();
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
        Guid? assigneeMembershipId)
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
