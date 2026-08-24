using PTS.SharedKernel.Tenancy;

namespace PTS.Modules.Tenancy;

/// <summary>
/// Scoped (per unit-of-work) holder of the server-established tenant. Register
/// one instance per scope, exposed to consumers as the read-only
/// <see cref="ITenantContext"/> and to the RLS-session bridge as the
/// write-capable <see cref="ITenantContextEstablisher"/> — see
/// TenancyModuleExtensions for the DI wiring.
///
/// Once set, a tenant cannot be silently swapped out from under a unit of
/// work: <see cref="Establish"/> throws if called again with a different
/// tenant, which is the "cannot be changed under you" guarantee Step 7/13
/// require against spoofing mid-flight.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantContextEstablisher
{
    public Guid? TenantId { get; private set; }

    public void Establish(Guid tenantId)
    {
        if (TenantId is Guid existing && existing != tenantId)
        {
            throw new InvalidOperationException(
                $"Tenant context for this scope is already established as '{existing}' " +
                $"and cannot be changed to '{tenantId}'.");
        }

        TenantId = tenantId;
    }
}
