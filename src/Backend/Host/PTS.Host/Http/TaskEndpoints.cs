using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.WorkManagement;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var project = endpoints
            .MapGroup("/tenants/{tenantId:guid}/workspaces/{workspaceId:guid}/projects/{projectId:guid}")
            .RequireAuthorization();

        project.MapGet("", GetProjectAsync);
        project.MapGet("/tasks", ListTasksAsync);
        project.MapGet("/tasks/{taskId:guid}", GetTaskAsync);
        project.MapPost("/tasks", CreateTaskAsync);
        project.MapPut("/tasks/{taskId:guid}", UpdateTaskAsync);
        project.MapDelete("/tasks/{taskId:guid}", DeleteTaskAsync);
        project.MapGet("/tasks/{taskId:guid}/comments", ListCommentsAsync);
        project.MapPost("/tasks/{taskId:guid}/comments", CreateCommentAsync);
        project.MapPut("/tasks/{taskId:guid}/comments/{commentId:guid}", UpdateCommentAsync);
        project.MapDelete("/tasks/{taskId:guid}/comments/{commentId:guid}", DeleteCommentAsync);
        project.MapGet("/tasks/{taskId:guid}/activity", ListActivityAsync);
        project.MapPost("/tasks/{taskId:guid}/seen", MarkTaskSeenAsync);

        return endpoints;
    }

    private static async Task<IResult> GetProjectAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
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
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            await session.CommitAsync(cancellationToken);
            return Results.Ok(ToProjectResponse(resolved.Project!));
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

    private static async Task<IResult> ListTasksAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var tasks = await session.DbContext.WorkTasks
                .AsNoTracking()
                .Where(task =>
                    task.ProjectId == projectId &&
                    task.WorkspaceId == workspaceId)
                .OrderBy(task => task.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var responses = await TaskCollaboration.ToTaskResponsesAsync(
                session, taskAuthorization, tasks, markSelectedSeen: false, cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(responses);
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

    private static async Task<IResult> GetTaskAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var response = await TaskCollaboration.ToTaskResponsesAsync(
                session, taskAuthorization, [task], markSelectedSeen: true, cancellationToken);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(response[0]);
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

    private static async Task<IResult> CreateTaskAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        CreateWorkTaskRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > WorkTaskConfiguration.TitleMaxLength)
        {
            return Results.BadRequest(new { error = "invalid_task" });
        }

        if (request.Description is { Length: > WorkTaskConfiguration.DescriptionMaxLength })
        {
            return Results.BadRequest(new { error = "invalid_task_description" });
        }

        if (!TryParseStatus(request.Status, out var status))
        {
            return Results.BadRequest(new { error = "invalid_task_status" });
        }

        if (!TryParsePriority(request.Priority, out var priority))
        {
            return Results.BadRequest(new { error = "invalid_task_priority" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            if (!authorization.CanEditTask(session.HasImplicitFullResourceAccess, resolved.Access))
            {
                return Results.Json(
                    new { error = "task_edit_forbidden" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var assigneeId = await TaskCollaboration.ResolveAssigneeAsync(
                session, authorization, workspaceId, request.AssigneeMembershipId, cancellationToken);
            if (assigneeId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "invalid_assignee" });
            }

            var now = DateTimeOffset.UtcNow;
            var task = new WorkTask
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                WorkspaceId = workspaceId,
                ProjectId = projectId,
                Title = title,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Status = status,
                Priority = priority,
                DueDate = request.DueDate,
                CreatedByMembershipId = session.MembershipId,
                AssignedMembershipId = assigneeId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            session.DbContext.WorkTasks.Add(task);
            if (await TaskCollaboration.SyncTagsAsync(
                    session, task, request.TagIds, request.NewTags, cancellationToken) is null)
            {
                return Results.BadRequest(new { error = "invalid_tag" });
            }

            TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.TaskCreated, null, title);
            TaskCollaboration.NotifyAssignmentChange(session, task, null, assigneeId);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            var created = await TaskCollaboration.ToTaskResponsesAsync(
                session, taskAuthorization, [task], markSelectedSeen: false, cancellationToken);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{task.Id}",
                created[0]);
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
            return Results.BadRequest(new { error = "invalid_task_relationship" });
        }
    }

    private static async Task<IResult> UpdateTaskAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        UpdateWorkTaskRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > WorkTaskConfiguration.TitleMaxLength)
        {
            return Results.BadRequest(new { error = "invalid_task" });
        }

        if (request.Description is { Length: > WorkTaskConfiguration.DescriptionMaxLength })
        {
            return Results.BadRequest(new { error = "invalid_task_description" });
        }

        if (!TryParseStatus(request.Status, out var status))
        {
            return Results.BadRequest(new { error = "invalid_task_status" });
        }

        if (!TryParsePriority(request.Priority, out var priority))
        {
            return Results.BadRequest(new { error = "invalid_task_priority" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var subject = taskAuthorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            var definitionChanged =
                !string.Equals(task.Title, title, StringComparison.Ordinal) ||
                !string.Equals(task.Description, description, StringComparison.Ordinal) ||
                task.Priority != priority ||
                task.DueDate != request.DueDate;
            if (definitionChanged && !taskAuthorization.CanEditDefinition(subject, task.Status))
            {
                return Results.Json(new { error = "task_field_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var assigneeId = await TaskCollaboration.ResolveAssigneeAsync(
                session, authorization, workspaceId, request.AssigneeMembershipId, cancellationToken);
            if (assigneeId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "invalid_assignee" });
            }

            if (assigneeId != task.AssignedMembershipId && !taskAuthorization.CanReassign(subject, task.Status))
            {
                return Results.Json(new { error = "task_field_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (status != task.Status && !taskAuthorization.CanChangeStatus(subject, task.Status, status))
            {
                return Results.Json(new { error = "task_status_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var tagsRequested = request.TagIds is not null || (request.NewTags is { Count: > 0 });
            if (tagsRequested && !taskAuthorization.CanManageTags(subject, task.Status))
            {
                return Results.Json(new { error = "task_field_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var previousTitle = task.Title;
            var previousDescription = task.Description;
            var previousPriority = task.Priority;
            var previousDue = task.DueDate;
            var previousStatus = task.Status;
            var previousAssignee = task.AssignedMembershipId;

            task.Title = title;
            task.Description = description;
            task.Status = status;
            task.Priority = priority;
            task.DueDate = request.DueDate;
            task.AssignedMembershipId = assigneeId;
            task.UpdatedAtUtc = DateTimeOffset.UtcNow;

            var previousTagIds = tagsRequested
                ? await session.DbContext.WorkTaskTags.Where(link => link.TaskId == task.Id).Select(link => link.TagId).ToListAsync(cancellationToken)
                : [];
            var syncedTags = await TaskCollaboration.SyncTagsAsync(
                session, task, request.TagIds, request.NewTags, cancellationToken);
            if (syncedTags is null)
            {
                return Results.BadRequest(new { error = "invalid_tag" });
            }

            if (!string.Equals(previousTitle, title, StringComparison.Ordinal))
            {
                TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.TitleChanged, previousTitle, title);
                TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskUpdated);
            }

            if (!string.Equals(previousDescription, description, StringComparison.Ordinal))
            {
                TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.DescriptionChanged, previousDescription, description);
                TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskUpdated);
            }

            if (previousPriority != priority)
            {
                TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.PriorityChanged, previousPriority.ToString(), priority.ToString());
                TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskPriorityChanged);
            }

            if (previousDue != request.DueDate)
            {
                TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.DeadlineChanged, FormatDate(previousDue), FormatDate(request.DueDate));
                TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskDeadlineChanged);
            }

            if (previousStatus != status)
            {
                var eventType = status == WorkTaskStatus.Open && previousStatus == WorkTaskStatus.Closed
                    ? WorkTaskActivityType.TaskReopened
                    : WorkTaskActivityType.StatusChanged;
                TaskCollaboration.RecordActivity(session, task, eventType, previousStatus.ToString(), status.ToString());
                var notifyType = status == WorkTaskStatus.Closed
                    ? WorkNotificationType.TaskClosed
                    : status == WorkTaskStatus.Open && previousStatus == WorkTaskStatus.Closed
                        ? WorkNotificationType.TaskReopened
                        : WorkNotificationType.TaskStatusChanged;
                TaskCollaboration.NotifyParticipants(session, task, notifyType);
            }

            if (previousAssignee != assigneeId)
            {
                TaskCollaboration.RecordActivity(
                    session,
                    task,
                    WorkTaskActivityType.AssigneeChanged,
                    previousAssignee?.ToString(),
                    assigneeId?.ToString());
                TaskCollaboration.NotifyAssignmentChange(session, task, previousAssignee, assigneeId);
            }

            if (tagsRequested &&
                (previousTagIds.Count != syncedTags.Count || previousTagIds.Any(id => !syncedTags.Contains(id))))
            {
                TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskTagChanged);
            }

            await session.DbContext.SaveChangesAsync(cancellationToken);
            var updated = await TaskCollaboration.ToTaskResponsesAsync(
                session, taskAuthorization, [task], markSelectedSeen: false, cancellationToken);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(updated[0]);
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

    private static async Task<IResult> DeleteTaskAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var subject = taskAuthorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            if (!taskAuthorization.CanDelete(subject))
            {
                return Results.Json(new { error = "task_delete_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            session.DbContext.WorkTasks.Remove(task);
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

    private static async Task<IResult> ListCommentsAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
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
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var comments = await session.DbContext.WorkTaskComments
                .AsNoTracking()
                .Where(comment => comment.TaskId == taskId)
                .OrderBy(comment => comment.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var authors = await LoadPeopleAsync(session, comments.Select(comment => comment.AuthorMembershipId), cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(comments.Select(comment =>
            {
                authors.TryGetValue(comment.AuthorMembershipId, out var author);
                return new WorkTaskCommentResponse(
                    comment.Id,
                    comment.AuthorMembershipId,
                    author.DisplayName,
                    comment.Body,
                    comment.CreatedAtUtc,
                    comment.UpdatedAtUtc,
                    comment.AuthorMembershipId == session.MembershipId);
            }).ToArray());
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

    private static async Task<IResult> CreateCommentAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        CreateWorkTaskCommentRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body) || body.Length > WorkTaskCommentConfiguration.BodyMaxLength)
        {
            return Results.BadRequest(new { error = "invalid_comment" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var subject = taskAuthorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            if (!taskAuthorization.CanComment(subject, task.Status))
            {
                return Results.Json(new { error = "task_comment_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var comment = new WorkTaskComment
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                TaskId = task.Id,
                AuthorMembershipId = session.MembershipId,
                Body = body,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            session.DbContext.WorkTaskComments.Add(comment);
            TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.CommentAdded, null, body);
            TaskCollaboration.NotifyParticipants(session, task, WorkNotificationType.TaskCommentAdded);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Created(
                $"/tenants/{session.TenantId}/workspaces/{workspaceId}/projects/{projectId}/tasks/{taskId}/comments/{comment.Id}",
                new WorkTaskCommentResponse(comment.Id, comment.AuthorMembershipId, null, comment.Body, comment.CreatedAtUtc, null, true));
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

    private static async Task<IResult> UpdateCommentAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        Guid commentId,
        CreateWorkTaskCommentRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body) || body.Length > WorkTaskCommentConfiguration.BodyMaxLength)
        {
            return Results.BadRequest(new { error = "invalid_comment" });
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var comment = await session.DbContext.WorkTaskComments
                .FirstOrDefaultAsync(item => item.Id == commentId && item.TaskId == taskId, cancellationToken);
            if (comment is null)
            {
                return Results.NotFound(new { error = "comment_not_found" });
            }

            var subject = taskAuthorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            if (!taskAuthorization.CanEditOwnComment(subject, comment.AuthorMembershipId))
            {
                return Results.Json(new { error = "task_comment_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            var previous = comment.Body;
            comment.Body = body;
            comment.UpdatedAtUtc = DateTimeOffset.UtcNow;
            TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.CommentEdited, previous, body);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(new WorkTaskCommentResponse(
                comment.Id, comment.AuthorMembershipId, null, comment.Body, comment.CreatedAtUtc, comment.UpdatedAtUtc, true));
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

    private static async Task<IResult> DeleteCommentAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        Guid commentId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        WorkspaceAuthorizationService authorization,
        TaskAuthorizationService taskAuthorization,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var comment = await session.DbContext.WorkTaskComments
                .FirstOrDefaultAsync(item => item.Id == commentId && item.TaskId == taskId, cancellationToken);
            if (comment is null)
            {
                return Results.NotFound(new { error = "comment_not_found" });
            }

            var subject = taskAuthorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            if (!taskAuthorization.CanEditOwnComment(subject, comment.AuthorMembershipId))
            {
                return Results.Json(new { error = "task_comment_forbidden" }, statusCode: StatusCodes.Status403Forbidden);
            }

            TaskCollaboration.RecordActivity(session, task, WorkTaskActivityType.CommentDeleted, comment.Body, null);
            session.DbContext.WorkTaskComments.Remove(comment);
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

    private static async Task<IResult> ListActivityAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
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
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            var activities = await session.DbContext.WorkTaskActivities
                .AsNoTracking()
                .Where(item => item.TaskId == taskId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var actors = await LoadPeopleAsync(session, activities.Select(item => item.ActorMembershipId), cancellationToken);
            await session.CommitAsync(cancellationToken);
            return Results.Ok(activities.Select(item =>
            {
                actors.TryGetValue(item.ActorMembershipId, out var actor);
                return new WorkTaskActivityResponse(
                    item.Id,
                    item.EventType.ToString(),
                    item.ActorMembershipId,
                    actor.DisplayName,
                    item.OldValue,
                    item.NewValue,
                    item.CreatedAtUtc);
            }).ToArray());
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

    private static async Task<IResult> MarkTaskSeenAsync(
        Guid tenantId,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
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
            var resolved = await ResolveAccessibleProjectAsync(
                session, authorization, workspaceId, projectId, cancellationToken);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            var task = await FindTaskInHierarchyAsync(session, workspaceId, projectId, taskId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound(new { error = "task_not_found" });
            }

            await TaskCollaboration.MarkSeenAsync(session, task.Id, cancellationToken);
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

    private static async Task<ResolvedProject> ResolveAccessibleProjectAsync(
        TenantRlsSession session,
        WorkspaceAuthorizationService authorization,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var workspace = await session.DbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == workspaceId, cancellationToken);
        if (workspace is null)
        {
            return new ResolvedProject(null, null, Results.NotFound(new { error = "workspace_not_found" }));
        }

        var explicitAccess = await GetExplicitAccessAsync(session, workspaceId, cancellationToken);
        if (!authorization.CanViewTask(session.HasImplicitFullResourceAccess, explicitAccess))
        {
            return new ResolvedProject(null, explicitAccess, Results.NotFound(new { error = "workspace_not_found" }));
        }

        var project = await session.DbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == projectId && item.WorkspaceId == workspaceId,
                cancellationToken);
        if (project is null)
        {
            return new ResolvedProject(null, explicitAccess, Results.NotFound(new { error = "project_not_found" }));
        }

        return new ResolvedProject(project, explicitAccess, null);
    }

    private static Task<WorkTask?> FindTaskInHierarchyAsync(
        TenantRlsSession session,
        Guid workspaceId,
        Guid projectId,
        Guid taskId,
        CancellationToken cancellationToken)
        => session.DbContext.WorkTasks
            .FirstOrDefaultAsync(
                task =>
                    task.Id == taskId &&
                    task.ProjectId == projectId &&
                    task.WorkspaceId == workspaceId,
                cancellationToken);

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
            .Where(access =>
                access.MembershipId == session.MembershipId &&
                access.WorkspaceId == workspaceId)
            .Select(access => (WorkspaceAccessLevel?)access.AccessLevel)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryParseStatus(string? value, out WorkTaskStatus status)
    {
        status = TaskStatusWorkflow.ParseOrDefault(value, out var valid);
        return valid;
    }

    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd");

    private static async Task<Dictionary<Guid, (string DisplayName, string Email)>> LoadPeopleAsync(
        TenantRlsSession session,
        IEnumerable<Guid> membershipIds,
        CancellationToken cancellationToken)
    {
        var ids = membershipIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await (
                from membership in session.DbContext.Memberships.AsNoTracking()
                join user in session.DbContext.Users.AsNoTracking()
                    on membership.UserId equals user.Id
                where ids.Contains(membership.Id)
                select new { membership.Id, user.DisplayName, user.Email })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => (row.DisplayName, row.Email));
    }

    private static bool TryParsePriority(string? value, out WorkTaskPriority priority)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            priority = WorkTaskPriority.Normal;
            return true;
        }

        if (string.Equals(value, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            priority = WorkTaskPriority.Normal;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out priority)
            && Enum.IsDefined(priority);
    }

    private static ProjectResponse ToProjectResponse(Project project)
        => new(project.Id, project.TenantId, project.WorkspaceId, project.Name);

    private sealed record ResolvedProject(
        Project? Project,
        WorkspaceAccessLevel? Access,
        IResult? Error);
}

public sealed record CreateWorkTaskRequest(
    string Title,
    string? Description = null,
    string? Status = null,
    string? Priority = null,
    DateOnly? DueDate = null,
    Guid? TenantId = null,
    Guid? AssigneeMembershipId = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<string>? NewTags = null);

public sealed record UpdateWorkTaskRequest(
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateOnly? DueDate,
    Guid? AssigneeMembershipId = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<string>? NewTags = null);

public sealed record WorkTaskTagResponse(Guid TagId, string Name);

public sealed record WorkTaskResponse(
    Guid TaskId,
    Guid TenantId,
    Guid WorkspaceId,
    Guid ProjectId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateOnly? DueDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? AssigneeMembershipId = null,
    string? AssigneeDisplayName = null,
    string? AssigneeEmail = null,
    IReadOnlyList<WorkTaskTagResponse>? Tags = null,
    Guid? CreatedByMembershipId = null,
    string? CreatedByDisplayName = null,
    string? CreatedByEmail = null,
    int UnseenActivityCount = 0,
    TaskCapabilitiesResponse? Capabilities = null);

public sealed record TaskCapabilitiesResponse(
    bool CanEditDefinition,
    bool CanManageTags,
    bool CanReassign,
    bool CanComment,
    bool CanDelete,
    IReadOnlyList<string> AllowedStatuses);

public sealed record CreateWorkTaskCommentRequest(string Body);

public sealed record WorkTaskCommentResponse(
    Guid CommentId,
    Guid AuthorMembershipId,
    string? AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    bool IsOwn = false);

public sealed record WorkTaskActivityResponse(
    Guid ActivityId,
    string EventType,
    Guid ActorMembershipId,
    string? ActorDisplayName,
    string? OldValue,
    string? NewValue,
    DateTimeOffset CreatedAtUtc);
