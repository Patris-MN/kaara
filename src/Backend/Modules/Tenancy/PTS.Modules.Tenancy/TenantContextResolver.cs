namespace PTS.Modules.Tenancy;

/// <summary>
/// Default <see cref="ITenantContextResolver"/>: grants access if and only if
/// an active <see cref="Membership"/> row exists for (userId, requestedTenantId).
/// Nothing else — not the caller's say-so, not a header, not a cookie — can
/// produce an <see cref="TenantResolutionResult.Success"/> result.
/// </summary>
public sealed class TenantContextResolver : ITenantContextResolver
{
    private readonly IMembershipLookup _membershipLookup;

    public TenantContextResolver(IMembershipLookup membershipLookup)
    {
        _membershipLookup = membershipLookup;
    }

    public async Task<TenantResolutionResult> ResolveAsync(Guid userId, Guid requestedTenantId, CancellationToken cancellationToken = default)
    {
        var membership = await _membershipLookup.FindActiveMembershipAsync(userId, requestedTenantId, cancellationToken);

        return membership is null
            ? TenantResolutionResult.Denied(
                $"User '{userId}' has no active membership for tenant '{requestedTenantId}'.")
            : TenantResolutionResult.Allowed(requestedTenantId);
    }
}
