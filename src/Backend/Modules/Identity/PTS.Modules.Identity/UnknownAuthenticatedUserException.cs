namespace PTS.Modules.Identity;

/// <summary>
/// Thrown when a token/principal names a <c>UserId</c> that does not exist in
/// the users table (deleted user, forged-but-signed-with-wrong-subject in
/// tests, stale identity). The principal was authenticated at the protocol
/// level, but it does not map to a current User record.
/// </summary>
public sealed class UnknownAuthenticatedUserException : Exception
{
    public Guid UserId { get; }

    public UnknownAuthenticatedUserException(Guid userId)
        : base($"Authenticated identity '{userId}' does not map to a User record.")
    {
        UserId = userId;
    }
}
