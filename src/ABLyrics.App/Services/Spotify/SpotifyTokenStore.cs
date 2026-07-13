using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ABLyrics.App.Services.Spotify;

internal sealed class SpotifyTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SpotifyTokenStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "spotify-tokens.dat");
    }

    public SpotifyTokenData? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SpotifyTokenData>(jsonBytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SpotifyTokenData data)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions));
        var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

internal sealed class SpotifyTokenData
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
