using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Spotify;

public interface ISpotifyPlaybackService
{
    Task<PlaybackState?> GetCurrentPlaybackAsync(CancellationToken cancellationToken = default);
}
