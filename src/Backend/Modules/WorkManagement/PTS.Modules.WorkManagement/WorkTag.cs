namespace PTS.Modules.WorkManagement;

/// <summary>
/// Tenant-scoped reusable Task label. Names are unique per tenant after
/// case-insensitive normalization. CreatedByMembershipId records who defined
/// the tag; tags are visible to the tenant once created, not private.
/// </summary>
public sealed class WorkTag
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public required string NormalizedName { get; set; }

    public Guid CreatedByMembershipId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
