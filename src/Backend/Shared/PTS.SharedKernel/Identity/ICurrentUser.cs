namespace PTS.SharedKernel.Identity;

/// <summary>
/// The authenticated global user for the current unit of work, derived only
/// from a cryptographically validated authentication principal — never from
/// request bodies, query strings, routes, or arbitrary headers.
///
/// Lives in SharedKernel so consuming modules can depend on "who is this
/// request" without referencing the Identity module (module-to-module
/// references are forbidden). Tenant membership is a separate question
/// answered by the Tenancy module after this identity is established.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The global <c>User.Id</c> when <see cref="IsAuthenticated"/> is
    /// true; otherwise null.</summary>
    Guid? UserId { get; }
}
