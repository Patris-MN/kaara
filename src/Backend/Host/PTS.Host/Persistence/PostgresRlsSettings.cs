using Microsoft.EntityFrameworkCore;

namespace PTS.Host.Persistence;

/// <summary>
/// Transaction-local PostgreSQL GUCs that RLS policies read. Always passed
/// with <c>is_local = true</c> (SET LOCAL) so a pooled connection cannot leak
/// identity or tenant context into the next request.
/// </summary>
internal static class PostgresRlsSettings
{
    public static Task SetCurrentUserIdAsync(DbContext dbContext, Guid userId, CancellationToken cancellationToken)
        => dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_user_id', {userId.ToString()}, true)",
            cancellationToken);

    public static Task SetCurrentTenantIdAsync(DbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
        => dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
            cancellationToken);
}
