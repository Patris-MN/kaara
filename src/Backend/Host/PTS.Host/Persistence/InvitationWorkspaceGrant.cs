using PTS.Modules.WorkManagement;

namespace PTS.Host.Persistence;

/// <summary>
/// Pending workspace access to apply when an invitation is accepted.
/// Host-composed because it spans Tenancy invitations and WorkManagement access.
/// </summary>
public class InvitationWorkspaceGrant
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid InvitationId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>Snapshot for unauthenticated invitation preview.</summary>
    public required string WorkspaceName { get; set; }

    public WorkspaceAccessLevel AccessLevel { get; set; }
}
