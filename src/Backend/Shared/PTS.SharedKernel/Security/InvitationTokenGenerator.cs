using System.Security.Cryptography;
using System.Text;

namespace PTS.SharedKernel.Security;

/// <summary>
/// Generates opaque invitation tokens and one-way hashes for storage.
/// Raw tokens are never persisted.
/// </summary>
public static class InvitationTokenGenerator
{
    public const int TokenByteLength = 32;

    public static (string RawToken, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var rawToken = Base64UrlEncode(bytes);
        return (rawToken, Hash(rawToken));
    }

    public static string Hash(string rawToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
