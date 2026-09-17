namespace PTS.Modules.WorkManagement;

/// <summary>
/// Task-level actions after Workspace access has already been granted.
/// Workspace Edit is required for every mutation and for delete; this service decides field rights.
/// </summary>
public sealed class TaskAuthorizationService
{
    private readonly TaskStatusWorkflow _workflow;

    public TaskAuthorizationService(TaskStatusWorkflow workflow)
    {
        _workflow = workflow;
    }

    public TaskSubject Describe(
        Guid membershipId,
        WorkTask task,
        bool hasWorkspaceView,
        bool hasWorkspaceEdit)
        => new(
            membershipId,
            hasWorkspaceView,
            hasWorkspaceEdit,
            task.CreatedByMembershipId == membershipId,
            task.AssignedMembershipId == membershipId);

    public bool CanView(TaskSubject subject) => subject.HasWorkspaceView;

    public bool CanEditDefinition(TaskSubject subject, WorkTaskStatus status)
        => subject.HasWorkspaceEdit && subject.IsCreator && !_workflow.IsClosed(status);

    public bool CanManageTags(TaskSubject subject, WorkTaskStatus status)
        => subject.HasWorkspaceEdit
            && (subject.IsCreator || subject.IsCurrentAssignee)
            && !_workflow.IsClosed(status);

    public bool CanReassign(TaskSubject subject, WorkTaskStatus status)
        => subject.HasWorkspaceEdit
            && (subject.IsCreator || subject.IsCurrentAssignee)
            && !_workflow.IsClosed(status);

    public bool CanComment(TaskSubject subject, WorkTaskStatus status)
        => subject.HasWorkspaceEdit
            && subject.HasWorkspaceView
            && (!_workflow.IsClosed(status) || subject.IsCreator);

    public bool CanEditOwnComment(TaskSubject subject, Guid authorMembershipId)
        => subject.HasWorkspaceEdit
            && subject.HasWorkspaceView
            && subject.MembershipId == authorMembershipId;

    public bool CanDelete(TaskSubject subject, bool hasNonCreatorEngagement = false)
        => subject.HasWorkspaceEdit && !hasNonCreatorEngagement;

    public bool CanChangeStatus(TaskSubject subject, WorkTaskStatus from, WorkTaskStatus to)
        => subject.HasWorkspaceEdit
            && subject.HasWorkspaceView
            && _workflow.CanTransition(subject.IsCreator, subject.IsCurrentAssignee, from, to);

    public IReadOnlyList<WorkTaskStatus> AllowedStatuses(TaskSubject subject, WorkTaskStatus current)
    {
        return Enum.GetValues<WorkTaskStatus>()
            .Where(status => CanChangeStatus(subject, current, status))
            .ToArray();
    }
}

public readonly record struct TaskSubject(
    Guid MembershipId,
    bool HasWorkspaceView,
    bool HasWorkspaceEdit,
    bool IsCreator,
    bool IsCurrentAssignee);
