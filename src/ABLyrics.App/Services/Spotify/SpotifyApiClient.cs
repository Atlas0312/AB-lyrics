using System.Net.Http;
using System.Net.Http.Headers;

namespace ABLyrics.App.Services.Spotify;

internal sealed class SpotifyApiClient : IDisposable
{
    private readonly ISpotifyAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private int _backoffSeconds = 2;

    public SpotifyApiClient(ISpotifyAuthService authService)
        : this(authService, new HttpClient { BaseAddress = new Uri("https://api.spotify.com/v1/") }, ownsClient: true)
    {
    }

    internal SpotifyApiClient(ISpotifyAuthService authService, HttpMessageHandler handler)
        : this(authService, new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.spotify.com/v1/") }, ownsClient: true)
    {
    }

    private SpotifyApiClient(ISpotifyAuthService authService, HttpClient httpClient, bool ownsClient)
    {
        _authService = authService;
        _httpClient = httpClient;
        _ownsHttpClient = ownsClient;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendLockedAsync(method, requestUri, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendLockedAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        // 401 时强制刷新一次再重试：服务端偶尔会比 ExpiresAt 提前作废 access_token，
        // 单靠本地时间判断的过期窗口 (AddMinutes(-1)) 可能错过这条窗口。
        var retriedAfterRefresh = false;
        var rateLimitRetries = 0;

        while (true)
        {
            var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode == 401 && !retriedAfterRefresh)
            {
                retriedAfterRefresh = true;
                response.Dispose();
                try
                {
                    await _authService.ForceRefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // 刷新失败：让外层走原本的 401 路径抛出，由 PollAsync 的 catch 写到 StatusText。
                    // 不在这里把 response 重新构造，避免遮蔽真实错误信息。
                    throw;
                }
                continue;
            }

            if ((int)response.StatusCode != 429)
            {
                _backoffSeconds = 2;
                return response;
            }

            var waitSeconds = ResolveRetryAfterSeconds(response);
            rateLimitRetries++;

            // 有限次退避：无限 while+429 会在限流未解除时持续消耗 currently-playing 配额。
            if (rateLimitRetries > 3)
            {
                response.Dispose();
                throw new HttpRequestException(
                    $"Spotify API 限流 (429)，已退避 {rateLimitRetries} 次，暂停轮询。");
            }

            _backoffSeconds = Math.Min(60, Math.Max(_backoffSeconds * 2, waitSeconds));
            response.Dispose();

            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private int ResolveRetryAfterSeconds(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is TimeSpan delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        if (retry?.Date is DateTimeOffset date)
        {
            return Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        }

        return Math.Max(1, _backoffSeconds);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _sendLock.Dispose();
    }
}
