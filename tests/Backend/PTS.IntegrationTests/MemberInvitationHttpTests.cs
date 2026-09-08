using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PTS.Host.Http;

namespace PTS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MemberInvitationHttpTests : IClassFixture<PtsWebApplicationFactory>
{
    private readonly PtsWebApplicationFactory _web;
    private readonly PostgresFixture _postgres;

    public MemberInvitationHttpTests(PtsWebApplicationFactory web, PostgresFixture postgres)
    {
        _web = web;
        _postgres = postgres;
    }

    [SkippableFact]
    public async Task Owner_can_invite_new_email_and_accept_via_token_after_register()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, Email("own"), "Owner");
        Authorize(client, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(client, "Patris");

        var email = Email("new");
        var invite = await client.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email, role = "Member" });
        invite.EnsureSuccessStatusCode();
        var created = await invite.Content.ReadFromJsonAsync<InvitationCreatedResponse>();
        Assert.NotNull(created);
        Assert.NotNull(created!.InvitationUrl);
        var token = created.InvitationUrl!.Split('/').Last();

        var preview = await client.GetAsync($"/invite/{token}");
        preview.EnsureSuccessStatusCode();

        var registerAccept = await client.PostAsJsonAsync(
            $"/invite/{token}/register",
            new RegisterAndAcceptInvitationRequest("Sara User", "correct-horse"));
        registerAccept.EnsureSuccessStatusCode();

        var previewAgain = await client.GetAsync($"/invite/{token}");
        Assert.Equal(HttpStatusCode.Conflict, previewAgain.StatusCode);
    }

    [SkippableFact]
    public async Task Existing_user_must_match_invited_email_on_token_accept()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("own2"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(ownerClient, "Org");

        var inviteeEmail = Email("sara");
        var invitee = await RegisterAndLoginAsync(_web.CreateClient(), inviteeEmail, "Sara");
        var stranger = await RegisterAndLoginAsync(_web.CreateClient(), Email("john"), "John");

        var invite = await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email = inviteeEmail, role = "Member" });
        invite.EnsureSuccessStatusCode();
        var created = await invite.Content.ReadFromJsonAsync<InvitationCreatedResponse>();
        var token = created!.InvitationUrl!.Split('/').Last();

        var strangerClient = _web.CreateClient();
        Authorize(strangerClient, stranger.AccessToken);
        var wrong = await strangerClient.PostAsync($"/invite/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        var inviteeClient = _web.CreateClient();
        Authorize(inviteeClient, invitee.AccessToken);
        var accept = await inviteeClient.PostAsync($"/invite/{token}/accept", null);
        accept.EnsureSuccessStatusCode();
    }

    [SkippableFact]
    public async Task Member_cannot_invite_and_cross_tenant_member_list_is_forbidden()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var ownerClient = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient, Email("own3"), "Owner");
        Authorize(ownerClient, owner.AccessToken);
        var tenantA = await CreateTenantOkAsync(ownerClient, "A");

        var memberClient = _web.CreateClient();
        var member = await RegisterAndLoginAsync(memberClient, Email("mem"), "Member");
        Authorize(ownerClient, owner.AccessToken);
        await ownerClient.PostAsJsonAsync(
            $"/tenants/{tenantA.TenantId}/invitations",
            new { email = member.Email, role = "Member" });
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenantA.TenantId}/invitations/accept", null);

        var inviteForbidden = await memberClient.PostAsJsonAsync(
            $"/tenants/{tenantA.TenantId}/invitations",
            new { email = Email("x"), role = "Member" });
        Assert.Equal(HttpStatusCode.Forbidden, inviteForbidden.StatusCode);

        var strangerClient = _web.CreateClient();
        var stranger = await RegisterAndLoginAsync(strangerClient, Email("str"), "Stranger");
        Authorize(strangerClient, stranger.AccessToken);
        await CreateTenantOkAsync(strangerClient, "B");

        var cross = await strangerClient.GetAsync($"/tenants/{tenantA.TenantId}/members");
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);
    }

    [SkippableFact]
    public async Task Duplicate_active_member_invite_returns_conflict()
    {
        Skip.IfNot(_postgres.DatabaseAvailable, _postgres.UnavailableReason);

        var client = _web.CreateClient();
        var owner = await RegisterAndLoginAsync(client, Email("own4"), "Owner");
        Authorize(client, owner.AccessToken);
        var tenant = await CreateTenantOkAsync(client, "Org");

        var email = Email("active");
        var memberClient = _web.CreateClient();
        var member = await RegisterAndLoginAsync(memberClient, email, "Member");
        await client.PostAsJsonAsync($"/tenants/{tenant.TenantId}/invitations", new { email, role = "Member" });
        Authorize(memberClient, member.AccessToken);
        await memberClient.PostAsync($"/tenants/{tenant.TenantId}/invitations/accept", null);

        Authorize(client, owner.AccessToken);
        var dup = await client.PostAsJsonAsync(
            $"/tenants/{tenant.TenantId}/invitations",
            new { email, role = "Member" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
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

    private static async Task<TenantResponse> CreateTenantOkAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/tenants",
            new CreateTenantRequest(name, $"t-{Guid.NewGuid():N}"[..12]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantResponse>())!;
    }
}

internal sealed record InvitationCreatedResponse(
    Guid InvitationId,
    string InvitedEmail,
    DateTimeOffset ExpiresAtUtc,
    string? InvitationUrl,
    bool EmailDeliveryDeferred);
