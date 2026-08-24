namespace PTS.Modules.Identity;

/// <summary>
/// Narrow persistence port for creating users and looking up login credentials.
/// Not a generic repository. Implemented by the Host against EF Core.
/// </summary>
public interface IUserAccountStore
{
    Task AddAsync(User user, UserCredential credential, CancellationToken cancellationToken = default);

    Task<UserCredential?> FindCredentialByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
