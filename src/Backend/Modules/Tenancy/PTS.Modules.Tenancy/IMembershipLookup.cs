namespace PTS.Modules.Tenancy;

/// <summary>
/// A narrow, single-purpose persistence port: "does this user have an active
/// membership for this tenant, and if so what is it". This is deliberately
/// NOT a generic repository (no <c>IRepository&lt;T&gt;</c>, no CRUD surface) —
/// it exists solely because <see cref="TenantContextResolver"/> needs exactly
/// this one query, and the Tenancy module cannot reference the composition
/// root's concrete DbContext (module boundary rule: modules never reference
/// the Host). The Host implements this interface against EF Core; see
/// docs/architecture/decisions/0004-tenancy-ports-and-adapters-for-persistence.md.
/// </summary>
public interface IMembershipLookup
{
    Task<Membership?> FindActiveMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Any status for the (user, tenant) pair. Used by invitation lifecycle.
    /// The Host adapter still SET LOCALs <c>app.current_user_id</c> to
    /// <paramref name="userId"/> so memberships RLS (self) can see the row.
    /// </summary>
    Task<Membership?> FindMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
