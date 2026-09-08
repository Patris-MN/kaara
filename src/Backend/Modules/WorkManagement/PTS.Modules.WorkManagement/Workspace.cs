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

    /// <summary>Lowercased trimmed <see cref="Name"/> for duplicate detection.</summary>
    public string NameNormalized { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Last metadata change. Legacy rows may be null until backfilled; API mapping
    /// falls back to <see cref="CreatedAtUtc"/>.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
