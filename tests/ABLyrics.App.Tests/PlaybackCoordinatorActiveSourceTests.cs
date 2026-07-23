using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackCoordinatorActiveSourceTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsAvailable { get; set; } = true;
        public bool IsConnected { get; private set; }
        public bool ConnectShouldThrow { get; set; }
        public int ConnectCalls { get; private set; }
        public PlaybackState? NextSnapshot { get; set; }

        public FakeSource(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            if (ConnectShouldThrow) throw new InvalidOperationException("boom");
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) return Task.FromResult(false);
            if (ConnectShouldThrow) return Task.FromResult(false);
            IsConnected = true;
            return Task.FromResult(true);
        }

        public void Disconnect() => IsConnected = false;

        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NextSnapshot);

        public event Action<PlaybackState?>? SnapshotChanged;
#pragma warning disable CS0067
        public event Action<string>? AuthenticationFailed;
#pragma warning restore CS0067

        public void RaiseAuthenticationFailed(string reason)
            => AuthenticationFailed?.Invoke(reason);
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB" };
        public Task<LyricsCandidate?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
            => Task.FromResult<LyricsCandidate?>(null);
        public Task<LyricsCandidate?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
            => Task.FromResult<LyricsCandidate?>(null);
        public Task<LyricsCandidate?> FetchCandidateAsync(TrackInfo track, CandidateOrigin origin, CancellationToken ct = default)
            => Task.FromResult<LyricsCandidate?>(null);
    }

    private static PlaybackCoordinator BuildCoordinator(PlaybackSourceRegistry registry, string initialId)
    {
        return new PlaybackCoordinator(
            registry,
            initialId,
            new FakeLyricsService(),
            new LyricsBehaviorService(new LyricsBehaviorSettings()),
            new DisplaySettingsService(new DisplayStyleSettings()));
    }

    [Fact]
    public void TryRestore_WhenSourceUnavailable_StaysStopped()
    {
        var registry = new PlaybackSourceRegistry();
        var source = new FakeSource("Spotify", "Spotify") { IsAvailable = false };
        registry.Register(source);

        var coordinator = BuildCoordinator(registry, "Spotify");

        var restored = coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        Assert.False(restored);
        Assert.False(coordinator.IsRunning);
        Assert.Equal(0, source.ConnectCalls);
    }

    [Fact]
    public void TryRestore_WhenSourceAvailableAndConnected_MarksRestored()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        var coordinator = BuildCoordinator(registry, "Spotify");

        var restored = coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        Assert.True(restored);
        Assert.True(coordinator.IsSourceConnected);
    }

    [Fact]
    public void SetActiveSource_UnknownId_LeavesActiveSourceNull()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        var coordinator = BuildCoordinator(registry, "Spotify");

        coordinator.SetActiveSourceAsync("Local").GetAwaiter().GetResult();

        Assert.Null(coordinator.ActivePlaybackSource);
    }

    [Fact]
    public void SetActiveSource_ConnectThrows_KeepsPreviousActiveSource()
    {
        var registry = new PlaybackSourceRegistry();
        var first = new FakeSource("Spotify", "Spotify");
        var second = new FakeSource("Local", "Local") { ConnectShouldThrow = true };
        registry.Register(first);
        registry.Register(second);
        var coordinator = BuildCoordinator(registry, "Spotify");
        coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        coordinator.SetActiveSourceAsync("Local").GetAwaiter().GetResult();

        Assert.Same(first, coordinator.ActivePlaybackSource);
    }

    [Fact]
    public void AuthenticationFailed_StopsCoordinatorAndUpdatesStatus()
    {
        var registry = new PlaybackSourceRegistry();
        var source = new FakeSource("Spotify", "Spotify");
        registry.Register(source);
        var coordinator = BuildCoordinator(registry, "Spotify");
        coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();
        coordinator.Start();

        Assert.True(coordinator.IsRunning);

        source.RaiseAuthenticationFailed("Spotify 登录已过期，请重新授权。");

        Assert.False(coordinator.IsRunning);
        Assert.Equal("Spotify 登录已过期，请重新授权。", coordinator.StatusText);
    }

    [Fact]
    public void AuthenticationFailed_FromStaleSource_AfterSwitch_DoesNotStop()
    {
        var registry = new PlaybackSourceRegistry();
        var first = new FakeSource("Spotify", "Spotify");
        var second = new FakeSource("Local", "Local");
        registry.Register(first);
        registry.Register(second);
        var coordinator = BuildCoordinator(registry, "Spotify");
        coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();
        coordinator.SetActiveSourceAsync("Local").GetAwaiter().GetResult();
        coordinator.Start();

        Assert.True(coordinator.IsRunning);

        // 旧 source（Spotify）延后触发的认证失效不应影响新 source 的轮询。
        first.RaiseAuthenticationFailed("Spotify 登录已过期，请重新授权。");

        Assert.True(coordinator.IsRunning);
    }
}