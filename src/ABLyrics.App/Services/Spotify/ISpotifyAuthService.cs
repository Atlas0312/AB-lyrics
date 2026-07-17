namespace ABLyrics.App.Services.Spotify;

public interface ISpotifyAuthService
{
    bool IsAuthenticated { get; }
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
    Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task LoginInteractiveAsync(CancellationToken cancellationToken = default);
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制刷新 access token。用于 401 后的"刷新一次再重试"路径。
    /// 失败会抛出异常，由调用方决定是否吞掉。
    /// </summary>
    Task ForceRefreshAsync(CancellationToken cancellationToken = default);

    void Logout(string? reason = null);

    /// <summary>
    /// token 不可恢复（refresh 失败 / 已被撤销）时触发。
    /// 监听者应停止轮询并刷新 UI 状态。
    /// </summary>
    event Action<string>? AuthenticationFailed;
}
