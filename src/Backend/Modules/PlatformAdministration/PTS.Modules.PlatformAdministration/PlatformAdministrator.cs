namespace PTS.Modules.PlatformAdministration;

/// <summary>
/// Grants a global <c>User</c> permission to operate the SaaS itself.
/// This is not a tenant <c>Membership</c> role and is not a property on
/// <c>User</c> — see .cursor/rules/20-identity-membership-platform-admin.mdc.
/// </summary>
public sealed class PlatformAdministrator
{
    public Guid UserId { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; }
}
