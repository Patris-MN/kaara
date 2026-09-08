using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

/// <summary>
/// Global notification inbox HTTP tests using the same invite/assign flow as
/// <see cref="TaskAssignmentTagsNotificationsHttpTests"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GlobalNotificationInboxHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public GlobalNotificationInboxHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Global_inbox_aggregates_unread_across_active_memberships()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var userClient = _web.CreateClient();
        var ownerAClient = _web.CreateClient();
        var ownerBClient = _web.CreateClient();
        var ownerCClient = _web.CreateClient();

        var userEmail = Email("inbox-user");
        var user = await RegisterAndLoginAsync(userClient, userEmail, "Inbox User");
        var ownerA = await RegisterAndLoginAsync(ownerAClient, Email("inbox-a"), "Owner A");
        var ownerB = await RegisterAndLoginAsync(ownerBClient, Email("inbox-b"), "Owner B");
        var ownerC = await RegisterAndLoginAsync(ownerCClient, Email("inbox-c"), "Owner C");

        Authorize(userClient, user.AccessToken);
        Authorize(ownerAClient, ownerA.AccessToken);
        Authorize(ownerBClient, ownerB.AccessToken);
        Authorize(ownerCClient, ownerC.AccessToken);

        var tenantA = await CreateTenantAsync(ownerAClient, "Patris", "pat");
        var tenantB = await CreateTenantAsync(ownerBClient, "FastPay", "fp");
        var tenantC = await CreateTenantAsync(ownerCClient, "Quiet Org", "qt");

        await InviteAndAcceptAsync(ownerAClient, userClient, tenantA.TenantId, userEmail);
        await InviteAndAcceptAsync(ownerBClient, userClient, tenantB.TenantId, userEmail);
        await InviteAndAcceptAsync(ownerCClient, userClient, tenantC.TenantId, userEmail);

        var membersA = await ownerAClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantA.TenantId}/members");
        var membersB = await ownerBClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantB.TenantId}/members");
        var membersC = await ownerCClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantC.TenantId}/members");
        var membershipA = Assert.Single(membersA!, item => item.UserId == user.UserId);
        var membershipB = Assert.Single(membersB!, item => item.UserId == user.UserId);
        var membershipC = Assert.Single(membersC!, item => item.UserId == user.UserId);

        var workspaceA = await CreateWorkspaceAsync(ownerAClient, tenantA.TenantId, "Engineering");
        var projectA = await CreateProjectAsync(ownerAClient, tenantA.TenantId, workspaceA.WorkspaceId, "Hospital");
        var workspaceB = await CreateWorkspaceAsync(ownerBClient, tenantB.TenantId, "Backend");
        var projectB = await CreateProjectAsync(ownerBClient, tenantB.TenantId, workspaceB.WorkspaceId, "Payments");
        var workspaceC = await CreateWorkspaceAsync(ownerCClient, tenantC.TenantId, "Solo");
        var projectC = await CreateProjectAsync(ownerCClient, tenantC.TenantId, workspaceC.WorkspaceId, "Internal");

        (await ownerAClient.PutAsJsonAsync(
            AccessPath(tenantA.TenantId, membershipA.MembershipId, workspaceA.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerBClient.PutAsJsonAsync(
            AccessPath(tenantB.TenantId, membershipB.MembershipId, workspaceB.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerCClient.PutAsJsonAsync(
            AccessPath(tenantC.TenantId, membershipC.MembershipId, workspaceC.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        await CreateAssignedTaskAsync(
            ownerAClient, tenantA.TenantId, workspaceA.WorkspaceId, projectA.ProjectId, "Dashboard", membershipA.MembershipId);
        await CreateAssignedTaskAsync(
            ownerAClient, tenantA.TenantId, workspaceA.WorkspaceId, projectA.ProjectId, "Reports", membershipA.MembershipId);
        await CreateAssignedTaskAsync(
            ownerBClient, tenantB.TenantId, workspaceB.WorkspaceId, projectB.ProjectId, "Payment API", membershipB.MembershipId);
        await CreateAssignedTaskAsync(
            ownerCClient, tenantC.TenantId, workspaceC.WorkspaceId, projectC.ProjectId, "Archive", membershipC.MembershipId);

        var preRead = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        var readTargetC = Assert.Single(preRead!.Items, item => item.TaskTitle == "Archive");
        (await userClient.PostAsync($"/notifications/{readTargetC.NotificationId}/read", null)).EnsureSuccessStatusCode();

        var inbox = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        Assert.NotNull(inbox);
        Assert.Equal(4, inbox!.Items.Count);
        Assert.Equal(3, inbox.UnreadCount);
        Assert.Contains(inbox.Items, item => item.TenantName == "Patris" && item.TaskTitle == "Dashboard");
        Assert.Contains(inbox.Items, item => item.TenantName == "FastPay" && item.TaskTitle == "Payment API");
        Assert.True(inbox.Items.Single(item => item.TaskTitle == "Archive").IsRead);

        var readTarget = Assert.Single(inbox.Items, item => item.TaskTitle == "Dashboard");
        (await userClient.PostAsync($"/notifications/{readTarget.NotificationId}/read", null)).EnsureSuccessStatusCode();

        var afterRead = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        Assert.Equal(2, afterRead!.UnreadCount);
    }

    [SkippableFact]
    public async Task Global_inbox_does_not_expose_other_users_notifications()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var userAClient = _web.CreateClient();
        var userBClient = _web.CreateClient();
        var ownerClient = _web.CreateClient();

        var userAEmail = Email("ga");
        var userBEmail = Email("gb");
        var userA = await RegisterAndLoginAsync(userAClient, userAEmail, "User A");
        var userB = await RegisterAndLoginAsync(userBClient, userBEmail, "User B");
        var owner = await RegisterAndLoginAsync(ownerClient, Email("go"), "Owner");

        Authorize(userAClient, userA.AccessToken);
        Authorize(userBClient, userB.AccessToken);
        Authorize(ownerClient, owner.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Shared Org", "sho");
        await InviteAndAcceptAsync(ownerClient, userAClient, tenant.TenantId, userAEmail);
        await InviteAndAcceptAsync(ownerClient, userBClient, tenant.TenantId, userBEmail);

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var membershipA = Assert.Single(members!, item => item.UserId == userA.UserId);

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Shared Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Shared Project");
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, membershipA.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        await CreateAssignedTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "For A only", membershipA.MembershipId);

        var inboxA = await userAClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        var inboxB = await userBClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");

        Assert.Single(inboxA!.Items);
        Assert.Empty(inboxB!.Items);
        Assert.Equal(1, inboxA.UnreadCount);
    }

    [SkippableFact]
    public async Task Suspended_membership_notifications_are_omitted_from_global_inbox()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var userClient = _web.CreateClient();
        var ownerClient = _web.CreateClient();
        var userEmail = Email("susp");
        var user = await RegisterAndLoginAsync(userClient, userEmail, "Suspended User");
        var owner = await RegisterAndLoginAsync(ownerClient, Email("sown"), "Owner");
        Authorize(userClient, user.AccessToken);
        Authorize(ownerClient, owner.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Suspend Org", "sus");
        await InviteAndAcceptAsync(ownerClient, userClient, tenant.TenantId, userEmail);
        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var membership = Assert.Single(members!, item => item.UserId == user.UserId);

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Suspend Space");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Suspend Project");
        (await ownerClient.PutAsJsonAsync(
            AccessPath(tenant.TenantId, membership.MembershipId, workspace.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        await CreateAssignedTaskAsync(
            ownerClient, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "Before suspend", membership.MembershipId);

        Assert.Single((await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications"))!.Items);

        await factory.SetMembershipStatusAsync(user.UserId, tenant.TenantId, MembershipStatus.Suspended);

        var after = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        Assert.Empty(after!.Items);
        Assert.Equal(0, after.UnreadCount);
    }

    [SkippableFact]
    public async Task Mark_read_via_global_endpoint_does_not_cross_memberships()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var userClient = _web.CreateClient();
        var ownerAClient = _web.CreateClient();
        var ownerBClient = _web.CreateClient();
        var userEmail = Email("mr");
        var user = await RegisterAndLoginAsync(userClient, userEmail, "Mark Read User");
        var ownerA = await RegisterAndLoginAsync(ownerAClient, Email("mra"), "Owner A");
        var ownerB = await RegisterAndLoginAsync(ownerBClient, Email("mrb"), "Owner B");
        Authorize(userClient, user.AccessToken);
        Authorize(ownerAClient, ownerA.AccessToken);
        Authorize(ownerBClient, ownerB.AccessToken);

        var tenantA = await CreateTenantAsync(ownerAClient, "Mark A", "mka");
        var tenantB = await CreateTenantAsync(ownerBClient, "Mark B", "mkb");
        await InviteAndAcceptAsync(ownerAClient, userClient, tenantA.TenantId, userEmail);
        await InviteAndAcceptAsync(ownerBClient, userClient, tenantB.TenantId, userEmail);

        var membersA = await ownerAClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantA.TenantId}/members");
        var membersB = await ownerBClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenantB.TenantId}/members");
        var membershipA = Assert.Single(membersA!, item => item.UserId == user.UserId);
        var membershipB = Assert.Single(membersB!, item => item.UserId == user.UserId);

        var workspaceA = await CreateWorkspaceAsync(ownerAClient, tenantA.TenantId, "A Space");
        var projectA = await CreateProjectAsync(ownerAClient, tenantA.TenantId, workspaceA.WorkspaceId, "A Project");
        var workspaceB = await CreateWorkspaceAsync(ownerBClient, tenantB.TenantId, "B Space");
        var projectB = await CreateProjectAsync(ownerBClient, tenantB.TenantId, workspaceB.WorkspaceId, "B Project");

        (await ownerAClient.PutAsJsonAsync(
            AccessPath(tenantA.TenantId, membershipA.MembershipId, workspaceA.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();
        (await ownerBClient.PutAsJsonAsync(
            AccessPath(tenantB.TenantId, membershipB.MembershipId, workspaceB.WorkspaceId),
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        await CreateAssignedTaskAsync(
            ownerAClient, tenantA.TenantId, workspaceA.WorkspaceId, projectA.ProjectId, "A Task", membershipA.MembershipId);
        await CreateAssignedTaskAsync(
            ownerBClient, tenantB.TenantId, workspaceB.WorkspaceId, projectB.ProjectId, "B Task", membershipB.MembershipId);

        var inbox = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        var noteA = Assert.Single(inbox!.Items, item => item.TaskTitle == "A Task");
        var noteB = Assert.Single(inbox.Items, item => item.TaskTitle == "B Task");

        (await userClient.PostAsync($"/notifications/{noteA.NotificationId}/read", null)).EnsureSuccessStatusCode();

        var refreshed = await userClient.GetFromJsonAsync<GlobalNotificationInboxResponse>("/notifications");
        Assert.True(refreshed!.Items.Single(item => item.NotificationId == noteA.NotificationId).IsRead);
        Assert.False(refreshed.Items.Single(item => item.NotificationId == noteB.NotificationId).IsRead);
        Assert.Equal(1, refreshed.UnreadCount);
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static string TaskPath(Guid tenantId, Guid workspaceId, Guid projectId)
        => $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks";

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
            TestProjectFactory.CreateRequest(name));
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
            new CreateWorkTaskRequest(title, null, "Open", "Normal", null, null, assigneeMembershipId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkTaskResponse>())!;
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
