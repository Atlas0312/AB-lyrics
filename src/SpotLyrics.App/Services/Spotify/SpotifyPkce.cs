using System.Security.Cryptography;
using System.Text;

namespace SpotLyrics.App.Services.Spotify;

internal static class SpotifyPkce
{
    public static (string Verifier, string Challenge) CreatePair()
    {
        var verifier = GenerateVerifier();
        var challenge = ComputeChallenge(verifier);
        return (verifier, challenge);
    }

    private static string GenerateVerifier()
    {
        const string unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(64);
        var builder = new StringBuilder(64);
        foreach (var b in bytes)
        {
            builder.Append(unreserved[b % unreserved.Length]);
        }

        return builder.ToString();
    }

    private static string ComputeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    public static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
