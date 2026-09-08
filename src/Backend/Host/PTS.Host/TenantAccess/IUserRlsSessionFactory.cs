using PTS.Modules.Identity;
using PTS.SharedKernel.Identity;

namespace PTS.Host.TenantAccess;

/// <summary>
/// Opens a transaction scoped to the authenticated user via
/// <c>app.current_user_id</c> only (no tenant GUC). Used for cross-tenant
/// inbox queries where RLS policies join through the user's memberships.
/// </summary>
public interface IUserRlsSessionFactory
{
    Task<UserRlsSession> OpenAsync(CancellationToken cancellationToken = default);
}
