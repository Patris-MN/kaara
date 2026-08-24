using PTS.Host.Authentication;
using PTS.Modules.Identity;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapGet("/me", GetMeAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IUserAuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await authentication.RegisterAsync(
                request.Email, request.Password, request.DisplayName, cancellationToken);
            return Results.Created("/auth/me", new AuthUserResponse(user.Id, user.Email, user.DisplayName));
        }
        catch (DuplicateEmailException)
        {
            return Results.Conflict(new { error = "email_already_registered" });
        }
        catch (ArgumentException ex)
        {
            var error = ex.ParamName switch
            {
                "email" => "invalid_email",
                "password" => "invalid_password",
                "displayName" => "invalid_display_name",
                _ => "invalid_registration",
            };
            return Results.BadRequest(new { error });
        }
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IUserAuthenticationService authentication,
        JwtAccessTokenIssuer tokens,
        CancellationToken cancellationToken)
    {
        var user = await authentication.AuthenticateAsync(request.Email, request.Password, cancellationToken);
        if (user is null)
        {
            return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var accessToken = tokens.Issue(user, out var expiresAtUtc);
        return Results.Ok(new LoginResponse(accessToken, expiresAtUtc, user.Id, user.Email, user.DisplayName));
    }

    private static async Task<IResult> GetMeAsync(
        ICurrentUser currentUser,
        IUserAccountStore users,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new AuthUserResponse(user.Id, user.Email, user.DisplayName));
    }
}

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthUserResponse(Guid UserId, string Email, string DisplayName);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid UserId,
    string Email,
    string DisplayName);
