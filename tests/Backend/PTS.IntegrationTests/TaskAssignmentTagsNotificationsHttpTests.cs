using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TaskAssignmentTagsNotificationsHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TaskAssignmentTagsNotificationsHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Assignment_follows_edit_permission_and_assignable_member_rules()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var editorClient = _web.CreateClient();
        var viewerClient = _web.CreateClient();
        var invitedClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("own"), "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, Email("adm"), "Admin");
        var editorEmail = Email("edit");
        var editor = await RegisterAndLoginAsync(editorClient, editorEmail, "Editor");
        var viewerEmail = Email("view");
        var viewer = await RegisterAndLoginAsync(viewerClient, viewerEmail, "Viewer");
        var invitedEmail = Email("inv");
        _ = await RegisterAndLoginAsync(invitedClient, invitedEmail, "Invited");
        var stranger = await RegisterAndLoginAsync(strangerClient, Email("str"), "Stranger");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);
        Authorize(editorClient, editor.AccessToken);
        Authorize(viewerClient, viewer.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Assign Org", "asgn");
        await factory.CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);
        await InviteAndAcceptAsync(ownerClient, editorClient, tenant.TenantId, editorEmail);
        await InviteAndAcceptAsync(ownerClient, viewerClient, tenant.TenantId, viewerEmail);
        (await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(invitedEmail))).EnsureSuccessStatusCode();

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Assign Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Assign Project");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        Assert.NotNull(members);
        var ownerMembership = Assert.Single(members, item => item.UserId == owner.UserId);
        var adminMembership = Assert.Single(members, item => item.UserId == admin.UserId);
        var editorMembership = Assert.Single(members, item => item.UserId == editor.UserId);
        var viewerMembership = Assert.Single(members, item => item.UserId == viewer.UserId);
        var invitedMembership = Assert.Single(members, item => item.Email == invitedEmail);

        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, editorMembership.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, viewerMembership.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var assignable = await ownerClient.GetFromJsonAsync<AssignableMemberResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/assignable-members");
        Assert.NotNull(assignable);
        Assert.Contains(assignable, item => item.MembershipId == ownerMembership.MembershipId);
        Assert.Contains(assignable, item => item.MembershipId == adminMembership.MembershipId);
        Assert.Contains(assignable, item => item.MembershipId == editorMembership.MembershipId);
        Assert.Contains(assignable, item => item.MembershipId == viewerMembership.MembershipId);
        Assert.DoesNotContain(assignable, item => item.MembershipId == invitedMembership.MembershipId);

        var ownerAssigned = await CreateAssignedTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Owner assigns", editorMembership.MembershipId);
        Assert.Equal(editorMembership.MembershipId, ownerAssigned.AssigneeMembershipId);
        var ownerReload = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerAssigned.TaskId));
        Assert.Equal(editorMembership.MembershipId, ownerReload!.AssigneeMembershipId);

        var adminAssigned = await CreateAssignedTaskAsync(
            adminClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Admin assigns", viewerMembership.MembershipId);
        Assert.Equal(viewerMembership.MembershipId, adminAssigned.AssigneeMembershipId);

        var editorAssigned = await CreateAssignedTaskAsync(
            editorClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Editor assigns", viewerMembership.MembershipId);
        Assert.Equal(viewerMembership.MembershipId, editorAssigned.AssigneeMembershipId);

        var viewAssign = await viewerClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest("View assign", null, "Todo", "Normal", null, null, viewerMembership.MembershipId));
        Assert.Equal(HttpStatusCode.Forbidden, viewAssign.StatusCode);

        Assert.Equal(
            "invalid_assignee",
            await ReadErrorAsync(await ownerClient.PostAsJsonAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
                new CreateWorkTaskRequest("Invited", null, "Todo", "Normal", null, null, invitedMembership.MembershipId))));

        var noAccessEmail = Email("none");
        var noAccessClient = _web.CreateClient();
        var noAccess = await RegisterAndLoginAsync(noAccessClient, noAccessEmail, "None");
        Authorize(noAccessClient, noAccess.AccessToken);
        await InviteAndAcceptAsync(ownerClient, noAccessClient, tenant.TenantId, noAccessEmail);
        var refreshedMembers = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var noAccessMembership = Assert.Single(refreshedMembers!, item => item.UserId == noAccess.UserId);
        Assert.Equal(
            "invalid_assignee",
            await ReadErrorAsync(await ownerClient.PostAsJsonAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
                new CreateWorkTaskRequest("No access", null, "Todo", "Normal", null, null, noAccessMembership.MembershipId))));

        var foreignTenant = await CreateTenantAsync(strangerClient, "Foreign Assign", "fasg");
        var foreignMembers = await strangerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{foreignTenant.TenantId}/members");
        var foreignMembership = Assert.Single(foreignMembers!, item => item.UserId == stranger.UserId);
        Assert.Equal(
            "invalid_assignee",
            await ReadErrorAsync(await ownerClient.PostAsJsonAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
                new CreateWorkTaskRequest("Foreign", null, "Todo", "Normal", null, null, foreignMembership.MembershipId))));

        await factory.SetMembershipStatusAsync(viewer.UserId, tenant.TenantId, MembershipStatus.Suspended);
        Assert.Equal(
            "invalid_assignee",
            await ReadErrorAsync(await ownerClient.PutAsJsonAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerAssigned.TaskId),
                new UpdateWorkTaskRequest("Owner assigns", null, "Todo", "Normal", null, viewerMembership.MembershipId))));

        await factory.SetMembershipStatusAsync(viewer.UserId, tenant.TenantId, MembershipStatus.Active);
        var reassigned = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerAssigned.TaskId),
            new UpdateWorkTaskRequest("Owner assigns", null, "Todo", "Normal", null, adminMembership.MembershipId));
        reassigned.EnsureSuccessStatusCode();
        var reassignedBody = await reassigned.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal(adminMembership.MembershipId, reassignedBody!.AssigneeMembershipId);

        var unassigned = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerAssigned.TaskId),
            new UpdateWorkTaskRequest("Owner assigns", null, "Todo", "Normal", null, null));
        unassigned.EnsureSuccessStatusCode();
        var unassignedBody = await unassigned.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Null(unassignedBody!.AssigneeMembershipId);
        var unassignedReload = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, ownerAssigned.TaskId));
        Assert.Null(unassignedReload!.AssigneeMembershipId);
    }

    [SkippableFact]
    public async Task Assignment_notifications_are_created_once_and_isolated_to_the_recipient()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberBClient = _web.CreateClient();
        var memberCClient = _web.CreateClient();
        var otherTenantClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("ntf-o"), "Owner");
        var memberBEmail = Email("ntf-b");
        var memberB = await RegisterAndLoginAsync(memberBClient, memberBEmail, "Member B");
        var memberCEmail = Email("ntf-c");
        var memberC = await RegisterAndLoginAsync(memberCClient, memberCEmail, "Member C");
        var other = await RegisterAndLoginAsync(otherTenantClient, Email("ntf-x"), "Other");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberBClient, memberB.AccessToken);
        Authorize(memberCClient, memberC.AccessToken);
        Authorize(otherTenantClient, other.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Notify Org", "ntfy");
        await InviteAndAcceptAsync(ownerClient, memberBClient, tenant.TenantId, memberBEmail);
        await InviteAndAcceptAsync(ownerClient, memberCClient, tenant.TenantId, memberCEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Notify Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Notify Project");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var ownerMembership = Assert.Single(members!, item => item.UserId == owner.UserId);
        var membershipB = Assert.Single(members!, item => item.UserId == memberB.UserId);
        var membershipC = Assert.Single(members!, item => item.UserId == memberC.UserId);

        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, membershipB.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, membershipC.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var created = await CreateAssignedTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Notify me", membershipB.MembershipId);

        var ownerNotes = await ownerClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Empty(ownerNotes!);

        var notesB = await memberBClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        var assigned = Assert.Single(notesB!);
        Assert.Equal("TaskAssigned", assigned.Type);
        Assert.Equal(created.TaskId, assigned.TaskId);
        Assert.False(assigned.IsRead);

        var unchanged = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId),
            new UpdateWorkTaskRequest("Notify me", null, "Todo", "Normal", null, membershipB.MembershipId));
        unchanged.EnsureSuccessStatusCode();
        var notesBAgain = await memberBClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Single(notesBAgain!);

        var reassigned = await ownerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, created.TaskId),
            new UpdateWorkTaskRequest("Notify me", null, "Todo", "Normal", null, membershipC.MembershipId));
        reassigned.EnsureSuccessStatusCode();

        var notesC = await memberCClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        var forC = Assert.Single(notesC!);
        Assert.Equal(created.TaskId, forC.TaskId);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberBClient.PostAsync(
                $"/tenants/{tenant.TenantId}/notifications/{forC.NotificationId}/read",
                null)).StatusCode);

        (await memberCClient.PostAsync(
            $"/tenants/{tenant.TenantId}/notifications/{forC.NotificationId}/read",
            null)).EnsureSuccessStatusCode();
        var readC = await memberCClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.True(Assert.Single(readC!).IsRead);

        var foreign = await CreateTenantAsync(otherTenantClient, "Other Notify", "ontf");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberCClient.GetAsync($"/tenants/{foreign.TenantId}/notifications")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await otherTenantClient.GetAsync($"/tenants/{tenant.TenantId}/notifications")).StatusCode);

        var selfAssigned = await CreateAssignedTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Self", ownerMembership.MembershipId);
        _ = selfAssigned;
        var ownerSelf = await ownerClient.GetFromJsonAsync<WorkNotificationResponse[]>(
            $"/tenants/{tenant.TenantId}/notifications");
        Assert.Empty(ownerSelf!);
    }

    [SkippableFact]
    public async Task Tags_are_normalized_persisted_and_blocked_across_tenants_or_view_users()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var editorClient = _web.CreateClient();
        var viewerClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();

        var owner = await RegisterAndLoginAsync(ownerClient, Email("tag-o"), "Owner");
        var editorEmail = Email("tag-e");
        var editor = await RegisterAndLoginAsync(editorClient, editorEmail, "Editor");
        var viewerEmail = Email("tag-v");
        var viewer = await RegisterAndLoginAsync(viewerClient, viewerEmail, "Viewer");
        var stranger = await RegisterAndLoginAsync(strangerClient, Email("tag-s"), "Stranger");

        Authorize(ownerClient, owner.AccessToken);
        Authorize(editorClient, editor.AccessToken);
        Authorize(viewerClient, viewer.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Tag Org", "tags");
        await InviteAndAcceptAsync(ownerClient, editorClient, tenant.TenantId, editorEmail);
        await InviteAndAcceptAsync(ownerClient, viewerClient, tenant.TenantId, viewerEmail);
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Tag Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Tag Project");
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var editorMembership = Assert.Single(members!, item => item.UserId == editor.UserId);
        var viewerMembership = Assert.Single(members!, item => item.UserId == viewer.UserId);
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, editorMembership.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, viewerMembership.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var createdTag = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/tags",
            new CreateWorkTagRequest("Backend"));
        createdTag.EnsureSuccessStatusCode();
        var backend = await createdTag.Content.ReadFromJsonAsync<WorkTagResponse>();
        Assert.Equal("Backend", backend!.Name);

        var duplicate = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/tags",
            new CreateWorkTagRequest("backend"));
        duplicate.EnsureSuccessStatusCode();
        var duplicateBody = await duplicate.Content.ReadFromJsonAsync<WorkTagResponse>();
        Assert.Equal(backend.TagId, duplicateBody!.TagId);

        Assert.Equal(
            "invalid_tag",
            await ReadErrorAsync(await ownerClient.PostAsJsonAsync(
                $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/tags",
                new CreateWorkTagRequest("   "))));

        var viewCreate = await viewerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/tags",
            new CreateWorkTagRequest("Secret"));
        Assert.Equal(HttpStatusCode.Forbidden, viewCreate.StatusCode);

        var created = await editorClient.PostAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
            new CreateWorkTaskRequest(
                "Tagged",
                null,
                "Todo",
                "Normal",
                null,
                null,
                null,
                [backend.TagId],
                ["Review"]));
        created.EnsureSuccessStatusCode();
        var createdBody = await created.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal(2, createdBody!.Tags!.Count);
        Assert.Contains(createdBody.Tags, tag => tag.Name == "Backend");
        Assert.Contains(createdBody.Tags, tag => tag.Name == "Review");

        var reloaded = await ownerClient.GetFromJsonAsync<WorkTaskResponse>(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, createdBody.TaskId));
        Assert.Equal(2, reloaded!.Tags!.Count);

        var reviewId = reloaded.Tags.Single(tag => tag.Name == "Review").TagId;
        var removed = await editorClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, createdBody.TaskId),
            new UpdateWorkTaskRequest("Tagged", null, "Todo", "Normal", null, null, [reviewId]));
        removed.EnsureSuccessStatusCode();
        var removedBody = await removed.Content.ReadFromJsonAsync<WorkTaskResponse>();
        Assert.Equal("Review", Assert.Single(removedBody!.Tags!).Name);

        var viewMutate = await viewerClient.PutAsJsonAsync(
            TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId, createdBody.TaskId),
            new UpdateWorkTaskRequest("Tagged", null, "Todo", "Normal", null, null, [backend.TagId]));
        Assert.Equal(HttpStatusCode.Forbidden, viewMutate.StatusCode);

        var foreign = await CreateTenantAsync(strangerClient, "Foreign Tags", "ftag");
        var foreignWorkspace = await CreateWorkspaceAsync(strangerClient, foreign.TenantId, "Foreign Space");
        var foreignTagResponse = await strangerClient.PostAsJsonAsync(
            $"/tenants/{foreign.TenantId}/workspaces/{foreignWorkspace.WorkspaceId}/tags",
            new CreateWorkTagRequest("Foreign"));
        foreignTagResponse.EnsureSuccessStatusCode();
        var foreignTag = await foreignTagResponse.Content.ReadFromJsonAsync<WorkTagResponse>();
        Assert.Equal(
            "invalid_tag",
            await ReadErrorAsync(await ownerClient.PostAsJsonAsync(
                TaskPath(tenant.TenantId, workspace.WorkspaceId, project.ProjectId),
                new CreateWorkTaskRequest(
                    "Cross tag",
                    null,
                    "Todo",
                    "Normal",
                    null,
                    null,
                    null,
                    [foreignTag!.TagId]))));
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId, Guid? taskId = null)
        => taskId is { } id
            ? $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{id}"
            : $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks";

    private static string AccessPath(Guid tenantId, Guid membershipId, Guid workspaceId)
        => $"/tenants/{tenantId}/members/{membershipId}/workspace-access/{workspaceId}";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

    private static async Task<WorkTaskResponse> CreateAssignedTaskAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        string title,
        Guid assigneeMembershipId)
    {
        var response = await client.PostAsJsonAsync(
            TaskPath(tenantId, workspaceId, projectId),
            new CreateWorkTaskRequest(title, null, "Todo", "Normal", null, null, assigneeMembershipId));
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
