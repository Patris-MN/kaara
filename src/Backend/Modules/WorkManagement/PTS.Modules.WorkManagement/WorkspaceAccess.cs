namespace PTS.Modules.WorkManagement;

/// <summary>
/// Explicit Workspace permission for a tenant Membership. Tenant-safe
/// relationships to Membership and Workspace are enforced by composite FKs.
/// </summary>
public sealed class WorkspaceAccess
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid MembershipId { get; set; }

    public Guid WorkspaceId { get; set; }

    public WorkspaceAccessLevel AccessLevel { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
