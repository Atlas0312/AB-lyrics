using System.IO;
using System.Text.Json;

namespace SpotLyrics.App.Configuration;

public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppSettings Load(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        var path = Path.Combine(baseDirectory, "appsettings.json");

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

        // 环境变量覆盖：SPOTIFY_CLIENT_ID > appsettings.json
        var envClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            settings.Spotify.ClientId = envClientId;
        }

        return settings;
    }
}
