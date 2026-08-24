using PTS.Modules.Identity;
using PTS.SharedKernel.Identity;

namespace PTS.Host.TenantAccess;

/// <summary>
/// The bridge between the application's server-side tenant-context
/// resolution (PTS.Modules.Tenancy.ITenantContextResolver) and PostgreSQL Row
/// -Level Security.
///
/// UserId is taken from <see cref="ICurrentUser"/> (validated authentication
/// principal) — never from a caller-supplied argument.
/// </summary>
public interface ITenantRlsSessionFactory
{
    /// <summary>
    /// Resolves tenant context for the authenticated user + requested tenant
    /// via Membership validation, then opens a tenant-scoped transaction with
    /// both <c>app.current_user_id</c> and <c>app.current_tenant_id</c> set
    /// via SET LOCAL. Throws <see cref="AuthenticationRequiredException"/> if
    /// unauthenticated, <see cref="UnknownAuthenticatedUserException"/> if the
    /// principal does not map to a User row, and
    /// <see cref="TenantAccessDeniedException"/> if the user has no active
    /// Membership for the requested tenant.
    /// </summary>
    Task<TenantRlsSession> OpenAsync(Guid requestedTenantId, CancellationToken cancellationToken = default);
}
