namespace PTS.Modules.WorkManagement;

/// <summary>
/// Access granted to an Active tenant Member for a Workspace. Owner and Admin
/// memberships have implicit full access and do not require rows.
/// </summary>
public enum WorkspaceAccessLevel
{
    View,
    Edit,
}
