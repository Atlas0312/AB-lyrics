using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ABLyrics.App.Configuration;

namespace ABLyrics.App.Services.Spotify;

public sealed class SpotifyAuthService : ISpotifyAuthService, IDisposable
{
    private static readonly Uri TokenEndpoint = new("https://accounts.spotify.com/api/token");
    private static readonly Uri AuthorizeEndpoint = new("https://accounts.spotify.com/authorize");

    private readonly SpotifySettings _settings;
    private readonly SpotifyTokenStore _tokenStore = new();
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private SpotifyTokenData? _tokens;

    public SpotifyAuthService(AppSettings settings)
    {
        _settings = settings.Spotify;
        _tokens = _tokenStore.Load();
    }

    public bool IsAuthenticated => _tokens is not null && !string.IsNullOrWhiteSpace(_tokens.RefreshToken);

    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_tokens is null || string.IsNullOrWhiteSpace(_tokens.RefreshToken))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            return false;
        }

        try
        {
            if (DateTimeOffset.UtcNow >= _tokens.ExpiresAt.AddMinutes(-1))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            throw new InvalidOperationException("请在 appsettings.json 中配置 Spotify ClientId。");
        }

        if (_tokens is null)
        {
            await LoginInteractiveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DateTimeOffset.UtcNow >= _tokens.ExpiresAt.AddMinutes(-1))
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        return _tokens!.AccessToken;
    }

    public async Task LoginInteractiveAsync(CancellationToken cancellationToken = default)
    {
        var redirectUri = new Uri(_settings.RedirectUri);
        var (verifier, challenge) = SpotifyPkce.CreatePair();
        var scope = string.Join(' ', _settings.Scopes);

        var query = string.Join('&', new[]
        {
            $"client_id={Uri.EscapeDataString(_settings.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(_settings.RedirectUri)}",
            $"scope={Uri.EscapeDataString(scope)}",
            "code_challenge_method=S256",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
        });

        var authorizeUrl = $"{AuthorizeEndpoint}?{query}";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/");
        listener.Start();

        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            var context = await listener.GetContextAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var response = context.Response;
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            var body = error is null
                ? "<html><body><h2>ABLyrics 授权成功，可以关闭此页面。</h2></body></html>"
                : $"<html><body><h2>授权失败：{WebUtility.HtmlEncode(error)}</h2></body></html>";

            var buffer = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            response.Close();

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"Spotify 授权被拒绝：{error}");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Spotify 授权未返回 code。");
            }

            await ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    public void Logout()
    {
        _tokens = null;
        _tokenStore.Clear();
    }

    private async Task ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _settings.RedirectUri,
            ["client_id"] = _settings.ClientId,
            ["code_verifier"] = verifier,
        });

        using var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ApplyTokenResponse(json);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tokens is null)
            {
                throw new InvalidOperationException("尚未登录 Spotify。");
            }

            if (DateTimeOffset.UtcNow < _tokens.ExpiresAt.AddMinutes(-1))
            {
                return;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _tokens.RefreshToken,
                ["client_id"] = _settings.ClientId,
            });

            using var response = await _httpClient.PostAsync(TokenEndpoint, content, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                Logout();
                throw new InvalidOperationException("Spotify 登录已过期，请重新授权。");
            }

            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ApplyTokenResponse(json, keepRefreshToken: true);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void ApplyTokenResponse(string json, bool keepRefreshToken = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Spotify token 响应缺少 access_token。");
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var refreshToken = keepRefreshToken
            ? _tokens?.RefreshToken ?? string.Empty
            : root.TryGetProperty("refresh_token", out var refreshElement)
                ? refreshElement.GetString() ?? string.Empty
                : string.Empty;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Spotify token 响应缺少 refresh_token。");
        }

        _tokens = new SpotifyTokenData
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
        };
        _tokenStore.Save(_tokens);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HttpRequestException($"Spotify 授权请求失败 ({(int)response.StatusCode}): {body}");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _refreshLock.Dispose();
    }
}
