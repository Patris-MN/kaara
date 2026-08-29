using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class TaskCollaborationEndpoints
{
    public static IEndpointRouteBuilder MapTaskCollaborationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapGroup("/tenants/{tenantId:guid}").RequireAuthorization();
        tenant.MapGet("/workspaces/{workspaceId:guid}/assignable-members", ListAssignableMembersAsync);
        tenant.MapGet("/workspaces/{workspaceId:guid}/tags", ListTagsAsync);
        tenant.MapPost("/workspaces/{workspaceId:guid}/tags", CreateTagAsync);
        tenant.MapGet("/notifications", ListNotificationsAsync);
        tenant.MapPost("/notifications/{notificationId:guid}/read", MarkNotificationReadAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAssignableMembersAsync(
        Guid tenantId,
        Guid workspaceId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (await RejectInaccessibleWorkspaceAsync(session, authorization, workspaceId, cancellationToken) is { } error)
            {
                return error;
            }

            var members = await session.DbContext.Memberships.AsNoTracking().ToListAsync(cancellationToken);
            var accessByMembership = await session.DbContext.WorkspaceAccess
                .AsNoTracking()
                .Where(access => access.WorkspaceId == workspaceId)
                .ToDictionaryAsync(access => access.MembershipId, access => access.AccessLevel, cancellationToken);
            var users = await session.DbContext.Users
                .AsNoTracking()
                .Where(user => members.Select(membership => membership.UserId).Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, cancellationToken);

            var assignable = members
                .Where(membership =>
                {
                    var implicitFullAccess = membership.Role is MembershipRole.Owner or MembershipRole.Admin;
                    accessByMembership.TryGetValue(membership.Id, out var access);
                    return authorization.IsAssignableMember(
                        membership.Status == MembershipStatus.Active,
                        implicitFullAccess,
                        implicitFullAccess ? null : access);
                })
                .Select(membership =>
                {
                    users.TryGetValue(membership.UserId, out var user);
                    return new AssignableMemberResponse(
                        membership.Id,
                        user?.DisplayName ?? string.Empty,
                        user?.Email ?? string.Empty);
                })
                .OrderBy(item => item.DisplayName)
                .ThenBy(item => item.Email)
                .ToArray();

            await session.CommitAsync(cancellationToken);
            return Results.Ok(assignable);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> ListTagsAsync(
        Guid tenantId,
        Guid workspaceId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            if (await RejectInaccessibleWorkspaceAsync(session, authorization, workspaceId, cancellationToken) is { } error)
            {
                return error;
            }

            var tags = await session.DbContext.WorkTags
                .AsNoTracking()
                .OrderBy(tag => tag.Name)
                .Select(tag => new WorkTagResponse(tag.Id, tag.Name))
                .ToListAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(tags);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> CreateTagAsync(
        Guid tenantId,
        Guid workspaceId,
        CreateWorkTagRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var workspace = await session.DbContext.Workspaces
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            var access = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
            if (!authorization.CanViewTask(session.HasImplicitFullResourceAccess, access))
            {
                return Results.NotFound(new { error = "workspace_not_found" });
            }

            if (!authorization.CanMutateTaskTags(session.HasImplicitFullResourceAccess, access))
            {
                return Results.Json(new { error = "task_edit_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tag = await TaskCollaboration.FindOrCreateTagAsync(session, request.Name, cancellationToken);
            if (tag is null)
            {
                return Results.BadRequest(new { error = "invalid_tag" });
            }

            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(new WorkTagResponse(tag.Id, tag.Name));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (DbUpdateException)
        {
            return Results.BadRequest(new { error = "invalid_tag" });
        }
    }

    private static async Task<IResult> ListNotificationsAsync(
        Guid tenantId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var notifications = await session.DbContext.WorkNotifications
                .AsNoTracking()
                .Where(item => item.RecipientMembershipId == session.MembershipId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .Select(item => new WorkNotificationResponse(
                    item.Id,
                    item.Type.ToString(),
                    item.TaskId,
                    item.WorkspaceId,
                    item.ProjectId,
                    item.IsRead,
                    item.CreatedAtUtc))
                .ToListAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(notifications);
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> MarkNotificationReadAsync(
        Guid tenantId,
        Guid notificationId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var notification = await session.DbContext.WorkNotifications
                .FirstOrDefaultAsync(
                    item => item.Id == notificationId && item.RecipientMembershipId == session.MembershipId,
                    cancellationToken);
            if (notification is null)
            {
                return Results.NotFound(new { error = "notification_not_found" });
            }

            notification.IsRead = true;
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult?> RejectInaccessibleWorkspaceAsync(
        TenantRlsSession session,
        WorkspaceAuthorizationService authorization,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var workspace = await session.DbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);
        if (workspace is null)
        {
            return Results.NotFound(new { error = "workspace_not_found" });
        }

        var access = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
        if (!authorization.CanViewTask(session.HasImplicitFullResourceAccess, access))
        {
            return Results.NotFound(new { error = "workspace_not_found" });
        }

        return null;
    }

    private static Task<WorkspaceAccessLevel?> GetExplicitAccessAsync(
        TenantRlsSession session,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (session.HasImplicitFullResourceAccess)
        {
            return Task.FromResult<WorkspaceAccessLevel?>(null);
        }

        return session.DbContext.WorkspaceAccess
            .AsNoTracking()
            .Where(access => access.MembershipId == session.MembershipId && access.WorkspaceId == workspaceId)
            .Select(access => (WorkspaceAccessLevel?)access.AccessLevel)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed record CreateWorkTagRequest(string Name);

public sealed record WorkTagResponse(Guid TagId, string Name);

public sealed record AssignableMemberResponse(Guid MembershipId, string DisplayName, string Email);

public sealed record WorkNotificationResponse(
    Guid NotificationId,
    string Type,
    Guid? TaskId,
    Guid? WorkspaceId,
    Guid? ProjectId,
    bool IsRead,
    DateTimeOffset CreatedAtUtc);
