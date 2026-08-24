namespace PTS.Modules.Tenancy;

/// <summary>
/// Links a global <c>User</c> (Identity module) to a <see cref="Tenant"/>,
/// carrying the tenant-scoped role and status. A user may hold many
/// Memberships (one per tenant they belong to).
///
/// This type deliberately does not reference the Identity module's <c>User</c>
/// type — <see cref="UserId"/> is a plain scalar. The database-level foreign
/// key to <c>users</c> is declared in the composition root's DbContext
/// (<c>PTS.Host.Persistence.AppDbContext</c>), not here, so that Tenancy never
/// takes a compile-time dependency on Identity purely to describe a
/// relationship — see
/// docs/architecture/decisions/0004-tenancy-ports-and-adapters-for-persistence.md.
/// </summary>
public class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public MembershipRole Role { get; set; }

    public MembershipStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
