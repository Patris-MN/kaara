namespace PTS.SharedKernel.Identity;

/// <summary>
/// Thrown when a unit of work requires an authenticated <see cref="ICurrentUser"/>
/// and none is present. Lives in SharedKernel so Tenancy can fail closed
/// without referencing the Identity module.
/// </summary>
public sealed class UnauthenticatedException : Exception
{
    public UnauthenticatedException()
        : base("Authentication is required.")
    {
    }
}
