using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectMetadataUpdateHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public ProjectMetadataUpdateHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_and_admin_can_update_project_metadata_fields()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pmu-own"), "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, Email("pmu-adm"), "Admin");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Metadata Org");
        await factory.CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Projects");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Alpha");

        var ownerPatch = await PatchProjectAsync(
            ownerClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Alpha Renamed", "Updated description", "teal"));
        ownerPatch.EnsureSuccessStatusCode();
        var ownerUpdated = await ownerPatch.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(ownerUpdated);
        Assert.Equal("Alpha Renamed", ownerUpdated.Name);
        Assert.Equal("Updated description", ownerUpdated.Description);
        Assert.Equal("teal", ownerUpdated.AccentToken);

        var adminPatch = await PatchProjectAsync(
            adminClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Admin Updated", null, "rose"));
        adminPatch.EnsureSuccessStatusCode();
        var adminUpdated = await adminPatch.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(adminUpdated);
        Assert.Equal("Admin Updated", adminUpdated.Name);
        Assert.Null(adminUpdated.Description);
        Assert.Equal("rose", adminUpdated.AccentToken);
    }

    [SkippableFact]
    public async Task Member_with_workspace_edit_can_create_but_not_update_project_metadata()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pmu-mem-own"), "Owner");
        var memberEmail = Email("pmu-mem");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Member");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Member Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Shared");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("Edit"))).EnsureSuccessStatusCode();

        var memberCreate = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Member Created"));
        memberCreate.EnsureSuccessStatusCode();

        var memberPatch = await PatchProjectAsync(
            memberClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Blocked Rename", "Nope", "teal"));
        Assert.Equal(HttpStatusCode.Forbidden, memberPatch.StatusCode);
        Assert.Equal("project_metadata_edit_forbidden", await ReadErrorAsync(memberPatch));
    }

    [SkippableFact]
    public async Task Member_with_workspace_view_cannot_update_project_metadata()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var memberClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pmu-view-own"), "Owner");
        var memberEmail = Email("pmu-view");
        var member = await RegisterAndLoginAsync(memberClient, memberEmail, "Viewer");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(memberClient, member.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "View Org");
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new InviteMemberRequest(memberEmail));
        await memberClient.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations/accept", new { });

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Read");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Readonly");

        var members = await ownerClient.GetFromJsonAsync<TenantMemberResponse[]>($"/tenants/{tenant.TenantId}/members");
        var memberRecord = Assert.Single(members!, item => item.UserId == member.UserId);

        (await ownerClient.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/members/{memberRecord.MembershipId}/workspace-access/{workspace.WorkspaceId}",
            new SetWorkspaceAccessRequest("View"))).EnsureSuccessStatusCode();

        var memberPatch = await PatchProjectAsync(
            memberClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Blocked", "Nope", "teal"));
        Assert.Equal(HttpStatusCode.Forbidden, memberPatch.StatusCode);
        Assert.Equal("project_metadata_edit_forbidden", await ReadErrorAsync(memberPatch));
    }

    [SkippableFact]
    public async Task Duplicate_project_names_are_rejected_but_same_project_can_keep_its_name()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, Email("pmu-dup"), "Owner");
        Authorize(client, owner.AccessToken);

        var tenant = await CreateTenantAsync(client, "Dup Org");
        var workspace = await CreateWorkspaceAsync(client, tenant.TenantId, "Dup");
        var alpha = await CreateProjectAsync(client, tenant.TenantId, workspace.WorkspaceId, "Alpha");
        await CreateProjectAsync(client, tenant.TenantId, workspace.WorkspaceId, "Beta");

        var duplicate = await PatchProjectAsync(
            client,
            tenant.TenantId,
            workspace.WorkspaceId,
            alpha.ProjectId,
            new UpdateProjectRequest("beta", "Conflict", "teal"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("project_name_conflict", await ReadErrorAsync(duplicate));

        var unchanged = await PatchProjectAsync(
            client,
            tenant.TenantId,
            workspace.WorkspaceId,
            alpha.ProjectId,
            new UpdateProjectRequest("Alpha", "Same name save", "teal"));
        unchanged.EnsureSuccessStatusCode();
        var saved = await unchanged.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(saved);
        Assert.Equal("Alpha", saved.Name);
        Assert.Equal("Same name save", saved.Description);
    }

    [SkippableFact]
    public async Task Cross_tenant_update_is_rejected_and_internal_fields_remain_unchanged()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var strangerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pmu-x-own"), "Owner");
        var stranger = await RegisterAndLoginAsync(strangerClient, Email("pmu-x-str"), "Stranger");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(strangerClient, stranger.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Local Org");
        var foreignTenant = await CreateTenantAsync(strangerClient, "Foreign Org");
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Local");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Protected");
        var foreignWorkspace = await CreateWorkspaceAsync(strangerClient, foreignTenant.TenantId, "Foreign");

        var crossTenant = await PatchProjectAsync(
            strangerClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Stolen", "Nope", "teal"));
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var crossWorkspace = await PatchProjectAsync(
            ownerClient,
            tenant.TenantId,
            foreignWorkspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Wrong workspace", null, "teal"));
        Assert.Equal(HttpStatusCode.NotFound, crossWorkspace.StatusCode);

        var craftedBody = JsonSerializer.Serialize(new
        {
            name = "Crafted",
            description = "Still local",
            accentToken = "teal",
            tenantId = foreignTenant.TenantId,
            workspaceId = foreignWorkspace.WorkspaceId,
            id = Guid.NewGuid(),
        });
        var crafted = await ownerClient.PatchAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects/{project.ProjectId}",
            new StringContent(craftedBody, Encoding.UTF8, "application/json"));
        crafted.EnsureSuccessStatusCode();
        var craftedProject = await crafted.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(craftedProject);
        Assert.Equal("Crafted", craftedProject.Name);
        Assert.Equal(tenant.TenantId, craftedProject.TenantId);
        Assert.Equal(workspace.WorkspaceId, craftedProject.WorkspaceId);
        Assert.Equal(project.ProjectId, craftedProject.ProjectId);
    }

    [SkippableFact]
    public async Task Suspended_and_removed_members_cannot_update_project_metadata()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var ownerClient = _web.CreateClient();
        var adminClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("pmu-sus-own"), "Owner");
        var admin = await RegisterAndLoginAsync(adminClient, Email("pmu-sus-adm"), "Admin");
        Authorize(ownerClient, owner.AccessToken);
        Authorize(adminClient, admin.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Status Org");
        await factory.CreateActiveMembershipAsync(admin.UserId, tenant.TenantId, MembershipRole.Admin);

        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Ops");
        var project = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Ops Project");

        var activePatch = await PatchProjectAsync(
            adminClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Admin Active Save", null, "teal"));
        activePatch.EnsureSuccessStatusCode();

        await factory.SetMembershipStatusAsync(admin.UserId, tenant.TenantId, MembershipStatus.Suspended);

        var suspendedPatch = await PatchProjectAsync(
            adminClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Suspended", null, "teal"));
        Assert.Equal(HttpStatusCode.Forbidden, suspendedPatch.StatusCode);

        await factory.SetMembershipStatusAsync(admin.UserId, tenant.TenantId, MembershipStatus.Removed);
        var removedPatch = await PatchProjectAsync(
            adminClient,
            tenant.TenantId,
            workspace.WorkspaceId,
            project.ProjectId,
            new UpdateProjectRequest("Removed", null, "teal"));
        Assert.True(
            removedPatch.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Unexpected status: {removedPatch.StatusCode}");
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<(string AccessToken, Guid UserId)> RegisterAndLoginAsync(
        HttpClient client,
        string email,
        string displayName)
    {
        var register = await client.PostAsJsonAsync("/auth/register", new { email, password = "Password1!", displayName });
        register.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password = "Password1!" });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("accessToken").GetString()!, doc.RootElement.GetProperty("userId").GetGuid());
    }

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"pmu-{Guid.NewGuid():N}"[..20]));
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

    private static Task<HttpResponseMessage> PatchProjectAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        UpdateProjectRequest request)
        => client.PatchAsJsonAsync(
            $"/tenants/{tenantId}/workspaces/{workspaceId}/projects/{projectId}",
            request);

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
