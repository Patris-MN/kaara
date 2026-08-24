namespace PTS.Modules.Identity;

/// <summary>
/// A global platform identity — a person's account across the entire platform.
///
/// Deliberately carries no <c>TenantId</c> and no tenant-scoped role. Which
/// tenants this user belongs to, and what role they hold in each, is owned
/// entirely by <see cref="PTS.Modules.Tenancy.Membership"/> in the Tenancy
/// module — see .cursor/rules/20-identity-membership-platform-admin.mdc.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
