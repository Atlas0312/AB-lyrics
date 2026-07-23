using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Playback;

public interface IPlaybackSource
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    bool IsConnected { get; }
    /// <summary>非交互恢复已有会话；无凭据时返回 false，不弹登录。</summary>
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
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