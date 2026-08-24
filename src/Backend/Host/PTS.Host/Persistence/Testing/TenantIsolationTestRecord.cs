namespace PTS.Host.Persistence.Testing;

/// <summary>
/// Exists solely to prove PostgreSQL Row-Level Security tenant isolation
/// end-to-end (see tests/Backend/PTS.IntegrationTests). This is NOT a business
/// entity and carries no product meaning — it lives in the Host, not in any
/// module, precisely so nobody mistakes it for one. Expect it to be retired
/// once a real tenant-owned WorkManagement table can serve as the isolation
/// proof instead (see docs/architecture/architecture-charter.md §4.2).
/// </summary>
public class TenantIsolationTestRecord
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
