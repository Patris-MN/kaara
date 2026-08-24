using PTS.SharedKernel.Identity;

namespace PTS.IntegrationTests;

/// <summary>
/// Test double for <see cref="ICurrentUser"/>. Production code reads a
/// validated authentication principal; tests assign the user the simulated
/// request is authenticated as.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId { get; private set; }

    public void AuthenticateAs(Guid userId) => UserId = userId;

    public void Clear() => UserId = null;
}
