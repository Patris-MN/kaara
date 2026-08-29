using Microsoft.EntityFrameworkCore;
using PTS.Host.Persistence;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.SharedKernel.Identity;

namespace PTS.Host.TenantAccess;

/// <summary>
/// Default <see cref="ITenantRlsSessionFactory"/>.
///
/// Bootstrap sequence (avoids RLS circularity):
///   1. Read UserId from <see cref="ICurrentUser"/> (validated principal only).
///   2. Confirm a User row exists (users RLS: id = current_user_id).
///   3. Resolve active Membership via <see cref="ITenantContextResolver"/>
///      (memberships RLS: user_id = current_user_id — no tenant GUC required).
///   4. Open a transaction and SET LOCAL both current_user_id and current_tenant_id.
///
/// Both GUCs use is_local=true so PostgreSQL clears them at COMMIT/ROLLBACK
/// before the pooled connection is reused.
/// </summary>
public sealed class TenantRlsSessionFactory : ITenantRlsSessionFactory
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountStore _userAccountStore;
    private readonly ITenantContextResolver _tenantContextResolver;
    private readonly ITenantContextEstablisher _tenantContextEstablisher;

    public TenantRlsSessionFactory(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ICurrentUser currentUser,
        IUserAccountStore userAccountStore,
        ITenantContextResolver tenantContextResolver,
        ITenantContextEstablisher tenantContextEstablisher)
    {
        _dbContextFactory = dbContextFactory;
        _currentUser = currentUser;
        _userAccountStore = userAccountStore;
        _tenantContextResolver = tenantContextResolver;
        _tenantContextEstablisher = tenantContextEstablisher;
    }

    public async Task<TenantRlsSession> OpenAsync(Guid requestedTenantId, CancellationToken cancellationToken = default)
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

        var resolution = await _tenantContextResolver.ResolveAsync(userId, requestedTenantId, cancellationToken);
        if (!resolution.Success ||
            resolution.TenantId is not Guid tenantId ||
            resolution.MembershipId is not Guid membershipId ||
            resolution.Role is not MembershipRole role)
        {
            throw new TenantAccessDeniedException(
                userId, requestedTenantId, resolution.FailureReason ?? "Access denied.");
        }

        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await PostgresRlsSettings.SetCurrentUserIdAsync(dbContext, userId, cancellationToken);
                await PostgresRlsSettings.SetCurrentTenantIdAsync(dbContext, tenantId, cancellationToken);
                await PostgresRlsSettings.SetCurrentMembershipRoleAsync(dbContext, role, cancellationToken);
                await PostgresRlsSettings.SetCurrentMembershipIdAsync(dbContext, membershipId, cancellationToken);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }

            _tenantContextEstablisher.Establish(tenantId);
            return new TenantRlsSession(
                dbContext,
                transaction,
                tenantId,
                membershipId,
                role,
                resolution.HasImplicitFullResourceAccess);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }
}
