namespace PTS.Modules.Tenancy;

/// <summary>
/// An organization/customer. This is the root of tenant-owned data — its own
/// <see cref="Id"/> is the <c>TenantId</c> referenced by every tenant-owned
/// record elsewhere in the system.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>URL-safe, unique, human-chosen identifier for the tenant.</summary>
    public required string Slug { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
