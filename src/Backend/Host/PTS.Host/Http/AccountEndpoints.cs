using PTS.Host.Authentication;
using PTS.Modules.Identity;
using PTS.SharedKernel.Entitlements;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/capabilities", GetCapabilitiesAsync).RequireAuthorization();
        endpoints.MapGet("/account/profile", GetProfileAsync).RequireAuthorization();
        endpoints.MapPatch("/account/profile", UpdateProfileAsync).RequireAuthorization();
        endpoints.MapPost("/account/change-password", ChangePasswordAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetCapabilitiesAsync(
        ICurrentUser currentUser,
        IOrganizationCreationEntitlementProvider organizationCreation,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var entitlement = await organizationCreation.GetForCurrentUserAsync(cancellationToken);
            return Results.Ok(new AccountCapabilitiesResponse(
                entitlement.CanCreateOrganization,
                entitlement.OrganizationLimit,
                entitlement.CurrentOrganizationCount,
                entitlement.ActiveMembershipCount,
                entitlement.PendingInvitationCount));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> GetProfileAsync(
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

        var credential = await users.FindCredentialByUserIdAsync(userId, cancellationToken);
        return Results.Ok(new AccountProfileResponse(
            user.Email,
            user.DisplayName,
            HasLocalCredential: credential is not null));
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateAccountProfileRequest request,
        ICurrentUser currentUser,
        IUserAuthenticationService authentication,
        IUserAccountStore users,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        try
        {
            var user = await authentication.UpdateDisplayNameAsync(userId, request.DisplayName, cancellationToken);
            var credential = await users.FindCredentialByUserIdAsync(userId, cancellationToken);
            return Results.Ok(new AccountProfileResponse(
                user.Email,
                user.DisplayName,
                HasLocalCredential: credential is not null));
        }
        catch (ArgumentException ex) when (ex.ParamName == "displayName")
        {
            return Results.BadRequest(new { error = "invalid_display_name" });
        }
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ICurrentUser currentUser,
        IUserAuthenticationService authentication,
        IUserAccountStore users,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        if (await users.FindCredentialByUserIdAsync(userId, cancellationToken) is null)
        {
            return Results.BadRequest(new { error = "local_credential_unavailable" });
        }

        try
        {
            await authentication.ChangePasswordAsync(
                userId,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken);
            return Results.NoContent();
        }
        catch (InvalidCurrentPasswordException)
        {
            return Results.BadRequest(new { error = "invalid_current_password" });
        }
        catch (ArgumentException ex) when (ex.ParamName == "newPassword")
        {
            return Results.BadRequest(new { error = "invalid_password" });
        }
        catch (ArgumentException ex) when (ex.ParamName == "currentPassword")
        {
            return Results.BadRequest(new { error = "current_password_required" });
        }
    }
}

public sealed record AccountCapabilitiesResponse(
    bool CanCreateOrganization,
    int OrganizationLimit,
    int CurrentOrganizationCount,
    int ActiveMembershipCount,
    int PendingInvitationCount);

public sealed record AccountProfileResponse(
    string Email,
    string DisplayName,
    bool HasLocalCredential);

public sealed record UpdateAccountProfileRequest(string DisplayName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
