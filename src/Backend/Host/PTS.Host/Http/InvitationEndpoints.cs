using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/invite/{token}", PreviewAsync);
        endpoints.MapPost("/invite/{token}/accept", AcceptByTokenAsync).RequireAuthorization();
        endpoints.MapPost("/invite/{token}/register", RegisterAndAcceptAsync);

        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        string token,
        ITenantInvitationStore invitations,
        CancellationToken cancellationToken)
    {
        try
        {
            var preview = await invitations.FindPreviewByRawTokenAsync(token, cancellationToken);
            if (preview is null)
            {
                return Results.Json(new { error = "invitation_invalid" }, statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(new InvitationPreviewResponse(
                preview.OrganizationName,
                preview.InvitedEmail,
                preview.Role.ToString(),
                preview.ExpiresAtUtc,
                preview.InviterDisplayName,
                preview.RequiresRegistration,
                preview.WorkspaceGrants.Select(item => new InvitationWorkspaceGrantResponse(item.WorkspaceName, item.AccessLevel)).ToList()));
        }
        catch (InvitationExpiredException)
        {
            return Results.Json(new { error = "invitation_expired" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationRevokedException)
        {
            return Results.Json(new { error = "invitation_revoked" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationAlreadyUsedException)
        {
            return Results.Json(new { error = "invitation_already_accepted" }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> AcceptByTokenAsync(
        string token,
        ICurrentUser currentUser,
        ITenantInvitationStore invitations,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        try
        {
            var membership = await invitations.AcceptByRawTokenAsync(userId, token, cancellationToken);
            return Results.Ok(new InvitationAcceptResponse(
                membership.Id,
                membership.TenantId,
                membership.Role.ToString(),
                membership.Status.ToString()));
        }
        catch (InvitationNotFoundException)
        {
            return Results.Json(new { error = "invitation_invalid" }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvitationExpiredException)
        {
            return Results.Json(new { error = "invitation_expired" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationRevokedException)
        {
            return Results.Json(new { error = "invitation_revoked" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationAlreadyUsedException)
        {
            return Results.Json(new { error = "invitation_already_accepted" }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvitationEmailMismatchException)
        {
            return Results.Json(new { error = "invitation_email_mismatch" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (AlreadyTenantMemberException)
        {
            return Results.Json(new { error = "already_tenant_member" }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RegisterAndAcceptAsync(
        string token,
        RegisterAndAcceptInvitationRequest request,
        ITenantInvitationStore invitations,
        IUserAuthenticationService authentication,
        CancellationToken cancellationToken)
    {
        try
        {
            var membership = await invitations.RegisterAndAcceptByRawTokenAsync(
                token,
                request.DisplayName,
                request.Password,
                async (email, password, displayName, ct) =>
                {
                    var user = await authentication.RegisterAsync(email, password, displayName, ct);
                    return user.Id;
                },
                cancellationToken);

            return Results.Ok(new InvitationAcceptResponse(
                membership.Id,
                membership.TenantId,
                membership.Role.ToString(),
                membership.Status.ToString()));
        }
        catch (InvitationNotFoundException)
        {
            return Results.Json(new { error = "invitation_invalid" }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvitationExpiredException)
        {
            return Results.Json(new { error = "invitation_expired" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationRevokedException)
        {
            return Results.Json(new { error = "invitation_revoked" }, statusCode: StatusCodes.Status410Gone);
        }
        catch (InvitationAlreadyUsedException)
        {
            return Results.Json(new { error = "invitation_already_accepted" }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvitationNotAllowedException ex)
        {
            return Results.BadRequest(new { error = "invitation_register_forbidden", detail = ex.Message });
        }
        catch (AlreadyTenantMemberException)
        {
            return Results.Json(new { error = "already_tenant_member" }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (DuplicateEmailException)
        {
            return Results.Conflict(new { error = "email_already_registered" });
        }
        catch (ArgumentException ex)
        {
            var error = ex.ParamName switch
            {
                "password" => "invalid_password",
                "displayName" => "invalid_display_name",
                _ => "invalid_registration",
            };
            return Results.BadRequest(new { error });
        }
    }
}

public sealed record RegisterAndAcceptInvitationRequest(string DisplayName, string Password);

public sealed record InvitationPreviewResponse(
    string OrganizationName,
    string InvitedEmail,
    string Role,
    DateTimeOffset ExpiresAtUtc,
    string? InviterDisplayName,
    bool RequiresRegistration,
    IReadOnlyList<InvitationWorkspaceGrantResponse> WorkspaceGrants);

public sealed record InvitationWorkspaceGrantResponse(string WorkspaceName, string AccessLevel);

public sealed record InvitationAcceptResponse(
    Guid MembershipId,
    Guid TenantId,
    string Role,
    string Status);
