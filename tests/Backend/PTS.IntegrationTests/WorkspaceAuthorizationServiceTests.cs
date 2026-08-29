namespace PTS.IntegrationTests;

public sealed class WorkspaceAuthorizationServiceTests
{
    private readonly PTS.Modules.WorkManagement.WorkspaceAuthorizationService _authorization = new();

    [Theory]
    [InlineData(true, null, true, true)]
    [InlineData(false, PTS.Modules.WorkManagement.WorkspaceAccessLevel.View, true, false)]
    [InlineData(false, PTS.Modules.WorkManagement.WorkspaceAccessLevel.Edit, true, true)]
    [InlineData(false, null, false, false)]
    public void Workspace_and_project_checks_follow_the_same_view_edit_matrix(
        bool implicitFullAccess,
        PTS.Modules.WorkManagement.WorkspaceAccessLevel? explicitAccess,
        bool canView,
        bool canEdit)
    {
        Assert.Equal(canView, _authorization.CanViewWorkspace(implicitFullAccess, explicitAccess));
        Assert.Equal(canEdit, _authorization.CanEditWorkspace(implicitFullAccess, explicitAccess));
        Assert.Equal(canView, _authorization.CanViewProject(implicitFullAccess, explicitAccess));
        Assert.Equal(canEdit, _authorization.CanEditProject(implicitFullAccess, explicitAccess));
        Assert.Equal(canView, _authorization.CanViewTask(implicitFullAccess, explicitAccess));
        Assert.Equal(canEdit, _authorization.CanEditTask(implicitFullAccess, explicitAccess));
        Assert.Equal(canEdit, _authorization.CanAssignTask(implicitFullAccess, explicitAccess));
        Assert.Equal(canEdit, _authorization.CanMutateTaskTags(implicitFullAccess, explicitAccess));
        Assert.Equal(canView, _authorization.IsAssignableMember(true, implicitFullAccess, explicitAccess));
        Assert.False(_authorization.IsAssignableMember(false, implicitFullAccess, explicitAccess));
        Assert.Equal(implicitFullAccess, _authorization.CanCreateWorkspace(implicitFullAccess));
        Assert.Equal(implicitFullAccess, _authorization.CanManageAccess(implicitFullAccess));
    }
}
