using Microsoft.EntityFrameworkCore;
using PTS.Host.Persistence;
using PTS.Modules.Identity;
using PTS.SharedKernel.Identity;

namespace PTS.Host.TenantAccess;

public sealed class UserRlsSessionFactory : IUserRlsSessionFactory
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountStore _userAccountStore;

    public UserRlsSessionFactory(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ICurrentUser currentUser,
        IUserAccountStore userAccountStore)
    {
        _dbContextFactory = dbContextFactory;
        _currentUser = currentUser;
        _userAccountStore = userAccountStore;
    }

    public async Task<UserRlsSession> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not Guid userId)
        {
            throw new AuthenticationRequiredException();
        }

        var existingUser = await _userAccountStore.FindByIdAsync(userId, cancellationToken);
        if (existingUser is null)
        {
            throw new UnknownAuthenticatedUserException(userId);
        }

        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await PostgresRlsSettings.SetCurrentUserIdAsync(dbContext, userId, cancellationToken);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }

            return new UserRlsSession(dbContext, transaction, userId);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }
}
