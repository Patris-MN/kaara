using Microsoft.AspNetCore.Identity;

namespace PTS.Modules.Identity;

/// <summary>
/// Registers users and verifies passwords. Password hashing uses ASP.NET Core
/// Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2) — we do not invent
/// a hash. This service never logs passwords or hashes.
/// </summary>
public sealed class UserAuthenticationService : IUserAuthenticationService
{
    public const int MinimumPasswordLength = 8;

    private readonly IUserAccountStore _store;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserAuthenticationService(IUserAccountStore store, IPasswordHasher<User> passwordHasher)
    {
        _store = store;
        _passwordHasher = passwordHasher;
    }

    public async Task<User> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException(
                $"Password must be at least {MinimumPasswordLength} characters.", nameof(password));
        }

        if (await _store.FindCredentialByEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            throw new DuplicateEmailException(normalizedEmail);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = displayName.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var credential = new UserCredential
        {
            UserId = user.Id,
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(user, password),
        };

        await _store.AddAsync(user, credential, cancellationToken);
        return user;
    }

    public async Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var credential = await _store.FindCredentialByEmailAsync(normalizedEmail, cancellationToken);
        if (credential is null)
        {
            return null;
        }

        var probeUser = new User
        {
            Id = credential.UserId,
            Email = credential.Email,
            DisplayName = string.Empty,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };

        var verification = _passwordHasher.VerifyHashedPassword(probeUser, credential.PasswordHash, password);
        if (verification is PasswordVerificationResult.Failed)
        {
            return null;
        }

        return await _store.FindByIdAsync(credential.UserId, cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
