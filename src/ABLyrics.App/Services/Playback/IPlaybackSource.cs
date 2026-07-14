using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Playback;

public interface IPlaybackSource
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect();
    Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default);
    event Action<PlaybackState?>? SnapshotChanged;
}