using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.Tenancy;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public AuthenticationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Unauthenticated_request_to_me_is_rejected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var response = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Register_and_login_resolve_to_the_same_user()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"auth-a-{Guid.NewGuid():N}@example.test";
        var register = await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", "User A"));
        register.EnsureSuccessStatusCode();
        var created = await register.Content.ReadFromJsonAsync<AuthUserResponse>();
        Assert.NotNull(created);

        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(tokens);
        Assert.Equal(created.UserId, tokens.UserId);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<AuthUserResponse>("/auth/me");
        Assert.Equal(created.UserId, me!.UserId);
        Assert.Equal(created.Email, me.Email);
        Assert.Equal(created.DisplayName, me.DisplayName);
        Assert.False(created.IsPlatformAdministrator);
        Assert.False(tokens.IsPlatformAdministrator);
        Assert.False(me.IsPlatformAdministrator);
    }

    [SkippableFact]
    public async Task Invalid_password_is_rejected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"auth-bad-{Guid.NewGuid():N}@example.test";
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", "User"))).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "wrong-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [SkippableFact]
    public async Task Forged_bearer_token_is_rejected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real.jwt.token");
        var response = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Full_chain_authenticated_user_can_only_read_own_tenant_isolation_records()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var factory = _postgres.Services.GetRequiredService<TestDataFactory>();
        var clientA = _web.CreateClient();
        var clientB = _web.CreateClient();
        var emailA = $"chain-a-{Guid.NewGuid():N}@example.test";
        var emailB = $"chain-b-{Guid.NewGuid():N}@example.test";
        var userA = await RegisterAndLoginAsync(clientA, emailA, "User A");
        var userB = await RegisterAndLoginAsync(clientB, emailB, "User B");

        var tenantA = await factory.CreateTenantAsync("ChainA");
        var tenantB = await factory.CreateTenantAsync("ChainB");
        await factory.CreateActiveMembershipAsync(userA.UserId, tenantA);
        await factory.CreateActiveMembershipAsync(userB.UserId, tenantB);

        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userA.AccessToken);

        var created = await clientA.PostAsJsonAsync(
            $"/tenants/{tenantA}/isolation-records",
            new CreateIsolationRecordRequest("chain-record-A"));
        created.EnsureSuccessStatusCode();

        var listA = await clientA.GetFromJsonAsync<IsolationRecordResponse[]>($"/tenants/{tenantA}/isolation-records");
        Assert.NotNull(listA);
        Assert.Contains(listA, r => r.Value == "chain-record-A");

        var cross = await clientA.GetAsync($"/tenants/{tenantB}/isolation-records");
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

}

public sealed class PtsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:Jwt:SigningKey", "PTS-DEV-ONLY-NOT-FOR-PRODUCTION-256BIT");
        builder.UseSetting("Authentication:Jwt:Issuer", "pts-dev");
        builder.UseSetting("Authentication:Jwt:Audience", "pts-api-dev");
        builder.UseSetting("PTS_BOOTSTRAP_PLATFORM_ADMIN_EMAIL", "");
        builder.UseSetting("PTS_BOOTSTRAP_PLATFORM_ADMIN_PASSWORD", "");
        builder.UseSetting("PTS_BOOTSTRAP_PLATFORM_ADMIN_DISPLAY_NAME", "");
    }
}
