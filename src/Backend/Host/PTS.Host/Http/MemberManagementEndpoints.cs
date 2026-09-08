using PTS.Modules.Tenancy;
using PTS.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace PTS.Host.Http;

public static class MemberManagementEndpoints
{
    public static IEndpointRouteBuilder MapMemberManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/tenants/{tenantId:guid}").RequireAuthorization();

        tenant.MapGet("/invitations/pending", ListPendingInvitationsAsync);
        tenant.MapPost("/invitations/{invitationId:guid}/resend", ResendInvitationAsync);
        tenant.MapPost("/invitations/{invitationId:guid}/revoke", RevokeInvitationAsync);
        tenant.MapPatch("/members/{membershipId:guid}/role", UpdateMemberRoleAsync);
        tenant.MapPost("/members/{membershipId:guid}/suspend", SuspendMemberAsync);
        tenant.MapPost("/members/{membershipId:guid}/reactivate", ReactivateMemberAsync);
        tenant.MapPost("/members/{membershipId:guid}/remove", RemoveMemberAsync);

        return endpoints;
    }

    private static async Task<IResult> ListPendingInvitationsAsync(
        Guid tenantId,
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
            var rows = await invitations.ListPendingForTenantAsync(userId, tenantId, cancellationToken);
            return Results.Ok(rows.Select(item => new PendingInvitationResponse(
                item.Id,
                item.InvitedEmail,
                item.Role.ToString(),
                item.ExpiresAtUtc,
                item.CreatedAtUtc,
                item.MembershipId)));
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ResendInvitationAsync(
        Guid tenantId,
        Guid invitationId,
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
            var result = await invitations.ResendAsync(userId, tenantId, invitationId, cancellationToken);
            return Results.Ok(new InvitationCreatedResponse(
                invitationId,
                result.InvitedEmail,
                result.ExpiresAtUtc,
                result.DevelopmentInvitationUrl,
                EmailDeliveryDeferred: result.DevelopmentInvitationUrl is not null));
        }
        catch (InvitationNotFoundException)
        {
            return Results.NotFound(new { error = "invitation_not_found" });
        }
        catch (InvitationAlreadyUsedException)
        {
            return Results.Conflict(new { error = "invitation_already_accepted" });
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> RevokeInvitationAsync(
        Guid tenantId,
        Guid invitationId,
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
            await invitations.RevokeAsync(userId, tenantId, invitationId, cancellationToken);
            return Results.NoContent();
        }
        catch (InvitationNotFoundException)
        {
            return Results.NotFound(new { error = "invitation_not_found" });
        }
        catch (InvitationAlreadyUsedException)
        {
            return Results.Conflict(new { error = "invitation_already_accepted" });
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> UpdateMemberRoleAsync(
        Guid tenantId,
        Guid membershipId,
        UpdateMemberRoleRequest request,
        ICurrentUser currentUser,
        ITenantInvitationStore invitations,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<MembershipRole>(request.Role, true, out var role))
        {
            return Results.BadRequest(new { error = "invalid_membership_role" });
        }

        try
        {
            var membership = await invitations.UpdateMemberRoleAsync(userId, tenantId, membershipId, role, cancellationToken);
            return Results.Ok(new MemberStatusResponse(membership.Id, membership.Role.ToString(), membership.Status.ToString()));
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OwnerProtectionException)
        {
            return Results.Conflict(new { error = "owner_protection" });
        }
        catch (DbUpdateException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid tenantId,
        Guid membershipId,
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
            var membership = await invitations.RemoveMemberFromOrganizationAsync(
                userId, tenantId, membershipId, cancellationToken);
            return Results.Ok(new MemberStatusResponse(membership.Id, membership.Role.ToString(), membership.Status.ToString()));
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OwnerProtectionException)
        {
            return Results.Conflict(new { error = "owner_protection" });
        }
        catch (DbUpdateException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> SuspendMemberAsync(
        Guid tenantId,
        Guid membershipId,
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
            var membership = await invitations.UpdateMemberStatusAsync(
                userId, tenantId, membershipId, MembershipStatus.Suspended, cancellationToken);
            return Results.Ok(new MemberStatusResponse(membership.Id, membership.Role.ToString(), membership.Status.ToString()));
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OwnerProtectionException)
        {
            return Results.Conflict(new { error = "owner_protection" });
        }
        catch (DbUpdateException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ReactivateMemberAsync(
        Guid tenantId,
        Guid membershipId,
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
            var membership = await invitations.UpdateMemberStatusAsync(
                userId, tenantId, membershipId, MembershipStatus.Active, cancellationToken);
            return Results.Ok(new MemberStatusResponse(membership.Id, membership.Role.ToString(), membership.Status.ToString()));
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DbUpdateException)
        {
            return Results.Json(new { error = "member_manage_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}

public sealed record UpdateMemberRoleRequest(string Role);

public sealed record PendingInvitationResponse(
    Guid InvitationId,
    string InvitedEmail,
    string Role,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    Guid? MembershipId);

public sealed record InvitationCreatedResponse(
    Guid InvitationId,
    string InvitedEmail,
    DateTimeOffset ExpiresAtUtc,
    string? InvitationUrl,
    bool EmailDeliveryDeferred);

public sealed record MemberStatusResponse(Guid MembershipId, string Role, string Status);
