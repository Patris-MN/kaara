namespace PTS.SharedKernel.Tenancy;

/// <summary>
/// Read-only accessor for "which tenant is this unit of work operating as",
/// once server-side resolution has established it. Consuming modules read
/// this; they never assign to it — establishment is the exclusive
/// responsibility of the Tenancy module's resolver plus the composition
/// root's RLS-session bridge (see docs/architecture/decisions/0004-....md).
///
/// A missing/null <see cref="TenantId"/> means no tenant context has been
/// established for the current unit of work — code must treat that as "no
/// tenant", never fall back to a default or a previous value.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}
