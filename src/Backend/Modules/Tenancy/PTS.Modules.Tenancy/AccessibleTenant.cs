namespace PTS.Modules.Tenancy;

/// <summary>
/// A tenant the current user can see, plus their membership role/status.
/// Used by listing endpoints — not an authorization decision on its own.
/// </summary>
public sealed record AccessibleTenant(
    Guid TenantId,
    string Name,
    string Slug,
    MembershipRole Role,
    MembershipStatus Status);
