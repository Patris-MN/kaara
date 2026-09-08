namespace PTS.Modules.Tenancy;

/// <summary>Lifecycle state of a <see cref="Membership"/>. Only <see cref="Active"/>
/// memberships are honored when resolving tenant context — see
/// <see cref="TenantContextResolver"/>.</summary>
public enum MembershipStatus
{
    Invited,
    Active,
    Suspended,

    /// <summary>
    /// Terminal organization access state. Historical task/comment references
    /// remain intact; the user cannot act in the tenant again.
    /// </summary>
    Removed,
}
