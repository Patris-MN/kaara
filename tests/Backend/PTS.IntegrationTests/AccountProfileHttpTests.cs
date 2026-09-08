using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AccountProfileHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public AccountProfileHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Zero_membership_user_can_get_profile()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"profile-zero-{Guid.NewGuid():N}@example.test";
        var login = await RegisterAndLoginAsync(client, email, "Zero Org User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var profile = await client.GetFromJsonAsync<AccountProfileResponse>("/account/profile");
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email);
        Assert.Equal("Zero Org User", profile.DisplayName);
    }

    [SkippableFact]
    public async Task Unauthenticated_profile_request_is_rejected()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var response = await client.GetAsync("/account/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Authenticated_user_can_read_and_update_own_profile()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"profile-{Guid.NewGuid():N}@example.test";
        var login = await RegisterAndLoginAsync(client, email, "User Profile");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var profile = await client.GetFromJsonAsync<AccountProfileResponse>("/account/profile");
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email);
        Assert.Equal("User Profile", profile.DisplayName);
        Assert.True(profile.HasLocalCredential);

        var update = await client.PatchAsJsonAsync("/account/profile", new UpdateAccountProfileRequest("Updated Name"));
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<AccountProfileResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.DisplayName);

        var me = await client.GetFromJsonAsync<AuthUserResponse>("/auth/me");
        Assert.Equal("Updated Name", me!.DisplayName);
    }

    [SkippableFact]
    public async Task Change_password_requires_current_password()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var email = $"profile-pw-{Guid.NewGuid():N}@example.test";
        var login = await RegisterAndLoginAsync(client, email, "Password User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var wrong = await client.PostAsJsonAsync(
            "/account/change-password",
            new ChangePasswordRequest("wrong-password", "new-password-123"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var change = await client.PostAsJsonAsync(
            "/account/change-password",
            new ChangePasswordRequest("correct-horse", "new-password-123"));
        change.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;
        var oldLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        var newLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "new-password-123"));
        newLogin.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Auth_providers_reports_google_unconfigured_in_tests()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var providers = await client.GetFromJsonAsync<AuthProvidersResponse>("/auth/providers");
        Assert.NotNull(providers);
        Assert.False(providers.Google.Available);
    }

    private static async Task<LoginResponse> RegisterAndLoginAsync(HttpClient client, string email, string displayName)
    {
        (await client.PostAsJsonAsync("/auth/register", new RegisterRequest(email, "correct-horse", displayName))).EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "correct-horse"));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}

public sealed record AuthProvidersResponse(GoogleAuthProviderResponse Google);
