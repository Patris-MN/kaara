using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class TenantLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapTenantLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/tenants", CreateTenantAsync).RequireAuthorization();
        endpoints.MapGet("/tenants", ListTenantsAsync).RequireAuthorization();
        endpoints.MapGet("/invitations", ListInvitationsAsync).RequireAuthorization();

        var tenant = endpoints.MapGroup("/tenants/{tenantId:guid}").RequireAuthorization();
        tenant.MapPost("/invitations", InviteAsync);
        tenant.MapPost("/invitations/accept", AcceptAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        ICurrentUser currentUser,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var tenant = await lifecycle.CreateTenantAsync(request.Name, request.Slug, cancellationToken);
            return Results.Created($"/tenants/{tenant.Id}", new TenantResponse(tenant.Id, tenant.Name, tenant.Slug));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_tenant", detail = ex.Message });
        }
        catch (DuplicateSlugException)
        {
            return Results.Conflict(new { error = "duplicate_slug" });
        }
    }

    private static async Task<IResult> ListTenantsAsync(
        ICurrentUser currentUser,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var tenants = await lifecycle.ListAccessibleTenantsAsync(cancellationToken);
            return Results.Ok(tenants.Select(ToMembershipResponse));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> ListInvitationsAsync(
        ICurrentUser currentUser,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var invitations = await lifecycle.ListPendingInvitationsAsync(cancellationToken);
            return Results.Ok(invitations.Select(ToMembershipResponse));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
    }

    private static TenantMembershipResponse ToMembershipResponse(AccessibleTenant tenant)
        => new(tenant.TenantId, tenant.Name, tenant.Slug, tenant.Role.ToString(), tenant.Status.ToString());

    private static async Task<IResult> InviteAsync(
        Guid tenantId,
        InviteMemberRequest request,
        ICurrentUser currentUser,
        IUserAccountStore users,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var credential = await users.FindCredentialByEmailAsync(email, cancellationToken);
            if (credential is null)
            {
                return Results.NotFound(new { error = "user_not_found" });
            }

            var membership = await lifecycle.InviteAsync(tenantId, credential.UserId, cancellationToken);
            return Results.Created(
                $"/tenants/{tenantId}/invitations",
                new InvitationResponse(membership.Id, membership.UserId, membership.TenantId, membership.Status.ToString()));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (InvitationNotAllowedException ex)
        {
            return Results.Json(new { error = "invite_forbidden", detail = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> AcceptAsync(
        Guid tenantId,
        ICurrentUser currentUser,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var membership = await lifecycle.AcceptInvitationAsync(tenantId, cancellationToken);
            return Results.Ok(new InvitationResponse(
                membership.Id, membership.UserId, membership.TenantId, membership.Status.ToString()));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (InvitationNotFoundException)
        {
            return Results.Json(new { error = "invitation_not_found" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}

public sealed record CreateTenantRequest(string Name, string Slug);

public sealed record InviteMemberRequest(string Email);

public sealed record TenantResponse(Guid TenantId, string Name, string Slug);

public sealed record TenantMembershipResponse(Guid TenantId, string Name, string Slug, string Role, string Status);

public sealed record InvitationResponse(Guid MembershipId, Guid UserId, Guid TenantId, string Status);
