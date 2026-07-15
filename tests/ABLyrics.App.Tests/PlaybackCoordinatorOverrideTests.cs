using System.IO;
using System.Reflection;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackCoordinatorOverrideTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; init; } = "Spotify";
        public string DisplayName { get; init; } = "Spotify";
        public bool IsAvailable { get; init; } = true;
        public bool IsConnected { get; set; }
        public PlaybackState? NextSnapshot { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NextSnapshot);

#pragma warning disable CS0067
        public event Action<PlaybackState?>? SnapshotChanged;
#pragma warning restore CS0067
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB", "Local" };
        public int FetchLyricsCallCount { get; private set; }

        // 记录每个 origin 拉取时返回的内容（缺省为 null）
        public Dictionary<CandidateOrigin, LyricsResult?> CandidateResponses { get; } = new();

        public Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
        {
            FetchLyricsCallCount++;
            return Task.FromResult<LyricsResult?>(null);
        }

        public Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
            => Task.FromResult<LyricsResult?>(null);

        public Task<LyricsResult?> FetchCandidateAsync(TrackInfo track, CandidateOrigin origin, CancellationToken ct = default)
        {
            return CandidateResponses.TryGetValue(origin, out var result)
                ? Task.FromResult(result)
                : Task.FromResult<LyricsResult?>(null);
        }
    }

    private sealed class FakeSearchService : ILyricsSearchService
    {
        public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(TrackInfo track, string library, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LyricsCandidate>>(Array.Empty<LyricsCandidate>());

        public Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private static TrackInfo Track(string id = "t1", string name = "晴天", string artist = "周杰伦", string album = "七里香") => new()
    {
        Id = id,
        Name = name,
        Artist = artist,
        Album = album,
        DurationMs = 269_000,
    };

    private static PlaybackCoordinator Build(
        FakeLyricsService lyrics,
        LyricsOverrideStore? store = null,
        ILyricsSearchService? search = null,
        FakeSource? source = null)
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(source ?? new FakeSource());
        return new PlaybackCoordinator(
            registry,
            "Spotify",
            lyrics,
            new LyricsBehaviorService(new LyricsBehaviorSettings()),
            new DisplaySettingsService(new DisplayStyleSettings()),
            search ?? new FakeSearchService(),
            store ?? new LyricsOverrideStore());
    }

    private static LyricsCandidate LocalCandidate(string filePath) => new()
    {
        Source = "Local",
        Label = "覆盖项",
        SyncedLyrics = "[00:01.00]测试歌词\n",
        PlainLyrics = "测试歌词",
        DurationMs = 269_000,
        Origin = new CandidateOrigin.Local(filePath),
    };

    private static string CreateTempLrcFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "ABLyricsTest-" + Guid.NewGuid().ToString("N") + ".lrc");
        File.WriteAllText(path, "[00:01.00]测试歌词\n");
        return path;
    }

    // LoadTrackAsync / PollAsync 是 private —— 走反射测试。
    private static Task InvokeLoadTrackAsync(PlaybackCoordinator coordinator, TrackInfo track)
    {
        var method = typeof(PlaybackCoordinator).GetMethod(
            "LoadTrackAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (Task)method!.Invoke(coordinator, new object[] { track })!;
    }

    private static Task InvokePollAsync(PlaybackCoordinator coordinator)
    {
        var method = typeof(PlaybackCoordinator).GetMethod(
            "PollAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return (Task)method!.Invoke(coordinator, null)!;
    }

    [Fact]
    public async Task LoadTrackAsync_HasOverride_AppliesOverrideBeforeLrclib()
    {
        var lrcPath = CreateTempLrcFile();
        try
        {
            var key = TrackKey.From("周杰伦", "七里香", "晴天");
            var store = new LyricsOverrideStore();
            store.Save(key, new CandidateOrigin.Local(lrcPath));

            var lyrics = new FakeLyricsService
            {
                CandidateResponses =
                {
                    [new CandidateOrigin.Local(lrcPath)] = new LyricsResult
                    {
                        Source = "Local",
                        SyncedLyrics = "[00:01.00]测试歌词\n",
                        PlainLyrics = "测试歌词",
                    },
                },
            };
            var coordinator = Build(lyrics, store);

            await InvokeLoadTrackAsync(coordinator, Track());

            // 主歌词源 = Local override，而不是 LRCLIB fallback
            Assert.Equal("Local", coordinator.LyricsSource);
            // LRCLIB fallback 没被调用
            Assert.Equal(0, lyrics.FetchLyricsCallCount);
        }
        finally
        {
            if (File.Exists(lrcPath)) File.Delete(lrcPath);
        }
    }

    [Fact]
    public async Task LoadTrackAsync_NoOverride_FallsBackAsBefore()
    {
        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics);

        await InvokeLoadTrackAsync(coordinator, Track(id: "noOverride", name: "N", artist: "A"));

        Assert.True(lyrics.FetchLyricsCallCount >= 1);
    }

    [Fact]
    public async Task LoadTrackAsync_OverrideStaleFile_RemovesOverrideAndFallsBack()
    {
        var key = TrackKey.From("周杰伦", "七里香", "晴天");
        var store = new LyricsOverrideStore();
        var stalePath = Path.Combine(Path.GetTempPath(), "ABLyricsTest-deleted-" + Guid.NewGuid().ToString("N") + ".lrc");
        // 不创建文件——模拟文件丢失
        store.Save(key, new CandidateOrigin.Local(stalePath));

        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics, store);

        await InvokeLoadTrackAsync(coordinator, Track());

        // 覆盖项被移除
        Assert.False(store.Load().ContainsKey(key));
        // 走 LRCLIB fallback
        Assert.True(lyrics.FetchLyricsCallCount >= 1);
    }

    [Fact]
    public async Task ApplyCandidateAsync_PersistsOverride()
    {
        var lrcPath = CreateTempLrcFile();
        try
        {
            var store = new LyricsOverrideStore();
            var lyrics = new FakeLyricsService();
            var coordinator = Build(lyrics, store);

            // 先 Load 一下让 coordinator 知道当前曲目
            await InvokeLoadTrackAsync(coordinator, Track());

            await coordinator.ApplyCandidateAsync(LocalCandidate(lrcPath));

            var key = TrackKey.From("周杰伦", "七里香", "晴天");
            Assert.True(store.Load().TryGetValue(key, out var origin));
            Assert.IsType<CandidateOrigin.Local>(origin);
            Assert.Equal(lrcPath, ((CandidateOrigin.Local)origin).FilePath);
        }
        finally
        {
            if (File.Exists(lrcPath)) File.Delete(lrcPath);
        }
    }

    [Fact]
    public async Task ApplyCandidateAsync_FreshTrack_UsesOverrideImmediately()
    {
        var lrcPath = CreateTempLrcFile();
        try
        {
            var lyrics = new FakeLyricsService
            {
                CandidateResponses =
                {
                    [new CandidateOrigin.Local(lrcPath)] = new LyricsResult
                    {
                        Source = "Local",
                        SyncedLyrics = "[00:01.00]X\n",
                        PlainLyrics = "X",
                    },
                },
            };
            var coordinator = Build(lyrics);

            await InvokeLoadTrackAsync(coordinator, Track());
            await coordinator.ApplyCandidateAsync(LocalCandidate(lrcPath));

            Assert.Equal("Local", coordinator.LyricsSource);
        }
        finally
        {
            if (File.Exists(lrcPath)) File.Delete(lrcPath);
        }
    }

    [Fact]
    public void OpenCandidatePicker_RaisesEvent()
    {
        var coordinator = Build(new FakeLyricsService());
        var raised = 0;
        coordinator.CandidatePickerRequested += () => raised++;

        coordinator.OpenCandidatePicker();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task ProgressMsChanged_FiredDuringPoll()
    {
        var source = new FakeSource { IsConnected = true };
        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics, source: source);

        await coordinator.TryRestoreSessionAsync();

        var received = new List<long>();
        coordinator.ProgressMsChanged += ms => received.Add(ms);

        source.NextSnapshot = new PlaybackState
        {
            Track = Track(),
            IsPlaying = true,
            ProgressMs = 1000,
        };
        await InvokePollAsync(coordinator);

        Assert.NotEmpty(received);
    }

    /// <summary>
    /// 回归 Bug 3：用户在候选窗口 [确认] 后，主引擎必须立刻持有候选歌词内容，
    /// 关键点：ApplyCandidateAsync 同步触发一次 UpdateLyricFrame——主引擎应立刻反映候选。
    /// </summary>
    [Fact]
    public async Task ApplyCandidateAsync_LoadedCandidate_DrivesEngineGetFrame()
    {
        var track = Track();
        var lyrics = new FakeLyricsService();
        var coordinator = Build(lyrics);

        // 让 coordinator 知道当前曲目（trackFields 依赖 _loadedTrackId/_trackTitle 等）
        await InvokeLoadTrackAsync(coordinator, track);

        // 取主引擎句柄
        var engineField = typeof(PlaybackCoordinator).GetField(
            "_syncEngine",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(engineField);
        var engine = (LyricsSyncEngine)engineField!.GetValue(coordinator)!;

        // 构造一个候选：synced lyrics 在 1.5s 处一句
        var candidate = new LyricsCandidate
        {
            Source = "Local",
            Label = "测试覆盖项",
            SyncedLyrics = "[00:01.50]确认后立刻显示\n",
            PlainLyrics = "确认后立刻显示",
            DurationMs = track.DurationMs,
            Origin = new CandidateOrigin.Local(CreateTempLrcFile()),
        };

        // 模拟播放进度已经走到 1.5s 之后，确保 UpdateLyricFrame 能命中候选行
        // 设置 _isPlaying=true + _lastProgressMs=1600（验证触发 UpdateLyricFrame 后立即刷出 CurrentLine）
        var playingField = typeof(PlaybackCoordinator).GetField(
            "_isPlaying", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastProgressField = typeof(PlaybackCoordinator).GetField(
            "_lastProgressMs", BindingFlags.NonPublic | BindingFlags.Instance);
        var lastSampleField = typeof(PlaybackCoordinator).GetField(
            "_lastSampleAt", BindingFlags.NonPublic | BindingFlags.Instance);
        playingField!.SetValue(coordinator, true);
        // _syncOffsetMs=150（默认）会扣掉；将 _lastProgressMs 设为 1800 保证插值后仍 >= 1500（候选首句时间）
        lastProgressField!.SetValue(coordinator, 1800L);
        lastSampleField!.SetValue(coordinator, DateTimeOffset.UtcNow);

        // 关键断言 #1：订阅了 ProgressMsChanged 后调 ApplyCandidate，事件应立刻触发
        var received = new List<long>();
        coordinator.ProgressMsChanged += ms => received.Add(ms);

        await coordinator.ApplyCandidateAsync(candidate);

        // Bug 3 核心：确认后主窗口字段立刻刷新，不需等下一次 Poll
        Assert.Equal("Local", coordinator.LyricsSource);
        Assert.True(coordinator.IsLyricsActive,
            "ApplyCandidateAsync 后，IsLyricsActive 应立刻为 true（progressMs 命中候选行）");
        Assert.Equal("确认后立刻显示", coordinator.CurrentLine);
        Assert.Equal(string.Empty, coordinator.StatusText);

        // 关键断言 #2：主引擎真实持有候选歌词内容
        var frameBefore = engine.GetFrame(0);
        var frameAt = engine.GetFrame(1600);
        Assert.True(frameAt.IsActive && frameAt.IsSynced,
            "ApplyCandidateAsync 后，主引擎 GetFrame(1600) 应返回已同步且 active 的帧");
        Assert.Equal("确认后立刻显示", frameAt.CurrentLine);
        // 0ms 早于 1500ms，引擎返回空帧是预期
        Assert.False(frameBefore.IsActive || !string.IsNullOrEmpty(frameBefore.CurrentLine),
            "未到第一句时间前，主引擎不应过早显示歌词");

        // 关键断言 #3：ProgressMsChanged 在 ApplyCandidate 期间被主动触发
        Assert.NotEmpty(received);
    }
}