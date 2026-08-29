using Microsoft.EntityFrameworkCore;
using PTS.Host.TenantAccess;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;

namespace PTS.Host.Http;

internal static class TaskCollaboration
{
    public static async Task<Guid?> ResolveAssigneeAsync(
        TenantRlsSession session,
        WorkspaceAuthorizationService authorization,
        Guid workspaceId,
        Guid? requestedMembershipId,
        CancellationToken cancellationToken)
    {
        if (requestedMembershipId is null)
        {
            return null;
        }

        var membership = await session.DbContext.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == requestedMembershipId && item.TenantId == session.TenantId,
                cancellationToken);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            return Guid.Empty;
        }

        var implicitFullAccess = membership.Role is MembershipRole.Owner or MembershipRole.Admin;
        WorkspaceAccessLevel? access = null;
        if (!implicitFullAccess)
        {
            access = await session.DbContext.WorkspaceAccess
                .AsNoTracking()
                .Where(item => item.MembershipId == membership.Id && item.WorkspaceId == workspaceId)
                .Select(item => (WorkspaceAccessLevel?)item.AccessLevel)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return authorization.IsAssignableMember(true, implicitFullAccess, access)
            ? membership.Id
            : Guid.Empty;
    }

    public static void RecordActivity(
        TenantRlsSession session,
        WorkTask task,
        WorkTaskActivityType eventType,
        string? oldValue,
        string? newValue)
    {
        session.DbContext.WorkTaskActivities.Add(new WorkTaskActivity
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            TaskId = task.Id,
            ActorMembershipId = session.MembershipId,
            EventType = eventType,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    public static void NotifyParticipants(
        TenantRlsSession session,
        WorkTask task,
        WorkNotificationType type,
        Guid? extraRecipientId = null)
    {
        var recipients = new HashSet<Guid>();
        recipients.Add(task.CreatedByMembershipId);
        if (task.AssignedMembershipId is Guid assignee)
        {
            recipients.Add(assignee);
        }

        if (extraRecipientId is Guid extra)
        {
            recipients.Add(extra);
        }

        recipients.Remove(session.MembershipId);
        foreach (var recipientId in recipients)
        {
            session.DbContext.WorkNotifications.Add(new WorkNotification
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                RecipientMembershipId = recipientId,
                Type = type,
                TaskId = task.Id,
                WorkspaceId = task.WorkspaceId,
                ProjectId = task.ProjectId,
                IsRead = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    public static void NotifyAssignmentChange(
        TenantRlsSession session,
        WorkTask task,
        Guid? previousAssigneeId,
        Guid? nextAssigneeId)
    {
        if (nextAssigneeId == previousAssigneeId)
        {
            return;
        }

        if (nextAssigneeId is Guid next && next != session.MembershipId)
        {
            session.DbContext.WorkNotifications.Add(new WorkNotification
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                RecipientMembershipId = next,
                Type = previousAssigneeId is null
                    ? WorkNotificationType.TaskAssigned
                    : WorkNotificationType.TaskReassigned,
                TaskId = task.Id,
                WorkspaceId = task.WorkspaceId,
                ProjectId = task.ProjectId,
                IsRead = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        if (task.CreatedByMembershipId != session.MembershipId &&
            (previousAssigneeId is not null || nextAssigneeId is not null))
        {
            session.DbContext.WorkNotifications.Add(new WorkNotification
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
                RecipientMembershipId = task.CreatedByMembershipId,
                Type = WorkNotificationType.TaskReassigned,
                TaskId = task.Id,
                WorkspaceId = task.WorkspaceId,
                ProjectId = task.ProjectId,
                IsRead = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    public static async Task<IReadOnlyList<Guid>?> SyncTagsAsync(
        TenantRlsSession session,
        WorkTask task,
        IReadOnlyList<Guid>? tagIds,
        IReadOnlyList<string>? newTags,
        CancellationToken cancellationToken)
    {
        if (tagIds is null && (newTags is null || newTags.Count == 0))
        {
            return [];
        }

        var desired = new HashSet<Guid>(tagIds ?? []);
        var newlyCreated = new HashSet<Guid>();
        foreach (var candidate in newTags ?? [])
        {
            var created = await FindOrCreateTagAsync(session, candidate, cancellationToken);
            if (created is null)
            {
                return null;
            }

            desired.Add(created.Id);
            if (session.DbContext.Entry(created).State == EntityState.Added)
            {
                newlyCreated.Add(created.Id);
            }
        }

        var toValidate = desired.Where(id => !newlyCreated.Contains(id)).ToArray();
        if (toValidate.Length > 0)
        {
            var known = await session.DbContext.WorkTags
                .AsNoTracking()
                .Where(tag => toValidate.Contains(tag.Id))
                .Select(tag => tag.Id)
                .ToListAsync(cancellationToken);
            if (known.Count != toValidate.Length)
            {
                return null;
            }
        }

        var existing = await session.DbContext.WorkTaskTags
            .Where(link => link.TaskId == task.Id)
            .ToListAsync(cancellationToken);
        foreach (var link in existing.Where(item => !desired.Contains(item.TagId)))
        {
            session.DbContext.WorkTaskTags.Remove(link);
            RecordActivity(session, task, WorkTaskActivityType.TagRemoved, link.TagId.ToString(), null);
        }

        var alreadyLinked = existing.Select(item => item.TagId).ToHashSet();
        foreach (var tagId in desired.Where(id => !alreadyLinked.Contains(id)))
        {
            session.DbContext.WorkTaskTags.Add(new WorkTaskTag
            {
                TenantId = session.TenantId,
                TaskId = task.Id,
                TagId = tagId,
            });
            RecordActivity(session, task, WorkTaskActivityType.TagAdded, null, tagId.ToString());
        }

        return desired.ToArray();
    }

    public static async Task<WorkTag?> FindOrCreateTagAsync(
        TenantRlsSession session,
        string? name,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim();
        var normalized = WorkTagConfiguration.NormalizeName(trimmed);
        if (normalized is null || trimmed is null)
        {
            return null;
        }

        var existing = await session.DbContext.WorkTags
            .FirstOrDefaultAsync(tag => tag.NormalizedName == normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new WorkTag
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            Name = trimmed,
            NormalizedName = normalized,
            CreatedByMembershipId = session.MembershipId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        session.DbContext.WorkTags.Add(created);
        return created;
    }

    public static async Task MarkSeenAsync(
        TenantRlsSession session,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var existing = await session.DbContext.WorkTaskReadStates
            .FirstOrDefaultAsync(
                state => state.TaskId == taskId && state.MembershipId == session.MembershipId,
                cancellationToken);
        if (existing is null)
        {
            session.DbContext.WorkTaskReadStates.Add(new WorkTaskReadState
            {
                TenantId = session.TenantId,
                TaskId = taskId,
                MembershipId = session.MembershipId,
                LastViewedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        existing.LastViewedAtUtc = DateTimeOffset.UtcNow;
    }

    public static async Task<IReadOnlyList<WorkTaskResponse>> ToTaskResponsesAsync(
        TenantRlsSession session,
        TaskAuthorizationService authorization,
        IReadOnlyList<WorkTask> tasks,
        bool markSelectedSeen,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var taskIds = tasks.Select(task => task.Id).ToArray();
        var membershipIds = tasks
            .SelectMany(task => new[] { task.CreatedByMembershipId, task.AssignedMembershipId ?? Guid.Empty })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var people = membershipIds.Length == 0
            ? []
            : await (
                    from membership in session.DbContext.Memberships.AsNoTracking()
                    join user in session.DbContext.Users.AsNoTracking()
                        on membership.UserId equals user.Id
                    where membershipIds.Contains(membership.Id)
                    select new { membership.Id, user.DisplayName, user.Email })
                .ToListAsync(cancellationToken);
        var personById = people.ToDictionary(item => item.Id);

        var tagRows = await (
                from link in session.DbContext.WorkTaskTags.AsNoTracking()
                join tag in session.DbContext.WorkTags.AsNoTracking()
                    on new { link.TenantId, link.TagId } equals new { tag.TenantId, TagId = tag.Id }
                where taskIds.Contains(link.TaskId)
                orderby tag.Name
                select new { link.TaskId, tag.Id, tag.Name })
            .ToListAsync(cancellationToken);
        var tagsByTask = tagRows
            .GroupBy(row => row.TaskId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new WorkTaskTagResponse(row.Id, row.Name)).ToArray());

        var lastViewed = await session.DbContext.WorkTaskReadStates
            .AsNoTracking()
            .Where(state => taskIds.Contains(state.TaskId) && state.MembershipId == session.MembershipId)
            .ToDictionaryAsync(state => state.TaskId, state => state.LastViewedAtUtc, cancellationToken);

        var unseenRows = await session.DbContext.WorkTaskActivities
            .AsNoTracking()
            .Where(activity =>
                taskIds.Contains(activity.TaskId) &&
                activity.ActorMembershipId != session.MembershipId)
            .Select(activity => new { activity.TaskId, activity.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var unseenByTask = unseenRows
            .GroupBy(row => row.TaskId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    lastViewed.TryGetValue(group.Key, out var viewed);
                    return group.Count(row => viewed == default || row.CreatedAtUtc > viewed);
                });

        if (markSelectedSeen && tasks.Count == 1)
        {
            await MarkSeenAsync(session, tasks[0].Id, cancellationToken);
        }

        return tasks.Select(task =>
        {
            personById.TryGetValue(task.CreatedByMembershipId, out var creator);
            personById.TryGetValue(task.AssignedMembershipId ?? Guid.Empty, out var assignee);
            tagsByTask.TryGetValue(task.Id, out var tags);
            unseenByTask.TryGetValue(task.Id, out var unseen);
            var subject = authorization.Describe(session.MembershipId, task, hasWorkspaceView: true);
            return new WorkTaskResponse(
                task.Id,
                task.TenantId,
                task.WorkspaceId,
                task.ProjectId,
                task.Title,
                task.Description,
                task.Status.ToString(),
                task.Priority.ToString(),
                task.DueDate,
                task.CreatedAtUtc,
                task.UpdatedAtUtc,
                task.AssignedMembershipId,
                assignee?.DisplayName,
                assignee?.Email,
                tags ?? [],
                task.CreatedByMembershipId,
                creator?.DisplayName,
                creator?.Email,
                unseen,
                new TaskCapabilitiesResponse(
                    authorization.CanEditDefinition(subject, task.Status),
                    authorization.CanManageTags(subject, task.Status),
                    authorization.CanReassign(subject, task.Status),
                    authorization.CanComment(subject, task.Status),
                    authorization.CanDelete(subject),
                    authorization.AllowedStatuses(subject, task.Status).Select(status => status.ToString()).ToArray()));
        }).ToArray();
    }

    private static string? Truncate(string? value)
        => value is { Length: > WorkTaskActivityConfiguration.ValueMaxLength }
            ? value[..WorkTaskActivityConfiguration.ValueMaxLength]
            : value;
}
