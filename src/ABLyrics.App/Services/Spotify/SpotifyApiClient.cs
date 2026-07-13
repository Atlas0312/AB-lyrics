using System.Net.Http;
using System.Net.Http.Headers;

namespace ABLyrics.App.Services.Spotify;

internal sealed class SpotifyApiClient : IDisposable
{
    private readonly ISpotifyAuthService _authService;
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://api.spotify.com/v1/"),
    };

    private int _backoffSeconds = 2;

    public SpotifyApiClient(ISpotifyAuthService authService)
    {
        _authService = authService;
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    public void Dispose() => _httpClient.Dispose();
}
