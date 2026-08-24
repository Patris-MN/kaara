namespace PTS.Modules.WorkManagement;

/// <summary>
/// A tenant-owned project that belongs to exactly one <see cref="Workspace"/>.
/// <see cref="TenantId"/> is denormalized so PostgreSQL can both enforce RLS
/// and prevent a project from referencing a workspace in another tenant via a
/// composite foreign key (TenantId, WorkspaceId).
/// </summary>
public class Project
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
