using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PTS.Host.Http;
using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectCreationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public ProjectCreationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Project_creation_requires_only_name_and_rejects_duplicate_names()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, $"pc-own-{Guid.NewGuid():N}@example.test", "Owner");
        Authorize(ownerClient, owner.AccessToken);

        var tenant = await CreateTenantAsync(ownerClient, "Create Org");
        var workspace = await CreateWorkspaceAsync(ownerClient, tenant.TenantId, "Projects");

        var created = await CreateProjectAsync(ownerClient, tenant.TenantId, workspace.WorkspaceId, "Van Steel");
        Assert.Equal("Van Steel", created.Name);
        Assert.Equal(ProjectAccentRules.Default, created.AccentToken);
        Assert.Equal(0, created.OpenTaskCount);

        var duplicateName = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("van steel"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);
        Assert.Equal("project_name_conflict", await ReadErrorAsync(duplicateName));

        var withAccent = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Teal Project", accentToken: "teal"));
        withAccent.EnsureSuccessStatusCode();
        var accentProject = await withAccent.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(accentProject);
        Assert.Equal("teal", accentProject.AccentToken);

        var invalidAccent = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects",
            TestProjectFactory.CreateRequest("Bad Accent", accentToken: "purple"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidAccent.StatusCode);
        Assert.Equal("invalid_project_accent", await ReadErrorAsync(invalidAccent));
    }

    [SkippableFact]
    public async Task Task_responses_no_longer_include_key_based_references()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, $"pc-ref-{Guid.NewGuid():N}@example.test", "Owner");
        Authorize(client, owner.AccessToken);

        var tenant = await CreateTenantAsync(client, "Ref Org");
        var workspace = await CreateWorkspaceAsync(client, tenant.TenantId, "Work");
        var project = await CreateProjectAsync(client, tenant.TenantId, workspace.WorkspaceId, "Alpha");

        var task = await CreateTaskAsync(client, tenant.TenantId, workspace.WorkspaceId, project.ProjectId, "First");

        using var json = JsonDocument.Parse(await client.GetStringAsync(
            $"/tenants/{tenant.TenantId}/workspaces/{workspace.WorkspaceId}/projects/{project.ProjectId}/tasks/{task.TaskId}"));
        Assert.False(json.RootElement.TryGetProperty("reference", out _));
    }

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

    private static void Authorize(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"pc-{Guid.NewGuid():N}"[..20]));
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

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }
}

public sealed class ProjectAccentRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("teal", true)]
    [InlineData("TEAL", true)]
    [InlineData("purple", false)]
    public void TryNormalize_accepts_curated_tokens_only(string? token, bool expected)
    {
        var ok = ProjectAccentRules.TryNormalize(token, out _);
        Assert.Equal(expected, ok);
    }

    [Fact]
    public void ResolveOrDefault_uses_indigo_when_unset()
        => Assert.Equal("indigo", ProjectAccentRules.ResolveOrDefault(null));
}
