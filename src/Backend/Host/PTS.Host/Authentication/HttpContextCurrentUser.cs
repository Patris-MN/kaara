using System.Security.Claims;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Authentication;

/// <summary>
/// Translates the ASP.NET Core <see cref="ClaimsPrincipal"/> — which exists
/// only after JwtBearer has validated signature, issuer, audience, and
/// lifetime — into <see cref="ICurrentUser"/>. Request bodies, query strings,
/// routes, and custom headers are never consulted.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    public const string UserIdClaimType = ClaimTypes.NameIdentifier;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => UserId is not null;

    public Guid? UserId
    {
        get
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var raw = principal.FindFirstValue(UserIdClaimType);
            return Guid.TryParse(raw, out var userId) ? userId : null;
        }
    }
}
