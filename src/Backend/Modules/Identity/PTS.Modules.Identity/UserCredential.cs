namespace PTS.Modules.Identity;

/// <summary>
/// Login credentials for a <see cref="User"/>. Stored in a table that is
/// deliberately NOT RLS-protected so that login can look up a hash by email
/// before any <c>app.current_user_id</c> exists. The hash is never a
/// plaintext password. Email is duplicated from <see cref="User"/> solely as
/// the login lookup key — profile identity remains on <see cref="User"/>.
/// </summary>
public class UserCredential
{
    public Guid UserId { get; set; }

    public required string Email { get; set; }

    public required string PasswordHash { get; set; }
}
