namespace PTS.Modules.WorkManagement;

/// <summary>
/// Central policy for WorkManagement resource authorization. Tenant role and
/// membership status remain Tenancy concerns; the Host supplies a subject
/// derived from the freshly resolved Active Membership.
/// </summary>
public sealed class WorkspaceAuthorizationService
{
    public bool CanView(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => hasImplicitFullAccess || explicitAccess is WorkspaceAccessLevel.View or WorkspaceAccessLevel.Edit;

    public bool CanEdit(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => hasImplicitFullAccess || explicitAccess is WorkspaceAccessLevel.Edit;

    public bool CanViewWorkspace(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanView(hasImplicitFullAccess, explicitAccess);

    public bool CanEditWorkspace(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanEdit(hasImplicitFullAccess, explicitAccess);

    public bool CanViewProject(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanView(hasImplicitFullAccess, explicitAccess);

    public bool CanEditProject(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanEdit(hasImplicitFullAccess, explicitAccess);

    public bool CanViewTask(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanViewProject(hasImplicitFullAccess, explicitAccess);

    public bool CanEditTask(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanEditProject(hasImplicitFullAccess, explicitAccess);

    public bool CanAssignTask(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanEditTask(hasImplicitFullAccess, explicitAccess);

    public bool CanMutateTaskTags(bool hasImplicitFullAccess, WorkspaceAccessLevel? explicitAccess)
        => CanEditTask(hasImplicitFullAccess, explicitAccess);

    /// <summary>
    /// An assignee must be an active tenant member who can already view the
    /// Task's Workspace (Owner/Admin implicit access, or Member View/Edit).
    /// </summary>
    public bool IsAssignableMember(
        bool isActive,
        bool hasImplicitFullAccess,
        WorkspaceAccessLevel? explicitAccess)
        => isActive && CanViewTask(hasImplicitFullAccess, explicitAccess);

    public bool CanCreateWorkspace(bool hasImplicitFullAccess)
        => hasImplicitFullAccess;

    public bool CanManageAccess(bool hasImplicitFullAccess)
        => hasImplicitFullAccess;
}
