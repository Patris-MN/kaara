namespace PTS.Host.TenantAccess;

/// <summary>
/// Thrown by <see cref="ITenantRlsSessionFactory"/> when a user requests a
/// tenant they have no active Membership for — this is the concrete "DENIED"
/// outcome Step 13's tenant-spoofing test asserts on.
/// </summary>
public sealed class TenantAccessDeniedException : Exception
{
    public Guid UserId { get; }

    public Guid RequestedTenantId { get; }

    public TenantAccessDeniedException(Guid userId, Guid requestedTenantId, string reason)
        : base($"User '{userId}' was denied access to tenant '{requestedTenantId}': {reason}")
    {
        UserId = userId;
        RequestedTenantId = requestedTenantId;
    }
}
