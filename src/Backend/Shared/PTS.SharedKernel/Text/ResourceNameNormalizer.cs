namespace PTS.SharedKernel.Text;

/// <summary>
/// Normalizes resource display names for case-insensitive duplicate comparison.
/// Uses invariant lowercasing to align with PostgreSQL <c>lower(btrim(...))</c> indexes.
/// </summary>
public static class ResourceNameNormalizer
{
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
