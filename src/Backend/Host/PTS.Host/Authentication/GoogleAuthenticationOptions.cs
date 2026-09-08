namespace PTS.Host.Authentication;

/// <summary>
/// Configuration for future Google OAuth. Secrets must come from environment or
/// user-secrets — never from source control.
/// </summary>
public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "Authentication:Google";

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
