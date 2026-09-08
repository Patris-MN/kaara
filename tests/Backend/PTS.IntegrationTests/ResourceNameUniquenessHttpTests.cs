using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ResourceNameUniquenessHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public ResourceNameUniquenessHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Workspace_names_are_unique_case_insensitively_within_tenant()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, Email("ws-uniq"), "Workspace User");
        Authorize(client, user.AccessToken);

        var tenant = await CreateTenantOkAsync(client, "Patris", "pat");
        var first = await CreateWorkspaceAsync(client, tenant.TenantId, "Development");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        foreach (var duplicate in new[] { "development", "DEVELOPMENT", " Development " })
        {
            var conflict = await CreateWorkspaceAsync(client, tenant.TenantId, duplicate);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("workspace_name_conflict", await ReadErrorCodeAsync(conflict));
        }
    }

    [SkippableFact]
    public async Task Workspace_same_name_is_allowed_in_different_tenants()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var clientA = _web.CreateClient();
        var clientB = _web.CreateClient();
        var userA = await RegisterAndLoginAsync(clientA, Email("ws-a"), "User A");
        var userB = await RegisterAndLoginAsync(clientB, Email("ws-b"), "User B");
        Authorize(clientA, userA.AccessToken);
        Authorize(clientB, userB.AccessToken);

        var tenantA = await CreateTenantOkAsync(clientA, "Patris", "pat");
        var tenantB = await CreateTenantOkAsync(clientB, "FastPay", "fp");

        Assert.Equal(HttpStatusCode.Created, (await CreateWorkspaceAsync(clientA, tenantA.TenantId, "Development")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateWorkspaceAsync(clientB, tenantB.TenantId, "Development")).StatusCode);
    }

    [SkippableFact]
    public async Task Workspace_rename_rejects_conflicts_but_allows_same_resource_casing_change()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, Email("ws-ren"), "Rename User");
        Authorize(client, user.AccessToken);

        var tenant = await CreateTenantOkAsync(client, "Rename Org", "ren");
        var developmentResponse = await CreateWorkspaceAsync(client, tenant.TenantId, "Development");
        var marketingResponse = await CreateWorkspaceAsync(client, tenant.TenantId, "Marketing");
        var dev = await developmentResponse.Content.ReadFromJsonAsync<WorkspaceResponse>();
        var marketing = await marketingResponse.Content.ReadFromJsonAsync<WorkspaceResponse>();

        var conflict = await client.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{marketing!.WorkspaceId}",
            new UpdateWorkspaceRequest("DEVELOPMENT", null, null));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var allowed = await client.PutAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{dev!.WorkspaceId}",
            new UpdateWorkspaceRequest("DEVELOPMENT", null, null));
        allowed.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Project_names_are_unique_case_insensitively_within_workspace()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, Email("pr-uniq"), "Project User");
        Authorize(client, user.AccessToken);

        var tenant = await CreateTenantOkAsync(client, "Project Org", "pro");
        var workspaceA = await CreateWorkspaceAsync(client, tenant.TenantId, "Engineering");
        var workspaceB = await CreateWorkspaceAsync(client, tenant.TenantId, "Marketing");
        var wsA = await workspaceA.Content.ReadFromJsonAsync<WorkspaceResponse>();
        var wsB = await workspaceB.Content.ReadFromJsonAsync<WorkspaceResponse>();

        Assert.Equal(HttpStatusCode.Created, (await CreateProjectAsync(client, tenant.TenantId, wsA!.WorkspaceId, "Website")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateProjectAsync(client, tenant.TenantId, wsA.WorkspaceId, "WEBSITE")).StatusCode);
        var crossWorkspace = await client.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{wsB!.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Website"));
        Assert.Equal(HttpStatusCode.Created, crossWorkspace.StatusCode);
    }

    [SkippableFact]
    public async Task Organization_names_are_unique_case_insensitively_for_the_same_user()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, Email("org-uniq"), "Org User");
        Authorize(client, user.AccessToken);

        Assert.Equal(HttpStatusCode.Created, (await PostTenantAsync(client, "Patris", "pat1")).StatusCode);
        var conflict = await PostTenantAsync(client, "patris", "pat2");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("organization_name_conflict", await ReadErrorCodeAsync(conflict));
    }

    [SkippableFact]
    public async Task Different_users_may_create_same_organization_display_name()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var clientA = _web.CreateClient();
        var clientB = _web.CreateClient();
        var userA = await RegisterAndLoginAsync(clientA, Email("oa"), "Org A");
        var userB = await RegisterAndLoginAsync(clientB, Email("ob"), "Org B");
        Authorize(clientA, userA.AccessToken);
        Authorize(clientB, userB.AccessToken);

        Assert.Equal(HttpStatusCode.Created, (await PostTenantAsync(clientA, "Patris", "pata")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostTenantAsync(clientB, "Patris", "patb")).StatusCode);
    }

    [SkippableFact]
    public async Task Concurrent_workspace_creates_cannot_bypass_unique_index()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var user = await RegisterAndLoginAsync(client, Email("ws-race"), "Race User");
        Authorize(client, user.AccessToken);
        var tenant = await CreateTenantOkAsync(client, "Race Org", "race");

        var tasks = Enumerable.Range(0, 2)
            .Select(_ => CreateWorkspaceAsync(client, tenant.TenantId, "Development"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, responses.Count(item => item.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(item => item.StatusCode == HttpStatusCode.Conflict));
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName)))
            .EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static Task<HttpResponseMessage> PostTenantAsync(HttpClient client, string name, string slugPrefix)
        => client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"{slugPrefix}-{Guid.NewGuid():N}"[..20]));

    private static async Task<TenantResponse> CreateTenantOkAsync(HttpClient client, string name, string slugPrefix)
    {
        var response = await PostTenantAsync(client, name, slugPrefix);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }

    private static Task<HttpResponseMessage> CreateWorkspaceAsync(HttpClient client, Guid tenantId, string name)
        => client.PostAsJsonAsync($"/tenants/{tenantId}/workspaces", new CreateWorkspaceRequest(name));

    private static Task<HttpResponseMessage> CreateProjectAsync(
        HttpClient client,
        Guid tenantId,
        Guid workspaceId,
        string name)
        => client.PostAsJsonAsync(
            $"/tenants/{tenantId}/workspaces/{workspaceId}/projects",
            TestProjectFactory.CreateRequest(name));

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}
