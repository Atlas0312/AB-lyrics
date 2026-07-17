using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Spotify;

namespace ABLyrics.App.Services.Playback;

public sealed class SpotifyPlaybackSource : IPlaybackSource
{
    private readonly ISpotifyAuthService _authService;
    private readonly ISpotifyPlaybackService _playbackService;
    private readonly SpotifySettings _settings;

    public SpotifyPlaybackSource(
        ISpotifyAuthService authService,
        ISpotifyPlaybackService playbackService,
        AppSettings settings)
    {
        _authService = authService;
        _playbackService = playbackService;
        _settings = settings.Spotify;
    }

    public string Id => "Spotify";
    public string DisplayName => "Spotify";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.ClientId);

    public bool IsConnected => _authService.IsAuthenticated;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("请在 appsettings.json 中配置 Spotify ClientId。");
        }
        await _authService.EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Disconnect() => _authService.Logout();

    public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => _playbackService.GetCurrentPlaybackAsync(cancellationToken);

    public event Action<PlaybackState?>? SnapshotChanged
    {
        add { /* Spotify: no push channel yet */ }
        remove { /* no-op */ }
    }

    public event Action<string>? AuthenticationFailed
    {
        add { _authService.AuthenticationFailed += value; }
        remove { _authService.AuthenticationFailed -= value; }
    }
}
