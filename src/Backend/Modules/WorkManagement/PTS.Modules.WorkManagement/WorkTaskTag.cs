namespace PTS.Modules.WorkManagement;

/// <summary>
/// Tenant-safe join between a <see cref="WorkTask"/> and a <see cref="WorkTag"/>.
/// </summary>
public sealed class WorkTaskTag
{
    public Guid TenantId { get; set; }

    public Guid TaskId { get; set; }

    public Guid TagId { get; set; }
}
