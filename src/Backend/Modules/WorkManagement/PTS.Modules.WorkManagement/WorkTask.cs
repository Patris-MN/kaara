namespace PTS.Modules.WorkManagement;

/// <summary>
/// A tenant-owned work item that belongs to exactly one <see cref="Project"/>.
/// Named <see cref="WorkTask"/> so it does not collide with
/// <c>System.Threading.Tasks.Task</c>. Hierarchy is denormalized
/// (TenantId + WorkspaceId + ProjectId) so PostgreSQL can enforce both RLS
/// and a tenant-safe composite foreign key. Parent ids are immutable after
/// create — Phase 6 does not move tasks between projects.
/// </summary>
public sealed class WorkTask
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid ProjectId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public WorkTaskStatus Status { get; set; }

    public WorkTaskPriority Priority { get; set; }

    /// <summary>Optional calendar deadline (date only, no timezone instant).</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// Permanent originator. References a tenant Membership and never changes.
    /// </summary>
    public Guid CreatedByMembershipId { get; set; }

    /// <summary>
    /// Optional current assignee. References a tenant Membership, never a User.
    /// </summary>
    public Guid? AssignedMembershipId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
