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

    /// <summary>
    /// 授权失效时由 source 触发，参数为给用户看的原因。
    /// 当前仅 Spotify 实现；其他 source 可保持不触发。
    /// </summary>
    event Action<string>? AuthenticationFailed;
}