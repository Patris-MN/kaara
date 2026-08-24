using Microsoft.Extensions.DependencyInjection;
using PTS.Host.Persistence;
using PTS.Host.TenantAccess;

namespace PTS.IntegrationTests;

/// <summary>
/// Wraps a <see cref="TenantRlsSession"/> together with the
/// <see cref="IServiceScope"/> it was resolved from, so each simulated
/// "request" in these tests gets its own DI scope — exactly like ASP.NET Core
/// creates a fresh scope per HTTP request. This matters concretely:
/// <c>TenantContext</c> is registered Scoped, and resolving
/// <see cref="ITenantRlsSessionFactory"/> repeatedly from the fixture's root
/// container (without a scope per call) would reuse the same TenantContext
/// instance across what should be independent requests — which
/// <c>TenantContext.Establish</c>'s "already established" guard correctly
/// rejects. Every test in this project goes through this helper instead of
/// calling the root container directly, for that reason.
/// </summary>
public sealed class ScopedTenantSession : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly TenantRlsSession _session;

    private ScopedTenantSession(IServiceScope scope, TenantRlsSession session)
    {
        _scope = scope;
        _session = session;
    }

    public static async Task<ScopedTenantSession> OpenAsync(IServiceProvider services, Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var scope = services.CreateScope();
        try
        {
            var currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
            currentUser.AuthenticateAs(userId);
            var sessions = scope.ServiceProvider.GetRequiredService<ITenantRlsSessionFactory>();
            var session = await sessions.OpenAsync(tenantId, cancellationToken);
            return new ScopedTenantSession(scope, session);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public AppDbContext DbContext => _session.DbContext;

    public Guid TenantId => _session.TenantId;

    public Task CommitAsync(CancellationToken cancellationToken = default) => _session.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) => _session.RollbackAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        _scope.Dispose();
    }
}
