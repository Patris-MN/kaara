namespace PTS.Modules.Tenancy;

/// <summary>
/// The write side of tenant-context establishment, deliberately kept separate
/// from <see cref="PTS.SharedKernel.Tenancy.ITenantContext"/> (the read side)
/// so that only trusted resolution code (the composition root's RLS-session
/// bridge, after a successful <see cref="ITenantContextResolver"/> result) can
/// ever call <see cref="Establish"/> — ordinary consumers only ever see the
/// read-only interface via DI.
/// </summary>
public interface ITenantContextEstablisher
{
    void Establish(Guid tenantId);
}
