namespace ABLyrics.App.Configuration;

public sealed class AppSettings
{
    public SpotifySettings Spotify { get; init; } = new();
    public NetEaseSettings NetEase { get; init; } = new();
    public LyricsSettings Lyrics { get; init; } = new();
    public UiSettings Ui { get; init; } = new();
    public PlaybackSettings Playback { get; init; } = new();
}

public sealed class SpotifySettings
{
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; init; } = "http://127.0.0.1:48721/callback";
    public string[] Scopes { get; init; } = [];
}

public sealed class NetEaseSettings
{
    public string MusicU { get; init; } = string.Empty;
}

public sealed class LyricsSettings
{
    public string PrimaryProvider { get; init; } = "LRCLIB";
    public string FallbackProvider { get; init; } = "Netease";
    public string UserAgent { get; init; } = "AB-lyrics/0.1.0";
    public string LocalPath { get; init; } = string.Empty;
}

public sealed class UiSettings
{
    public string DefaultMode { get; init; } = "AppBar";
    public int AppBarHeight { get; init; } = 56;
}
