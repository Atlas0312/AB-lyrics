using System.IO;
using System.Reflection;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

/// <summary>
/// 声明式矩阵：直接测 <see cref="PlaybackCoordinator.LoadLyricsAsync"/>，
/// 用 (LyricsLoadRequest, mock 状态) → 期望对外状态 覆盖关键组合。
/// </summary>
public class LoadLyricsAsyncMatrixTests
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

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAvailable) return Task.FromResult(false);
            IsConnected = true;
            return Task.FromResult(true);
        }

        public void Disconnect() => IsConnected = false;

        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<PlaybackState?>(null);

#pragma warning disable CS0067
        public event Action<PlaybackState?>? SnapshotChanged;
        public event Action<string>? AuthenticationFailed;
#pragma warning restore CS0067
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB", "Local" };

        public int FetchLyricsCallCount { get; private set; }
        public int FetchFromSourceCallCount { get; private set; }
        public int FetchCandidateCallCount { get; private set; }

        public LyricsCandidate? DefaultFetchResult { get; set; }
        public Dictionary<string, LyricsCandidate?> SourceResponses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<CandidateOrigin, LyricsCandidate?> CandidateResponses { get; } = new();

        public Task<LyricsCandidate?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
        {
            FetchLyricsCallCount++;
            return Task.FromResult(DefaultFetchResult);
        }

        public Task<LyricsCandidate?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
        {
            FetchFromSourceCallCount++;
            return Task.FromResult(
                SourceResponses.TryGetValue(source, out var v) ? v : null);
        }

        public Task<LyricsCandidate?> FetchCandidateAsync(TrackInfo track, CandidateOrigin origin, CancellationToken ct = default)
        {
            FetchCandidateCallCount++;
            return Task.FromResult(
                CandidateResponses.TryGetValue(origin, out var v) ? v : null);
        }
    }

    private static TrackInfo Track(
        string id = "t1",
        string name = "晴天",
        string artist = "周杰伦",
        string album = "七里香",
        int durationMs = 269_000) => new()
    {
        Id = id,
        Name = name,
        Artist = artist,
        Album = album,
        DurationMs = durationMs,
    };

    private static LyricsBehaviorService NewBehavior(bool promptLocalMissing = true)
    {
        var store = new LyricsBehaviorStore(Path.Combine(
            Path.GetTempPath(),
            "ABLyricsTests-" + Guid.NewGuid().ToString("N"),
            "lyrics-behavior.json"));
        return new LyricsBehaviorService(
            store,
            new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = promptLocalMissing });
    }

    private sealed class FakeSearchService : ILyricsSearchService
    {
        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(
            TrackInfo track, string library, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LyricsCandidate>>(Array.Empty<LyricsCandidate>());

        public Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private static PlaybackCoordinator Build(
        FakeLyricsService lyrics,
        LyricsBehaviorService? behavior = null,
        LyricsOverrideStore? overrideStore = null)
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource());
        return new PlaybackCoordinator(
            registry,
            "Spotify",
            lyrics,
            behavior ?? NewBehavior(),
            new DisplaySettingsService(new DisplayStyleSettings()),
            new FakeSearchService(),
            overrideStore ?? new LyricsOverrideStore());
    }

    private static LyricsSyncEngine GetEngine(PlaybackCoordinator coordinator)
    {
        var field = typeof(PlaybackCoordinator).GetField(
            "_syncEngine",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (LyricsSyncEngine)field!.GetValue(coordinator)!;
    }

    private static void SetPlayingProgress(PlaybackCoordinator coordinator, long progressMs)
    {
        typeof(PlaybackCoordinator)
            .GetField("_isPlaying", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(coordinator, true);
        typeof(PlaybackCoordinator)
            .GetField("_lastProgressMs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(coordinator, progressMs);
        typeof(PlaybackCoordinator)
            .GetField("_lastSampleAt", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(coordinator, DateTimeOffset.UtcNow);
    }

    private static LyricsCandidate SyncedCandidate(
        string source,
        string line,
        CandidateOrigin origin,
        int durationMs = 269_000) => new()
    {
        Source = source,
        Label = source,
        SyncedLyrics = $"[00:01.50]{line}\n",
        PlainLyrics = line,
        DurationMs = durationMs,
        Origin = origin,
    };

    // -------------------------------------------------------------------------
    // 1. OverrideOnly + 成功
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OverrideOnly_Success_SetsSourceClearsStatusAndFeedsEngine()
    {
        var origin = new CandidateOrigin.Local(@"C:\fake\override.lrc");
        var lyrics = new FakeLyricsService
        {
            CandidateResponses =
            {
                [origin] = SyncedCandidate("Local", "覆盖成功行", origin),
            },
        };
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(Track());

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.OverrideOnly,
            OverrideOrigin = origin,
            FailurePolicy = LyricsLoadFailurePolicy.PreserveOverride,
            FlashOnLoading = false,
            FlushFrameImmediately = false,
        });

        Assert.Equal("Local", coordinator.LyricsSource);
        Assert.Equal(string.Empty, coordinator.StatusText);
        Assert.Equal(0, lyrics.FetchLyricsCallCount);

        var frame = GetEngine(coordinator).GetFrame(1600);
        Assert.True(frame.IsActive && frame.IsSynced);
        Assert.Equal("覆盖成功行", frame.CurrentLine);
    }

    // -------------------------------------------------------------------------
    // 2. OverrideOnly + 失败 + PreserveOverride
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OverrideOnly_Failure_PreserveOverride_StatusHintsAndSkipsDefaultFetch()
    {
        var origin = new CandidateOrigin.Local(@"C:\fake\missing.lrc");
        var lyrics = new FakeLyricsService(); // CandidateResponses 空 → FetchCandidate 返回 null
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(Track());

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.OverrideOnly,
            OverrideOrigin = origin,
            FailurePolicy = LyricsLoadFailurePolicy.PreserveOverride,
            FlashOnLoading = false,
            FlushFrameImmediately = false,
        });

        Assert.Contains("覆盖项", coordinator.StatusText);
        Assert.Equal(0, lyrics.FetchLyricsCallCount);
        Assert.Equal(1, lyrics.FetchCandidateCallCount);
        Assert.Equal(string.Empty, coordinator.LyricsSource);
        Assert.False(GetEngine(coordinator).GetFrame(1600).IsActive);
    }

    // -------------------------------------------------------------------------
    // 3. Default + 无 override + Fetch 成功
    // -------------------------------------------------------------------------

    /// <summary>
    /// 使用不会撞上用户真实 lyrics-overrides.json 的曲目键，保证 Default 路径无 override。
    /// </summary>
    private static TrackInfo UniqueTrack()
    {
        var id = Guid.NewGuid().ToString("N");
        return Track(id: id, name: "Song-" + id, artist: "Artist-" + id, album: "Album-" + id);
    }

    [Fact]
    public async Task Default_NoOverride_FetchSuccess_LyricsSourceFromCandidate()
    {
        var lyrics = new FakeLyricsService
        {
            DefaultFetchResult = SyncedCandidate(
                "LRCLIB",
                "在线行",
                new CandidateOrigin.Lrclib(42)),
        };
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(UniqueTrack());

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.Default,
            FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
            FlashOnLoading = false,
            FlushFrameImmediately = false,
        });

        Assert.Equal("LRCLIB", coordinator.LyricsSource);
        Assert.Equal(string.Empty, coordinator.StatusText);
        Assert.Equal(1, lyrics.FetchLyricsCallCount);
        Assert.Equal(0, lyrics.FetchCandidateCallCount);
    }

    // -------------------------------------------------------------------------
    // 4. Default + 无 override + Fetch 失败
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Default_NoOverride_FetchFailure_StatusNoLyrics()
    {
        var lyrics = new FakeLyricsService { DefaultFetchResult = null };
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(UniqueTrack());

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.Default,
            FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
            FlashOnLoading = false,
            FlushFrameImmediately = false,
        });

        Assert.Equal("暂无歌词", coordinator.StatusText);
        Assert.Equal(1, lyrics.FetchLyricsCallCount);
        Assert.False(GetEngine(coordinator).GetFrame(0).IsActive);
    }

    // -------------------------------------------------------------------------
    // 5. ExplicitSource=Local + 缺失 + PromptLocalMissing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExplicitLocal_Missing_PromptLocalMissing_RaisesOnce()
    {
        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics, NewBehavior(promptLocalMissing: true));
        coordinator.InjectLoadedTrack(Track());

        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        var request = new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitSource,
            ExplicitSourceName = "Local",
            FlashOnLoading = false,
            PromptLocalMissing = true,
            FlushFrameImmediately = false,
        };

        await coordinator.LoadLyricsAsync(request);
        await coordinator.LoadLyricsAsync(request);

        Assert.Equal(1, raised);
        Assert.Equal(2, lyrics.FetchFromSourceCallCount);
        Assert.Equal("暂无歌词", coordinator.StatusText);
        Assert.Equal("Local", coordinator.LyricsSource);
    }

    // -------------------------------------------------------------------------
    // 6. ExplicitCandidate + FlushFrameImmediately
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExplicitCandidate_FlushFrameImmediately_UpdatesCurrentLineAndProgress()
    {
        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics);
        var track = Track();
        coordinator.InjectLoadedTrack(track);
        SetPlayingProgress(coordinator, 1800L);

        var progressEvents = new List<long>();
        coordinator.ProgressMsChanged += ms => progressEvents.Add(ms);

        var candidate = SyncedCandidate(
            "Local",
            "确认后立刻显示",
            new CandidateOrigin.Local(@"C:\fake\pick.lrc"),
            track.DurationMs);

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitCandidate,
            ExplicitCandidate = candidate,
            FlashOnLoading = false,
            FlushFrameImmediately = true,
        });

        Assert.Equal("Local", coordinator.LyricsSource);
        Assert.Equal(string.Empty, coordinator.StatusText);
        Assert.True(coordinator.IsLyricsActive);
        Assert.Equal("确认后立刻显示", coordinator.CurrentLine);
        Assert.NotEmpty(progressEvents);

        var frame = GetEngine(coordinator).GetFrame(1600);
        Assert.True(frame.IsActive && frame.IsSynced);
        Assert.Equal("确认后立刻显示", frame.CurrentLine);
    }

    // -------------------------------------------------------------------------
    // 7. ExplicitSource 非 Local → FlashOnLoading / LoadingFlash
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExplicitSource_NonLocal_FlashOnLoading_RaisesLoadingFlash()
    {
        var lyrics = new FakeLyricsService
        {
            SourceResponses =
            {
                ["LRCLIB"] = SyncedCandidate("LRCLIB", "闪烁后出现", new CandidateOrigin.Lrclib(1)),
            },
        };
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(Track());

        var flashCount = 0;
        coordinator.LoadingFlash += () => flashCount++;

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitSource,
            ExplicitSourceName = "LRCLIB",
            FlashOnLoading = true,
            FlushFrameImmediately = false,
        });

        Assert.Equal(1, flashCount);
        Assert.Equal("LRCLIB", coordinator.LyricsSource);
        Assert.Equal(string.Empty, coordinator.StatusText);
    }

    [Fact]
    public async Task ExplicitSource_Local_FlashOnLoadingTrue_DoesNotFlash()
    {
        // ShouldFlashFor：ExplicitSource + Local 即使 FlashOnLoading=true 也不闪。
        var lyrics = new FakeLyricsService
        {
            SourceResponses =
            {
                ["Local"] = SyncedCandidate(
                    "Local",
                    "本地行",
                    new CandidateOrigin.Local(@"C:\fake\a.lrc")),
            },
        };
        var coordinator = Build(lyrics);
        coordinator.InjectLoadedTrack(Track());

        var flashCount = 0;
        coordinator.LoadingFlash += () => flashCount++;

        await coordinator.LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitSource,
            ExplicitSourceName = "Local",
            FlashOnLoading = true,
            FlushFrameImmediately = false,
        });

        Assert.Equal(0, flashCount);
        Assert.Equal("Local", coordinator.LyricsSource);
    }
}
