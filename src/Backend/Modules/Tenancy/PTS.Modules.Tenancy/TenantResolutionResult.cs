namespace PTS.Modules.Tenancy;

/// <summary>Outcome of <see cref="ITenantContextResolver.ResolveAsync"/>. Never
/// constructed from client input directly — always the result of an actual
/// Membership lookup.</summary>
public sealed record TenantResolutionResult
{
    public bool Success { get; }
    public Guid? TenantId { get; }
    public Guid? MembershipId { get; }
    public MembershipRole? Role { get; }
    public bool HasImplicitFullResourceAccess =>
        Role is MembershipRole.Owner or MembershipRole.Admin;
    public string? FailureReason { get; }

    private TenantResolutionResult(
        bool success,
        Guid? tenantId,
        Guid? membershipId,
        MembershipRole? role,
        string? failureReason)
    {
        Success = success;
        TenantId = tenantId;
        MembershipId = membershipId;
        Role = role;
        FailureReason = failureReason;
    }

    public static TenantResolutionResult Allowed(Membership membership)
        => new(true, membership.TenantId, membership.Id, membership.Role, null);

    public static TenantResolutionResult Denied(string reason)
        => new(false, null, null, null, reason);
}
