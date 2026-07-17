using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ABLyrics.App.Services.Spotify;
using Xunit;

namespace ABLyrics.App.Tests;

public class SpotifyApiClientTests
{
    /// <summary>
    /// 排队式 HttpMessageHandler：每次 SendAsync 取队首响应。
    /// 同时记录 Authorization 头，便于断言"401 后刷新得到的 token 与原 token 不同"。
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<string> AuthorizationHeaders { get; } = new();

        public void Enqueue(HttpStatusCode status, string body = "")
        {
            _responses.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("no more scripted responses"),
                });
            }
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeAuthService : ISpotifyAuthService
    {
        public string CurrentToken { get; private set; } = "token-v1";
        public int RefreshCalls { get; private set; }
        public bool RefreshShouldThrow { get; set; }
        public bool IsAuthenticated { get; private set; } = true;

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task LoginInteractiveAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentToken);

        public Task ForceRefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            if (RefreshShouldThrow) throw new InvalidOperationException("refresh failed");
            CurrentToken = "token-v2";
            return Task.CompletedTask;
        }

        public void Logout(string? reason = null)
        {
            IsAuthenticated = false;
            CurrentToken = string.Empty;
        }

        public event Action<string>? AuthenticationFailed;
    }

    [Fact]
    public async Task On401_ForcesRefreshOnceThenRetries()
    {
        var auth = new FakeAuthService();
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized); // 第一次：401
        handler.Enqueue(HttpStatusCode.OK, "{\"ok\":true}"); // 刷新后：200

        using var client = new SpotifyApiClient(auth, handler);

        using var response = await client.SendAsync(HttpMethod.Get, "me/player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, auth.RefreshCalls);
        Assert.Equal(new[] { "token-v1", "token-v2" }, handler.AuthorizationHeaders);
    }

    [Fact]
    public async Task On401_WhenRefreshFails_PropagatesException()
    {
        var auth = new FakeAuthService { RefreshShouldThrow = true };
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized);

        using var client = new SpotifyApiClient(auth, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(HttpMethod.Get, "me/player"));
        Assert.Equal(1, auth.RefreshCalls);
    }

    [Fact]
    public async Task On401_DoesNotRetryMoreThanOnce()
    {
        var auth = new FakeAuthService();
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized); // 401
        handler.Enqueue(HttpStatusCode.Unauthorized); // 刷新后还是 401：必须直接返回，不再触发二次刷新

        using var client = new SpotifyApiClient(auth, handler);

        using var response = await client.SendAsync(HttpMethod.Get, "me/player");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, auth.RefreshCalls);
    }

    [Fact]
    public async Task On200_DoesNotRefresh()
    {
        var auth = new FakeAuthService();
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        using var client = new SpotifyApiClient(auth, handler);

        using var response = await client.SendAsync(HttpMethod.Get, "me/player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, auth.RefreshCalls);
    }
}