using Microsoft.EntityFrameworkCore.Storage;
using PTS.Host.Persistence;

namespace PTS.Host.TenantAccess;

/// <summary>
/// User-scoped unit of work with only <c>app.current_user_id</c> set via
/// SET LOCAL. Safe for pooled connections because the GUC is transaction-local.
/// </summary>
public sealed class UserRlsSession : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;
    private bool _completed;

    internal UserRlsSession(AppDbContext dbContext, IDbContextTransaction transaction, Guid userId)
    {
        DbContext = dbContext;
        _transaction = transaction;
        UserId = userId;
    }

    public AppDbContext DbContext { get; }

    public Guid UserId { get; }

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
                // Best-effort cleanup when the connection may already be broken.
            }
        }

        await _transaction.DisposeAsync();
        await DbContext.DisposeAsync();
    }
}
