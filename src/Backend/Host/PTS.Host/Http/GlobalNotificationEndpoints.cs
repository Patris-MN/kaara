using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class GlobalNotificationEndpoints
{
    private const int DefaultInboxLimit = 50;
    private const int MaxInboxLimit = 100;

    public static IEndpointRouteBuilder MapGlobalNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/notifications", ListGlobalNotificationsAsync).RequireAuthorization();
        endpoints.MapPost("/notifications/{notificationId:guid}/read", MarkGlobalNotificationReadAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ListGlobalNotificationsAsync(
        int? limit,
        ICurrentUser currentUser,
        IUserRlsSessionFactory userSessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await userSessions.OpenAsync(cancellationToken);
            var take = Math.Clamp(limit ?? DefaultInboxLimit, 1, MaxInboxLimit);

            var unreadCount = await session.DbContext.WorkNotifications
                .AsNoTracking()
                .CountAsync(item => !item.IsRead, cancellationToken);

            var items = await (
                    from notification in session.DbContext.WorkNotifications.AsNoTracking()
                    join tenant in session.DbContext.Tenants.AsNoTracking()
                        on notification.TenantId equals tenant.Id
                    orderby notification.CreatedAtUtc descending
                    select new GlobalNotificationResponse(
                        notification.Id,
                        notification.TenantId,
                        tenant.Name,
                        notification.Type.ToString(),
                        notification.TaskId,
                        notification.WorkspaceId,
                        notification.ProjectId,
                        notification.TaskTitle,
                        notification.ProjectName,
                        notification.IsRead,
                        notification.TaskId.HasValue &&
                        notification.WorkspaceId.HasValue &&
                        notification.ProjectId.HasValue,
                        notification.CreatedAtUtc))
                .Take(take)
                .ToListAsync(cancellationToken);

            await session.CommitAsync(cancellationToken);
            return Results.Ok(new GlobalNotificationInboxResponse(items, unreadCount));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> MarkGlobalNotificationReadAsync(
        Guid notificationId,
        ICurrentUser currentUser,
        IUserRlsSessionFactory userSessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await userSessions.OpenAsync(cancellationToken);
            var notification = await session.DbContext.WorkNotifications
                .FirstOrDefaultAsync(item => item.Id == notificationId, cancellationToken);
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
    }
}

public sealed record GlobalNotificationInboxResponse(
    IReadOnlyList<GlobalNotificationResponse> Items,
    int UnreadCount);

public sealed record GlobalNotificationResponse(
    Guid NotificationId,
    Guid TenantId,
    string TenantName,
    string Type,
    Guid? TaskId,
    Guid? WorkspaceId,
    Guid? ProjectId,
    string? TaskTitle,
    string? ProjectName,
    bool IsRead,
    bool TargetAvailable,
    DateTimeOffset CreatedAtUtc);
