namespace PTS.Modules.Tenancy;

/// <summary>Outcome of <see cref="ITenantContextResolver.ResolveAsync"/>. Never
/// constructed from client input directly — always the result of an actual
/// Membership lookup.</summary>
public sealed record TenantResolutionResult
{
    public bool Success { get; }
    public Guid? TenantId { get; }
    public string? FailureReason { get; }

    private TenantResolutionResult(bool success, Guid? tenantId, string? failureReason)
    {
        Success = success;
        TenantId = tenantId;
        FailureReason = failureReason;
    }

    public static TenantResolutionResult Allowed(Guid tenantId) => new(true, tenantId, null);

    public static TenantResolutionResult Denied(string reason) => new(false, null, reason);
}
