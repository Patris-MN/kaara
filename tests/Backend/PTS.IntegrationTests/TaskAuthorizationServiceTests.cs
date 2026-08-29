using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

public sealed class TaskAuthorizationServiceTests
{
    private readonly TaskStatusWorkflow _workflow = new();
    private readonly TaskAuthorizationService _authorization;

    public TaskAuthorizationServiceTests()
    {
        _authorization = new TaskAuthorizationService(_workflow);
    }

    [Fact]
    public void Creator_owns_definition_status_tags_reassign_and_delete()
    {
        var task = TaskFor(creator: Id(1), assignee: Id(2));
        var subject = _authorization.Describe(Id(1), task, hasWorkspaceView: true);

        Assert.True(_authorization.CanView(subject));
        Assert.True(_authorization.CanEditDefinition(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanManageTags(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanReassign(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanComment(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanComment(subject, WorkTaskStatus.Closed));
        Assert.True(_authorization.CanDelete(subject));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.Open, WorkTaskStatus.Closed));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.Closed, WorkTaskStatus.Open));
        Assert.False(_authorization.CanEditDefinition(subject, WorkTaskStatus.Closed));
    }

    [Fact]
    public void Current_assignee_can_collaborate_but_cannot_rewrite_or_close()
    {
        var task = TaskFor(creator: Id(1), assignee: Id(2));
        var subject = _authorization.Describe(Id(2), task, hasWorkspaceView: true);

        Assert.True(_authorization.CanView(subject));
        Assert.False(_authorization.CanEditDefinition(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanManageTags(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanReassign(subject, WorkTaskStatus.Open));
        Assert.True(_authorization.CanComment(subject, WorkTaskStatus.Open));
        Assert.False(_authorization.CanComment(subject, WorkTaskStatus.Closed));
        Assert.False(_authorization.CanDelete(subject));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.Open, WorkTaskStatus.InProgress));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.InProgress, WorkTaskStatus.Waiting));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.Waiting, WorkTaskStatus.Resolved));
        Assert.True(_authorization.CanChangeStatus(subject, WorkTaskStatus.Open, WorkTaskStatus.Resolved));
        Assert.False(_authorization.CanChangeStatus(subject, WorkTaskStatus.Resolved, WorkTaskStatus.Closed));
        Assert.False(_authorization.CanChangeStatus(subject, WorkTaskStatus.Closed, WorkTaskStatus.Open));
    }

    [Fact]
    public void Previous_assignee_and_view_member_are_read_and_comment_only()
    {
        var task = TaskFor(creator: Id(1), assignee: Id(3));
        var previous = _authorization.Describe(Id(2), task, hasWorkspaceView: true);
        var viewer = _authorization.Describe(Id(4), task, hasWorkspaceView: true);

        Assert.True(_authorization.CanView(previous));
        Assert.True(_authorization.CanComment(previous, WorkTaskStatus.Open));
        Assert.False(_authorization.CanManageTags(previous, WorkTaskStatus.Open));
        Assert.False(_authorization.CanReassign(previous, WorkTaskStatus.Open));
        Assert.False(_authorization.CanChangeStatus(previous, WorkTaskStatus.Open, WorkTaskStatus.InProgress));
        Assert.False(_authorization.CanDelete(previous));
        Assert.True(_authorization.CanComment(viewer, WorkTaskStatus.Open));
        Assert.False(_authorization.CanEditDefinition(viewer, WorkTaskStatus.Open));
        Assert.True(_authorization.CanEditOwnComment(viewer, Id(4)));
        Assert.False(_authorization.CanEditOwnComment(viewer, Id(1)));
    }

    [Fact]
    public void Status_workflow_maps_legacy_aliases_and_keeps_closed_as_source_of_truth()
    {
        Assert.Equal(WorkTaskStatus.Open, TaskStatusWorkflow.ParseOrDefault("Todo", out var todo));
        Assert.True(todo);
        Assert.Equal(WorkTaskStatus.Closed, TaskStatusWorkflow.ParseOrDefault("Done", out var done));
        Assert.True(done);
        Assert.True(_workflow.IsClosed(WorkTaskStatus.Closed));
        Assert.False(_workflow.IsClosed(WorkTaskStatus.Resolved));
        Assert.False(_workflow.CanTransition(isCreator: false, isCurrentAssignee: true, WorkTaskStatus.Closed, WorkTaskStatus.Open));
        Assert.True(_workflow.CanTransition(isCreator: true, isCurrentAssignee: false, WorkTaskStatus.Closed, WorkTaskStatus.Open));
    }

    private static Guid Id(byte value) => new(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static WorkTask TaskFor(Guid creator, Guid assignee) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Ticket",
        CreatedByMembershipId = creator,
        AssignedMembershipId = assignee,
        Status = WorkTaskStatus.Open,
    };
}
