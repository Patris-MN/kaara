namespace PTS.SharedKernel.Entitlements;

/// <summary>
/// Authoritative global entitlement for Organization creation. Billing/Plan
/// implementations will replace the development provider in a later phase.
/// </summary>
public interface IOrganizationCreationEntitlementProvider
{
    Task<OrganizationCreationEntitlement> GetForCurrentUserAsync(
        CancellationToken cancellationToken = default);
}
