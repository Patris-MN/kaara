namespace PTS.Modules.Tenancy;

/// <summary>
/// Secure invitation-link record for onboarding into a tenant.
/// Possession of the raw token proves invite intent; acceptance still
/// requires authenticated identity matching <see cref="InvitedEmail"/>.
/// </summary>
public class TenantInvitation
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Normalized recipient email (lowercase, trimmed).</summary>
    public required string InvitedEmail { get; set; }

    public MembershipRole Role { get; set; } = MembershipRole.Member;

    /// <summary>SHA-256 hex hash of the opaque raw token.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid CreatedByMembershipId { get; set; }

    /// <summary>Snapshot for unauthenticated invitation preview.</summary>
    public required string OrganizationName { get; set; }

    /// <summary>Snapshot for unauthenticated invitation preview.</summary>
    public string? InviterDisplayName { get; set; }

    /// <summary>Set when the invitee already had a User at invite time.</summary>
    public Guid? InviteeUserId { get; set; }

    /// <summary>Linked Invited membership when invitee User existed at invite time.</summary>
    public Guid? MembershipId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
