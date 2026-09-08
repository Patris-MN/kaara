using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.SharedKernel.Entitlements;
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
        tenant.MapPut(string.Empty, UpdateTenantAsync);
        tenant.MapPost("/invitations", InviteAsync);
        tenant.MapPost("/invitations/accept", AcceptAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        ICurrentUser currentUser,
        IOrganizationCreationEntitlementProvider organizationCreation,
        ITenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            var entitlement = await organizationCreation.GetForCurrentUserAsync(cancellationToken);
            if (!entitlement.CanCreateOrganization)
            {
                return Results.Json(
                    new { error = "organization_create_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

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
        catch (TenantNameConflictException ex)
        {
            return Results.Conflict(new { error = "organization_name_conflict", existingName = ex.ExistingName });
        }
    }

    private static async Task<IResult> UpdateTenantAsync(
        Guid tenantId,
        UpdateTenantRequest request,
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
            var tenant = await lifecycle.UpdateTenantAsync(tenantId, request.Name, cancellationToken);
            return Results.Ok(new TenantResponse(tenant.Id, tenant.Name, tenant.Slug));
        }
        catch (UnauthenticatedException)
        {
            return Results.Unauthorized();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_tenant", detail = ex.Message });
        }
        catch (TenantUpdateForbiddenException)
        {
            return Results.Json(new { error = "tenant_update_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (TenantNameConflictException ex)
        {
            return Results.Conflict(new { error = "organization_name_conflict", existingName = ex.ExistingName });
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
        => new(
            tenant.TenantId,
            tenant.Name,
            tenant.Slug,
            tenant.Role.ToString(),
            tenant.Status.ToString(),
            tenant.WorkspaceCount,
            CanManage: tenant.Status == MembershipStatus.Active
                && tenant.Role is MembershipRole.Owner or MembershipRole.Admin);

    private static async Task<IResult> InviteAsync(
        Guid tenantId,
        InviteMemberRequest request,
        ICurrentUser currentUser,
        ITenantInvitationStore invitations,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<MembershipRole>(request.Role ?? "Member", true, out var role))
        {
            return Results.BadRequest(new { error = "invalid_membership_role" });
        }

        var grants = (request.WorkspaceAccess ?? [])
            .Select(item => (item.WorkspaceId, item.AccessLevel))
            .ToList();

        try
        {
            var result = await invitations.CreateInvitationAsync(
                userId,
                tenantId,
                request.Email,
                role,
                grants,
                cancellationToken);

            return Results.Created(
                $"/tenants/{tenantId}/invitations",
                new InvitationCreatedResponse(
                    result.InvitationId,
                    result.InvitedEmail,
                    result.ExpiresAtUtc,
                    result.DevelopmentInvitationUrl,
                    EmailDeliveryDeferred: true));
        }
        catch (InvitationNotAllowedException ex)
        {
            return Results.Json(new { error = "invite_forbidden", detail = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (MemberManagementForbiddenException)
        {
            return Results.Json(new { error = "invite_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (AlreadyTenantMemberException)
        {
            return Results.Conflict(new { error = "already_tenant_member" });
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

public sealed record UpdateTenantRequest(string Name);

public sealed record InviteMemberRequest(
    string Email,
    string? Role = null,
    IReadOnlyList<InviteWorkspaceAccessRequest>? WorkspaceAccess = null);

public sealed record InviteWorkspaceAccessRequest(Guid WorkspaceId, string AccessLevel);

public sealed record TenantResponse(Guid TenantId, string Name, string Slug);

public sealed record TenantMembershipResponse(
    Guid TenantId,
    string Name,
    string Slug,
    string Role,
    string Status,
    int WorkspaceCount = 0,
    bool CanManage = false);

public sealed record InvitationResponse(Guid MembershipId, Guid UserId, Guid TenantId, string Status);
