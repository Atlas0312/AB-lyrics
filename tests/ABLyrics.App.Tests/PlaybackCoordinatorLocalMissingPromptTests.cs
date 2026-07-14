using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackCoordinatorLocalMissingPromptTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; init; } = "Spotify";
        public string DisplayName { get; init; } = "Spotify";
        public bool IsAvailable { get; init; } = true;
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }
        public void Disconnect() => IsConnected = false;
        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<PlaybackState?>(null);
        public event Action<PlaybackState?>? SnapshotChanged;
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public Dictionary<string, LyricsResult?> Responses { get; } = new();
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB", "Local" };
        public int CallCount { get; private set; }

        public Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<LyricsResult?>(null);
        }

        public Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Responses.TryGetValue($"{source}:{track.Id}", out var v) ? v : null);
        }
    }

    private static TrackInfo Track(string id) => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
        DurationMs = 1000,
    };

    private static PlaybackCoordinator Build(
        FakeLyricsService lyrics,
        PlaybackSourceRegistry registry,
        LyricsBehaviorService behavior)
    {
        registry.Register(new FakeSource { Id = "Spotify", DisplayName = "Spotify" });
        return new PlaybackCoordinator(
            registry,
            "Spotify",
            lyrics,
            behavior,
            new DisplaySettingsService(new DisplayStyleSettings()));
    }

    /// <summary>
    /// Builds a <see cref="LyricsBehaviorService"/> backed by an isolated store so tests
    /// don't read the user's real %LOCALAPPDATA%/ABLyrics/lyrics-behavior.json.
    /// </summary>
    private static LyricsBehaviorService NewBehavior(bool prompt)
    {
        var store = new LyricsBehaviorStore(Path.Combine(
            Path.GetTempPath(),
            "ABLyricsTests-" + Guid.NewGuid().ToString("N"),
            "lyrics-behavior.json"));
        return new LyricsBehaviorService(store, new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = prompt });
    }

    [Fact]
    public async Task LocalSource_MissingLyrics_RaisesEventOnce()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<TrackInfo>();
        coordinator.LocalLyricsMissing += t => raised.Add(t);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Single(raised);
        Assert.Equal("t1", raised[0].Id);
    }

    [Fact]
    public async Task LocalSource_SameTrackReloadedTwice_RaisesOnlyOnce()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task LocalSource_DifferentTracks_RaisesForEach()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        coordinator.InjectLoadedTrack(Track("t2"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(new[] { "t1", "t2" }, raised);
    }

    [Fact]
    public async Task NonLocalSource_MissingLyrics_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_PresentLyrics_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        lyrics.Responses["Local:t1"] = new LyricsResult { Source = "Local", PlainLyrics = "x" };
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOff_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(false);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOff_StillTracksInDedupSet()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(false);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        // Now turn the prompt back on. Same track should NOT raise again.
        behavior.Update(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOnAfterOff_RaisesForNewTracksOnly()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(false);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        behavior.Update(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });

        coordinator.InjectLoadedTrack(Track("t2"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(new[] { "t2" }, raised);
    }

    [Fact]
    public async Task LocalSource_ClearLyrics_AllowsRetrigger()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = NewBehavior(true);
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        coordinator.ClearLocalPromptState();
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(2, raised);
    }
}