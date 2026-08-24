using Microsoft.EntityFrameworkCore.Storage;
using PTS.Host.Persistence;

namespace PTS.Host.TenantAccess;

/// <summary>
/// A single tenant-scoped unit of work: one <see cref="AppDbContext"/>
/// instance (from the pooled <c>IDbContextFactory</c>) plus one open
/// transaction that has had <c>app.current_tenant_id</c> set via
/// <c>SET LOCAL</c> for exactly this transaction. See
/// <see cref="TenantRlsSessionFactory"/> for how it's created.
///
/// Disposing without committing rolls back — and because PostgreSQL clears
/// <c>SET LOCAL</c> values at both COMMIT and ROLLBACK, the tenant setting
/// can never survive past this session, regardless of how the caller exits
/// (success, exception, or explicit rollback). That is what makes it safe for
/// the underlying physical connection to go back into the pool afterward and
/// be reused by a completely different tenant's session.
/// </summary>
public sealed class TenantRlsSession : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;
    private bool _completed;

    internal TenantRlsSession(AppDbContext dbContext, IDbContextTransaction transaction, Guid tenantId)
    {
        DbContext = dbContext;
        _transaction = transaction;
        TenantId = tenantId;
    }

    public AppDbContext DbContext { get; }

    public Guid TenantId { get; }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch
            {
                // The connection may already be broken (e.g. the caller's own
                // exception came from a connection fault) — safe to ignore
                // during best-effort cleanup; disposing the transaction/context
                // below still returns the connection to the pool.
            }
        }

        await _transaction.DisposeAsync();
        await DbContext.DisposeAsync();
    }
}
