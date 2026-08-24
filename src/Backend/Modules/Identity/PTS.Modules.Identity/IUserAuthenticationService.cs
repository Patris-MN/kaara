namespace PTS.Modules.Identity;

public interface IUserAuthenticationService
{
    Task<User> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Returns the user when credentials are valid; otherwise null.
    /// Callers must not learn whether the email exists from this method's
    /// return value alone — both unknown email and wrong password yield null.</summary>
    Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);
}
