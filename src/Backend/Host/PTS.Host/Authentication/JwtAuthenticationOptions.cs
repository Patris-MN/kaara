namespace PTS.Host.Authentication;

public sealed class JwtAuthenticationOptions
{
    public const string SectionName = "Authentication:Jwt";

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    /// <summary>
    /// HMAC-SHA256 signing key. Must be supplied via configuration/environment
    /// (e.g. <c>Authentication__Jwt__SigningKey</c>) — never committed.
    /// </summary>
    public required string SigningKey { get; init; }

    public int AccessTokenLifetimeMinutes { get; init; } = 60;
}
