using SpotLyrics.App.Models;

namespace SpotLyrics.App.Services.Spotify;

public interface ISpotifyPlaybackService
{
    Task<PlaybackState?> GetCurrentPlaybackAsync(CancellationToken cancellationToken = default);
}
