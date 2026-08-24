namespace PTS.Modules.WorkManagement;

/// <summary>
/// A tenant-owned container for projects. <see cref="TenantId"/> is mandatory
/// and is the tenancy boundary — never inferred from ambient memory alone.
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
