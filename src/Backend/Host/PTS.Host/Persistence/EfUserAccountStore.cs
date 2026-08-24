using Microsoft.EntityFrameworkCore;
using PTS.Modules.Identity;

namespace PTS.Host.Persistence;

internal sealed class EfUserAccountStore : IUserAccountStore
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public EfUserAccountStore(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task AddAsync(User user, UserCredential credential, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, user.Id, cancellationToken);

        db.Users.Add(user);
        db.UserCredentials.Add(credential);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UserCredential?> FindCredentialByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == email, cancellationToken);
    }

    public async Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PostgresRlsSettings.SetCurrentUserIdAsync(db, userId, cancellationToken);

        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }
}
