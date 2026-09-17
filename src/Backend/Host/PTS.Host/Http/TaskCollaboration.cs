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
        MarkExternalEngagementIfNeeded(session, task);
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

    public static async Task<string?> ResolveProjectNameAsync(
        TenantRlsSession session,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return await session.DbContext.Projects
            .AsNoTracking()
            .Where(project => project.TenantId == session.TenantId && project.Id == projectId)
            .Select(project => project.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static void NotifyParticipants(
        TenantRlsSession session,
        WorkTask task,
        WorkNotificationType type,
        string? projectName = null,
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
            session.DbContext.WorkNotifications.Add(CreateNotification(
                session,
                task,
                recipientId,
                type,
                projectName));
        }
    }

    public static void NotifyAssignmentChange(
        TenantRlsSession session,
        WorkTask task,
        Guid? previousAssigneeId,
        Guid? nextAssigneeId,
        string? projectName = null)
    {
        if (nextAssigneeId == previousAssigneeId)
        {
            return;
        }

        if (nextAssigneeId is Guid next && next != session.MembershipId)
        {
            session.DbContext.WorkNotifications.Add(CreateNotification(
                session,
                task,
                next,
                previousAssigneeId is null
                    ? WorkNotificationType.TaskAssigned
                    : WorkNotificationType.TaskReassigned,
                projectName));
        }

        if (task.CreatedByMembershipId != session.MembershipId &&
            (previousAssigneeId is not null || nextAssigneeId is not null))
        {
            session.DbContext.WorkNotifications.Add(CreateNotification(
                session,
                task,
                task.CreatedByMembershipId,
                WorkNotificationType.TaskReassigned,
                projectName));
        }
    }

    private static WorkNotification CreateNotification(
        TenantRlsSession session,
        WorkTask task,
        Guid recipientMembershipId,
        WorkNotificationType type,
        string? projectName)
    {
        return new WorkNotification
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            RecipientMembershipId = recipientMembershipId,
            Type = type,
            TaskId = task.Id,
            WorkspaceId = task.WorkspaceId,
            ProjectId = task.ProjectId,
            TaskTitle = string.IsNullOrWhiteSpace(task.Title) ? null : task.Title.Trim(),
            ProjectName = projectName,
            IsRead = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
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

        var tagIdsForLookup = existing.Select(item => item.TagId)
            .Concat(desired)
            .Distinct()
            .ToArray();
        var tagNames = tagIdsForLookup.Length == 0
            ? new Dictionary<Guid, string>()
            : await session.DbContext.WorkTags
                .AsNoTracking()
                .Where(tag => tagIdsForLookup.Contains(tag.Id))
                .ToDictionaryAsync(tag => tag.Id, tag => tag.Name, cancellationToken);

        foreach (var entry in session.DbContext.ChangeTracker.Entries<WorkTag>())
        {
            if (entry.State == EntityState.Added && desired.Contains(entry.Entity.Id))
            {
                tagNames[entry.Entity.Id] = entry.Entity.Name;
            }
        }

        foreach (var link in existing.Where(item => !desired.Contains(item.TagId)))
        {
            session.DbContext.WorkTaskTags.Remove(link);
            var removedName = tagNames.GetValueOrDefault(link.TagId);
            RecordActivity(session, task, WorkTaskActivityType.TagRemoved, removedName, null);
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
            var addedName = tagNames.GetValueOrDefault(tagId);
            RecordActivity(session, task, WorkTaskActivityType.TagAdded, null, addedName);
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
        WorkTask task,
        CancellationToken cancellationToken)
    {
        MarkExternalEngagementIfNeeded(session, task);

        var existing = await session.DbContext.WorkTaskReadStates
            .FirstOrDefaultAsync(
                state => state.TaskId == task.Id && state.MembershipId == session.MembershipId,
                cancellationToken);
        if (existing is null)
        {
            session.DbContext.WorkTaskReadStates.Add(new WorkTaskReadState
            {
                TenantId = session.TenantId,
                TaskId = task.Id,
                MembershipId = session.MembershipId,
                LastViewedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        existing.LastViewedAtUtc = DateTimeOffset.UtcNow;
    }

    public static void MarkExternalEngagementIfNeeded(TenantRlsSession session, WorkTask task)
    {
        if (session.MembershipId != task.CreatedByMembershipId)
        {
            task.HasExternalEngagement = true;
        }
    }

    public static async Task<bool> HasNonCreatorEngagementAsync(
        TenantRlsSession session,
        WorkTask task,
        CancellationToken cancellationToken)
    {
        var engagement = await GetNonCreatorEngagementByTaskAsync(session, [task], cancellationToken);
        return engagement.GetValueOrDefault(task.Id, false);
    }

    public static async Task<Dictionary<Guid, bool>> GetNonCreatorEngagementByTaskAsync(
        TenantRlsSession session,
        IReadOnlyList<WorkTask> tasks,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var engaged = tasks.ToDictionary(task => task.Id, task => task.HasExternalEngagement);
        var pendingIds = tasks.Where(task => !task.HasExternalEngagement).Select(task => task.Id).ToArray();
        if (pendingIds.Length == 0)
        {
            return engaged;
        }

        var creatorByTask = tasks.ToDictionary(task => task.Id, task => task.CreatedByMembershipId);

        var commented = await session.DbContext.WorkTaskComments
            .AsNoTracking()
            .Where(comment => pendingIds.Contains(comment.TaskId))
            .Select(comment => new { comment.TaskId, comment.AuthorMembershipId })
            .ToListAsync(cancellationToken);
        foreach (var row in commented)
        {
            if (creatorByTask[row.TaskId] != row.AuthorMembershipId)
            {
                engaged[row.TaskId] = true;
            }
        }

        var stillPending = pendingIds.Where(id => !engaged[id]).ToArray();
        if (stillPending.Length == 0)
        {
            return engaged;
        }

        var activities = await session.DbContext.WorkTaskActivities
            .AsNoTracking()
            .Where(activity => stillPending.Contains(activity.TaskId))
            .Select(activity => new { activity.TaskId, activity.ActorMembershipId })
            .ToListAsync(cancellationToken);
        foreach (var row in activities)
        {
            if (creatorByTask[row.TaskId] != row.ActorMembershipId)
            {
                engaged[row.TaskId] = true;
            }
        }

        return engaged;
    }

    public static string? DescribeDeleteBlockedReason(TaskSubject subject, bool hasNonCreatorEngagement)
    {
        if (!subject.HasWorkspaceEdit)
        {
            return "task_delete_forbidden";
        }

        if (hasNonCreatorEngagement)
        {
            return "task_seen_by_another_member";
        }

        return null;
    }

    public static TaskSubject DescribeSubject(
        TenantRlsSession session,
        TaskAuthorizationService authorization,
        WorkspaceAuthorizationService workspaceAuthorization,
        WorkspaceAccessLevel? explicitAccess,
        WorkTask task)
    {
        var hasView = workspaceAuthorization.CanViewTask(session.HasImplicitFullResourceAccess, explicitAccess);
        var hasEdit = workspaceAuthorization.CanEditTask(session.HasImplicitFullResourceAccess, explicitAccess);
        return authorization.Describe(session.MembershipId, task, hasView, hasEdit);
    }

    public static async Task<IReadOnlyList<WorkTaskResponse>> ToTaskResponsesAsync(
        TenantRlsSession session,
        TaskAuthorizationService authorization,
        WorkspaceAuthorizationService workspaceAuthorization,
        WorkspaceAccessLevel? explicitAccess,
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

        var engagementByTask = await GetNonCreatorEngagementByTaskAsync(session, tasks, cancellationToken);

        if (markSelectedSeen && tasks.Count == 1)
        {
            await MarkSeenAsync(session, tasks[0], cancellationToken);
        }

        return tasks.Select(task =>
        {
            personById.TryGetValue(task.CreatedByMembershipId, out var creator);
            personById.TryGetValue(task.AssignedMembershipId ?? Guid.Empty, out var assignee);
            tagsByTask.TryGetValue(task.Id, out var tags);
            unseenByTask.TryGetValue(task.Id, out var unseen);
            var subject = DescribeSubject(
                session, authorization, workspaceAuthorization, explicitAccess, task);
            engagementByTask.TryGetValue(task.Id, out var hasEngagement);
            var canDelete = authorization.CanDelete(subject, hasEngagement);
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
                    canDelete,
                    authorization.AllowedStatuses(subject, task.Status).Select(status => status.ToString()).ToArray(),
                    DescribeDeleteBlockedReason(subject, hasEngagement)));
        }).ToArray();
    }

    private static string? Truncate(string? value)
        => value is { Length: > WorkTaskActivityConfiguration.ValueMaxLength }
            ? value[..WorkTaskActivityConfiguration.ValueMaxLength]
            : value;
}
