namespace PTS.Modules.WorkManagement;

/// <summary>
/// Curated visual accent tokens for project identity. UX-only; not used for authorization.
/// </summary>
public static class ProjectAccentRules
{
    public const string Default = "indigo";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "indigo",
        "teal",
        "violet",
        "rose",
        "amber",
        "slate",
        "emerald",
        "sky",
    };

    public static bool TryNormalize(string? token, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            normalized = null;
            return true;
        }

        normalized = token.Trim().ToLowerInvariant();
        return Allowed.Contains(normalized);
    }

    public static string ResolveOrDefault(string? token)
        => TryNormalize(token, out var normalized) && normalized is not null ? normalized : Default;
}
