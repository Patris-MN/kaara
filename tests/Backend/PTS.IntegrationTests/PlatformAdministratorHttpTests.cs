using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Http;
using PTS.Modules.PlatformAdministration;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PlatformAdministratorHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public PlatformAdministratorHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Granting_platform_administrator_is_reflected_on_me_and_is_not_a_user_column()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"platform-admin-{Guid.NewGuid():N}@example.test";
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", "Ops"))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(tokens.IsPlatformAdministrator);

        using (var scope = _web.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPlatformAdministratorStore>();
            await store.EnsureAsync(tokens.UserId);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<AuthUserResponse>("/auth/me");
        Assert.True(me!.IsPlatformAdministrator);
        Assert.Equal(tokens.UserId, me.UserId);
    }
}
