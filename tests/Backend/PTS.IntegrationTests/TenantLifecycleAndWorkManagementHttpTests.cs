using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TenantLifecycleAndWorkManagementHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public TenantLifecycleAndWorkManagementHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Unauthenticated_create_tenant_is_rejected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var response = await client.PostAsJsonAsync("/tenants", new CreateTenantRequest("Acme", $"acme-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Full_chain_register_create_tenant_workspace_project_and_cross_tenant_idor_is_denied()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var clientA = _web.CreateClient();
        var clientB = _web.CreateClient();
        var userA = await RegisterAndLoginAsync(clientA, $"lc-a-{Guid.NewGuid():N}@example.test", "User A");
        var userB = await RegisterAndLoginAsync(clientB, $"lc-b-{Guid.NewGuid():N}@example.test", "User B");

        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userB.AccessToken);

        var slug = $"org-{Guid.NewGuid():N}"[..20];
        var createdTenant = await clientA.PostAsJsonAsync("/tenants", new CreateTenantRequest("Org A", slug));
        createdTenant.EnsureSuccessStatusCode();
        var tenant = await createdTenant.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        var duplicate = await clientA.PostAsJsonAsync("/tenants", new CreateTenantRequest("Org A2", slug));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var ws = await clientA.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces",
            new CreateWorkspaceRequest("Workspace A", TenantId: tenant.TenantId));
        ws.EnsureSuccessStatusCode();
        var workspace = await ws.Content.ReadFromJsonAsync<WorkspaceResponse>();
        Assert.NotNull(workspace);
        Assert.Equal(tenant.TenantId, workspace.TenantId);

        var pr = await clientA.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects",
            new CreateProjectRequest("Project A", TenantId: Guid.NewGuid()));
        pr.EnsureSuccessStatusCode();
        var project = await pr.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);

        var listedWs = await clientA.GetFromJsonAsync<WorkspaceResponse[]>($"/tenants/{tenant.TenantId}/workspaces");
        Assert.Contains(listedWs!, w => w.WorkspaceId == workspace.WorkspaceId);

        var listedPr = await clientA.GetFromJsonAsync<ProjectResponse[]>(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects");
        Assert.Contains(listedPr!, p => p.ProjectId == project.ProjectId);

        Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/tenants/{tenant.TenantId}/workspaces")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await clientB.GetAsync($"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await clientB.PostAsJsonAsync(
                $"/tenants/{tenant.TenantId}/workspaces",
                new CreateWorkspaceRequest("Hijack", workspace.TenantId))).StatusCode);
    }

    [SkippableFact]
    public async Task Member_cannot_invite_and_user_cannot_accept_someone_elses_invitation()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var clientOwner = _web.CreateClient();
        var clientMember = _web.CreateClient();
        var clientStranger = _web.CreateClient();

        var memberEmail = $"mem-{Guid.NewGuid():N}@example.test";
        var strangerEmail = $"str-{Guid.NewGuid():N}@example.test";
        var owner = await RegisterAndLoginAsync(clientOwner, $"own-{Guid.NewGuid():N}@example.test", "Owner");
        var member = await RegisterAndLoginAsync(clientMember, memberEmail, "Member");
        var stranger = await RegisterAndLoginAsync(clientStranger, strangerEmail, "Stranger");

        clientOwner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        clientMember.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", member.AccessToken);
        clientStranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stranger.AccessToken);

        var slug = $"inv-{Guid.NewGuid():N}"[..20];
        var tenantResponse = await clientOwner.PostAsJsonAsync("/tenants", new CreateTenantRequest("Invite Org", slug));
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        var inviteMember = await clientOwner.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        inviteMember.EnsureSuccessStatusCode();

        var stolenAccept = await clientStranger.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { });
        Assert.Equal(HttpStatusCode.Forbidden, stolenAccept.StatusCode);

        var accept = await clientMember.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations/accept",
            new { });
        accept.EnsureSuccessStatusCode();

        var memberInvite = await clientMember.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(strangerEmail));
        Assert.Equal(HttpStatusCode.Forbidden, memberInvite.StatusCode);

        await factory.CreateMembershipAsync(stranger.UserId, tenant.TenantId, MembershipStatus.Suspended, MembershipRole.Admin);
        var suspendedInvite = await clientStranger.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        Assert.Equal(HttpStatusCode.Forbidden, suspendedInvite.StatusCode);
    }

    [SkippableFact]
    public async Task Invited_and_unrelated_users_cannot_create_workspaces()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var clientOwner = _web.CreateClient();
        var clientInvited = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(clientOwner, $"iown-{Guid.NewGuid():N}@example.test", "Owner");
        var invitedEmail = $"iinv-{Guid.NewGuid():N}@example.test";
        var invited = await RegisterAndLoginAsync(clientInvited, invitedEmail, "Invited");
        clientOwner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        clientInvited.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invited.AccessToken);

        var tenantResponse = await clientOwner.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Invited Org", $"iorg-{Guid.NewGuid():N}"[..20]));
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        (await clientOwner.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(invitedEmail))).EnsureSuccessStatusCode();

        var invitedCreate = await clientInvited.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces",
            new CreateWorkspaceRequest("Nope"));
        Assert.Equal(HttpStatusCode.Forbidden, invitedCreate.StatusCode);
    }

    [SkippableFact]
    public async Task Tenant_list_returns_only_the_caller_active_memberships()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var clientA = _web.CreateClient();
        var clientB = _web.CreateClient();
        var userA = await RegisterAndLoginAsync(clientA, $"list-a-{Guid.NewGuid():N}@example.test", "User A");
        var userB = await RegisterAndLoginAsync(clientB, $"list-b-{Guid.NewGuid():N}@example.test", "User B");
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userB.AccessToken);

        var tenantA = await clientA.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("List A", $"lista-{Guid.NewGuid():N}"[..20]));
        tenantA.EnsureSuccessStatusCode();
        var createdA = await tenantA.Content.ReadFromJsonAsync<TenantResponse>();

        var tenantB = await clientB.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("List B", $"listb-{Guid.NewGuid():N}"[..20]));
        tenantB.EnsureSuccessStatusCode();
        var createdB = await tenantB.Content.ReadFromJsonAsync<TenantResponse>();

        var listedA = await clientA.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        var listedB = await clientB.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        Assert.Contains(listedA!, t => t.TenantId == createdA!.TenantId);
        Assert.DoesNotContain(listedA!, t => t.TenantId == createdB!.TenantId);
        Assert.Contains(listedB!, t => t.TenantId == createdB.TenantId);
        Assert.DoesNotContain(listedB!, t => t.TenantId == createdA.TenantId);

        var strangerTenant = await factory.CreateTenantAsync("Stranger");
        await factory.CreateMembershipAsync(userA.UserId, strangerTenant, MembershipStatus.Suspended);
        var afterSuspend = await clientA.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        Assert.DoesNotContain(afterSuspend!, t => t.TenantId == strangerTenant);
    }

    [SkippableFact]
    public async Task Pending_invitations_are_visible_only_to_the_invitee()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var clientOwner = _web.CreateClient();
        var clientInvited = _web.CreateClient();
        var clientStranger = _web.CreateClient();
        var invitedEmail = $"pinv-{Guid.NewGuid():N}@example.test";
        var owner = await RegisterAndLoginAsync(clientOwner, $"pown-{Guid.NewGuid():N}@example.test", "Owner");
        var invited = await RegisterAndLoginAsync(clientInvited, invitedEmail, "Invited");
        var stranger = await RegisterAndLoginAsync(clientStranger, $"pstr-{Guid.NewGuid():N}@example.test", "Stranger");
        clientOwner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        clientInvited.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invited.AccessToken);
        clientStranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stranger.AccessToken);

        var tenantResponse = await clientOwner.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest("Pending Org", $"pend-{Guid.NewGuid():N}"[..20]));
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(tenant);

        (await clientOwner.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(invitedEmail))).EnsureSuccessStatusCode();

        var ownerTenants = await clientOwner.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        Assert.Contains(ownerTenants!, t => t.TenantId == tenant.TenantId);

        var invitedTenants = await clientInvited.GetFromJsonAsync<TenantMembershipResponse[]>("/tenants");
        Assert.DoesNotContain(invitedTenants!, t => t.TenantId == tenant.TenantId);

        var invitedInbox = await clientInvited.GetFromJsonAsync<TenantMembershipResponse[]>("/invitations");
        Assert.Contains(invitedInbox!, t => t.TenantId == tenant.TenantId && t.Status == "Invited");

        var strangerInbox = await clientStranger.GetFromJsonAsync<TenantMembershipResponse[]>("/invitations");
        Assert.DoesNotContain(strangerInbox!, t => t.TenantId == tenant.TenantId);

        var unauthenticated = _web.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync("/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync("/invitations")).StatusCode);
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
