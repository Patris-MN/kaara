using Microsoft.EntityFrameworkCore;
using PTS.Modules.PlatformAdministration;

namespace PTS.Host.Persistence;

internal sealed class EfPlatformAdministratorStore : IPlatformAdministratorStore
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public EfPlatformAdministratorStore(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<bool> IsPlatformAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PlatformAdministrators
            .AsNoTracking()
            .AnyAsync(a => a.UserId == userId, cancellationToken);
    }

    public async Task EnsureAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.PlatformAdministrators.AnyAsync(a => a.UserId == userId, cancellationToken))
        {
            return;
        }

        db.PlatformAdministrators.Add(new PlatformAdministrator
        {
            UserId = userId,
            GrantedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
