using System.Net.Http;
using System.Net.Http.Headers;

namespace ABLyrics.App.Services.Spotify;

internal sealed class SpotifyApiClient : IDisposable
{
    private readonly ISpotifyAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

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
        // 401 时强制刷新一次再重试：服务端偶尔会比 ExpiresAt 提前作废 access_token，
        // 单靠本地时间判断的过期窗口 (AddMinutes(-1)) 可能错过这条窗口。
        var retriedAfterRefresh = false;

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

            var waitSeconds = response.Headers.RetryAfter?.Delta?.TotalSeconds is double seconds
                ? (int)Math.Ceiling(seconds)
                : _backoffSeconds;
            _backoffSeconds = Math.Min(60, _backoffSeconds * 2);
            response.Dispose();

            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
