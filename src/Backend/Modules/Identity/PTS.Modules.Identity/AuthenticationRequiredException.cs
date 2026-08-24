namespace PTS.Modules.Identity;

/// <summary>
/// Thrown when a security-sensitive operation is attempted without a validated
/// authenticated identity. Fail-closed: absence of authentication is denial,
/// not an invitation to accept a caller-supplied user id.
/// </summary>
public sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException()
        : base("An authenticated user is required.")
    {
    }
}
