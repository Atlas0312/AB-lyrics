using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

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

        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
            : new AppSettings();

        // Environment override for Spotify client id.
        var envClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            settings.Spotify.ClientId = envClientId;
        }

        // Layer playback-state.json on top of appsettings.json.
        try
        {
            var playback = new PlaybackStateStore().Load();
            if (!string.IsNullOrWhiteSpace(playback.ActiveSource))
            {
                settings.Playback.ActiveSource = playback.ActiveSource;
            }
        }
        catch
        {
            // Ignore — keep the appsettings.json value.
        }

        return settings;
    }
}
