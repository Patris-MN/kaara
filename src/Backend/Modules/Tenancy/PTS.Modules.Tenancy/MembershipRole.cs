namespace PTS.Modules.Tenancy;

/// <summary>Tenant-scoped role. Unrelated to platform-administrator permissions
/// (see PTS.Modules.PlatformAdministration) — a Membership role never grants
/// platform-level access, and vice versa.</summary>
public enum MembershipRole
{
    Owner,
    Admin,
    Member,
}
