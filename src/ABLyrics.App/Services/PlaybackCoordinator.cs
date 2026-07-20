using System.ComponentModel;
using System.Runtime.CompilerServices;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Lyricify.Lyrics.Parsers;

namespace ABLyrics.App.Services;

public sealed class PlaybackCoordinator : INotifyPropertyChanged, IDisposable
{
    private const int PollIntervalMs = 800;

    private readonly PlaybackSourceRegistry _registry;
    private readonly ILyricsService _lyricsService;
    private readonly LyricsBehaviorService _lyricsBehavior;
    private readonly LyricsSyncEngine _syncEngine = new();
    private readonly TrackInfoLayoutState _trackInfoLayout = new();
    private readonly System.Timers.Timer _timer;
    private readonly HashSet<string> _localPromptedTrackIds = new();
    private readonly object _localPromptLock = new();
    private readonly ILyricsSearchService _searchService;
    private readonly LyricsOverrideStore _overrideStore;
    private readonly Dictionary<string, CandidateOrigin> _overrides;
    private LyricsCandidate? _overrideCandidate;

    private IPlaybackSource? _activePlaybackSource;
    private string _lyricsActiveSource = string.Empty;
    private string _statusText = "未配置播放来源";
    private string _trackTitle = string.Empty;
    private string _artistName = string.Empty;
    private string _currentLine = string.Empty;
    private string _previousLine = string.Empty;
    private string _nextLine = string.Empty;
    private string _lyricsSource = string.Empty;
    private bool _isLyricsActive;
    private bool _shouldCenterTrackInfo = true;

    private string? _loadedTrackId;
    private int _trackDurationMs;
    private string _trackAlbum = string.Empty;
    private long _lastProgressMs;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.UtcNow;
    private bool _isPlaying;

    private int _syncOffsetMs = 150;

    public int SyncOffsetMs
    {
        get => _syncOffsetMs;
        set
        {
            var clamped = Math.Clamp(value, 0, 500);
            if (clamped == _syncOffsetMs) return;
            _syncOffsetMs = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncOffsetMs)));
            SyncOffsetRecorder.Record(_syncOffsetMs);
        }
    }

    public PlaybackCoordinator(
        PlaybackSourceRegistry registry,
        string initialSourceId,
        ILyricsService lyricsService,
        LyricsBehaviorService lyricsBehavior,
        DisplaySettingsService? displaySettings = null,
        ILyricsSearchService? searchService = null,
        LyricsOverrideStore? overrideStore = null)
    {
        _registry = registry;
        _lyricsService = lyricsService;
        _lyricsBehavior = lyricsBehavior;
        _searchService = searchService ?? new LyricsSearchService(App.Settings);
        _overrideStore = overrideStore ?? new LyricsOverrideStore();
        _overrides = _overrideStore.Load().ToDictionary(kv => kv.Key, kv => kv.Value);
        _activePlaybackSource = _registry.Get(initialSourceId);
        AttachActiveSourceEvents(_activePlaybackSource);

        _syncOffsetMs = Math.Clamp(displaySettings?.Current.SyncOffsetMs ?? 150, 0, 500);

        _timer = new System.Timers.Timer(PollIntervalMs);
        _timer.Elapsed += async (_, _) => await PollAsync().ConfigureAwait(false);
        _timer.AutoReset = true;
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string TrackTitle
    {
        get => _trackTitle;
        private set => SetField(ref _trackTitle, value);
    }

    public string ArtistName
    {
        get => _artistName;
        private set => SetField(ref _artistName, value);
    }

    public string TrackAlbum => _trackAlbum;

    public string CurrentLine
    {
        get => _currentLine;
        private set => SetField(ref _currentLine, value);
    }

    public string PreviousLine
    {
        get => _previousLine;
        private set => SetField(ref _previousLine, value);
    }

    public string NextLine
    {
        get => _nextLine;
        private set => SetField(ref _nextLine, value);
    }

    public string LyricsSource
    {
        get => _lyricsSource;
        private set => SetField(ref _lyricsSource, value);
    }

    public bool IsLyricsActive
    {
        get => _isLyricsActive;
        private set => SetField(ref _isLyricsActive, value);
    }

    public bool ShouldCenterTrackInfo
    {
        get => _shouldCenterTrackInfo;
        private set => SetField(ref _shouldCenterTrackInfo, value);
    }

    public bool IsPlaying => _isPlaying;
    public IReadOnlyList<string> AvailableSources => _lyricsService.AvailableSources;
    public string? GetCurrentTrackId() => _loadedTrackId;

    /// <summary>当前正在使用的歌词候选（含原始 Synced/Plain 文本）。调试导出用。</summary>
    public LyricsCandidate? CurrentLyricsCandidate => _overrideCandidate;

    /// <summary>
    /// DEBUG：导出同步排查用的完整快照（曲目 / 进度 / 偏移 / 来源链接 / 时间轴 / 原文）。
    /// 无歌词时返回 null。
    /// </summary>
    public string? TryBuildLyricsDebugDump()
    {
        var candidate = _overrideCandidate;
        var rawText = candidate?.SyncedLyrics ?? candidate?.PlainLyrics;
        if (candidate is null || string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        var sb = new System.Text.StringBuilder(rawText.Length + 2048);
        var interpolated = GetInterpolatedProgressMs();
        var sampleAgeMs = (long)(DateTimeOffset.UtcNow - _lastSampleAt).TotalMilliseconds;

        sb.AppendLine("=== ABLyrics Lyrics Sync Debug Dump ===");
        sb.AppendLine($"ExportedAt: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine();

        sb.AppendLine("--- Track ---");
        sb.AppendLine($"Title: {_trackTitle}");
        sb.AppendLine($"Artist: {_artistName}");
        sb.AppendLine($"Album: {_trackAlbum}");
        sb.AppendLine($"TrackId: {_loadedTrackId ?? "(null)"}");
        sb.AppendLine($"DurationMs: {_trackDurationMs} ({FormatClock(_trackDurationMs)})");
        if (!string.IsNullOrWhiteSpace(_loadedTrackId))
        {
            sb.AppendLine($"PlaybackLink: https://open.spotify.com/track/{_loadedTrackId}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Sync State (why may be off) ---");
        sb.AppendLine($"IsPlaying: {_isPlaying}");
        sb.AppendLine($"SyncOffsetMs: {_syncOffsetMs}");
        sb.AppendLine($"LastPollProgressMs: {_lastProgressMs} ({FormatClock(_lastProgressMs)})");
        sb.AppendLine($"SampleAgeMs: {sampleAgeMs}");
        sb.AppendLine($"InterpolatedProgressMs (= poll + clamp(age) - SyncOffset): {interpolated} ({FormatClock(interpolated)})");
        sb.AppendLine($"DisplayedPrevious: {_previousLine}");
        sb.AppendLine($"DisplayedCurrent: {_currentLine}");
        sb.AppendLine($"DisplayedNext: {_nextLine}");

        sb.AppendLine();
        sb.AppendLine("--- Lyrics Source ---");
        sb.AppendLine($"UiLyricsSource: {_lyricsSource}");
        sb.AppendLine($"CandidateSource: {candidate.Source}");
        sb.AppendLine($"CandidateLabel: {candidate.Label}");
        sb.AppendLine($"CandidateDurationMs: {candidate.DurationMs}");
        sb.AppendLine($"HasSyncedLyrics: {!string.IsNullOrWhiteSpace(candidate.SyncedLyrics)}");
        sb.AppendLine($"HasPlainLyrics: {!string.IsNullOrWhiteSpace(candidate.PlainLyrics)}");
        AppendOriginDebug(sb, candidate.Origin, _trackTitle, _artistName);

        if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
        {
            var data = LrcParser.Parse(candidate.SyncedLyrics);
            sb.AppendLine();
            sb.AppendLine("--- Parsed Timeline (engine input) ---");
            sb.AppendLine("# index\tstartMs\tlrcTag\ttext");
            if (data?.Lines is { Count: > 0 } lines)
            {
                var activeIndex = -1;
                for (var i = 0; i < lines.Count; i++)
                {
                    var start = lines[i].StartTime ?? 0;
                    if (start <= interpolated)
                    {
                        activeIndex = i;
                    }

                    var tag = FormatLrcTag(start);
                    var text = (lines[i].Text ?? string.Empty).Replace('\t', ' ');
                    var marker = i == activeIndex ? " <== current @ interpolated" : string.Empty;
                    sb.AppendLine($"{i}\t{start}\t{tag}\t{text}{marker}");
                }

                if (activeIndex < 0 && lines.Count > 0)
                {
                    sb.AppendLine($"# note: interpolated ({interpolated}ms) is before first line ({lines[0].StartTime ?? 0}ms)");
                }
            }
            else
            {
                sb.AppendLine("# (parser produced no timed lines)");
            }

            sb.AppendLine();
            sb.AppendLine("--- Synced Lyrics (raw) ---");
            sb.AppendLine(candidate.SyncedLyrics.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics))
        {
            sb.AppendLine();
            sb.AppendLine("--- Plain Lyrics (raw) ---");
            sb.AppendLine(candidate.PlainLyrics.TrimEnd());
        }

        sb.AppendLine();
        sb.AppendLine("=== End ===");
        return sb.ToString();
    }

    private static void AppendOriginDebug(
        System.Text.StringBuilder sb,
        CandidateOrigin origin,
        string title,
        string artist)
    {
        switch (origin)
        {
            case CandidateOrigin.Local local:
                sb.AppendLine("OriginKind: Local");
                sb.AppendLine($"OriginFilePath: {local.FilePath}");
                sb.AppendLine($"OriginLink: file:///{local.FilePath.Replace('\\', '/')}");
                break;
            case CandidateOrigin.Lrclib lrclib:
                sb.AppendLine("OriginKind: LRCLIB");
                sb.AppendLine($"OriginId: {lrclib.LrclibId}");
                sb.AppendLine($"OriginApiLink: https://lrclib.net/api/get/{lrclib.LrclibId}");
                var lrclibQuery = string.Join(' ', new[] { artist, title }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(lrclibQuery))
                {
                    sb.AppendLine($"OriginSearchLink: https://lrclib.net/?q={Uri.EscapeDataString(lrclibQuery)}");
                }
                break;
            case CandidateOrigin.Netease netease:
                sb.AppendLine("OriginKind: Netease");
                sb.AppendLine($"OriginId: {netease.NeteaseSongId}");
                sb.AppendLine($"OriginLink: https://music.163.com/#/song?id={netease.NeteaseSongId}");
                break;
            default:
                sb.AppendLine($"OriginKind: {origin.GetType().Name}");
                sb.AppendLine($"Origin: {origin}");
                break;
        }
    }

    private static string FormatClock(long ms)
    {
        if (ms < 0) ms = 0;
        var totalSeconds = ms / 1000.0;
        var m = (int)(totalSeconds / 60);
        var s = totalSeconds - m * 60;
        return $"{m:00}:{s:00.000}";
    }

    private static string FormatLrcTag(int ms)
    {
        if (ms < 0) ms = 0;
        var m = ms / 60000;
        var s = (ms % 60000) / 1000;
        var cs = (ms % 1000) / 10;
        return $"[{m:00}:{s:00}.{cs:00}]";
    }

    public event Action? LoadingFlash;
    public event Action<bool>? IsPlayingChanged;
    public event Action? SourceStateChanged;
    public event Action<TrackInfo>? LocalLyricsMissing;
    public event Action<long>? ProgressMsChanged;
    public event Action? CandidatePickerRequested;

    public bool IsRunning => _timer.Enabled;

    public bool IsAuthenticated => _activePlaybackSource?.IsConnected ?? false;

    /// <summary>当前播放进度来源（可插拔的播放来源抽象）。</summary>
    public IPlaybackSource? ActivePlaybackSource => _activePlaybackSource;

    /// <summary>当前歌词来源名称（"LRCLIB" / "Netease" / "Local" 等），与公开 API 兼容。</summary>
    public string ActiveSource => _lyricsActiveSource;

    public bool IsSourceConnected => _activePlaybackSource?.IsConnected ?? false;

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (_activePlaybackSource is null) return false;
        if (!_activePlaybackSource.IsAvailable)
        {
            StatusText = $"来源不可用：{_activePlaybackSource.DisplayName}";
            return false;
        }

        try
        {
            await _activePlaybackSource.ConnectAsync().ConfigureAwait(false);
            StatusText = $"已连接 {_activePlaybackSource.DisplayName}";
            SourceStateChanged?.Invoke();
            return true;
        }
        catch
        {
            StatusText = $"请先连接 {_activePlaybackSource.DisplayName}";
            return false;
        }
    }

    private void OnActiveSourceAuthenticationFailed(string reason)
    {
        // Spotify 这类走 OAuth 的 source，refresh_token 被撤销或长期未活动后会触发。
        // 必须立即停止轮询，否则 PollAsync 会以每 800 ms 一次的频率继续打 401。
        Stop();
        StatusText = reason;
        SourceStateChanged?.Invoke();
    }

    public async Task SetActiveSourceAsync(string id, bool restoreOnly = false)
    {
        if (_activePlaybackSource is { } current
            && string.Equals(current.Id, id, StringComparison.Ordinal)
            && current.IsConnected)
        {
            return;
        }

        Stop();
        ClearLyrics();

        var previous = _activePlaybackSource;
        DetachActiveSourceEvents(previous);
        var next = _registry.Get(id);
        if (next is null)
        {
            _activePlaybackSource = null;
            StatusText = "未知播放来源";
            SourceStateChanged?.Invoke();
            return;
        }

        if (!next.IsAvailable)
        {
            _activePlaybackSource = next;
            StatusText = $"来源不可用：{next.DisplayName}";
            SourceStateChanged?.Invoke();
            return;
        }

        AttachActiveSourceEvents(next);
        _activePlaybackSource = next;
        try
        {
            await next.ConnectAsync().ConfigureAwait(false);
            StatusText = $"已连接 {next.DisplayName}";
            Start();
        }
        catch (Exception ex)
        {
            _activePlaybackSource = previous;
            DetachActiveSourceEvents(next);
            AttachActiveSourceEvents(previous);
            StatusText = $"连接 {next.DisplayName} 失败：{ex.Message}";
        }

        SourceStateChanged?.Invoke();

        if (!restoreOnly && _activePlaybackSource is { } persisted && persisted == next && next.IsConnected)
        {
            try
            {
                new PlaybackStateStore().Save(new PlaybackSettings { ActiveSource = next.Id });
            }
            catch
            {
                // Persist failure is non-fatal.
            }
        }
    }

    public Task LoginAsync()
    {
        if (_activePlaybackSource is null)
        {
            throw new InvalidOperationException("尚未选择播放来源。");
        }
        return _activePlaybackSource.ConnectAsync();
    }

    public void Logout()
    {
        Stop();
        _activePlaybackSource?.Disconnect();
        ResetDisplay("已断开当前播放来源");
        SourceStateChanged?.Invoke();
    }

    public void Start()
    {
        if (_activePlaybackSource is null || !_activePlaybackSource.IsConnected)
        {
            StatusText = _activePlaybackSource is null
                ? "未配置播放来源"
                : $"请先连接 {_activePlaybackSource.DisplayName}";
            return;
        }

        if (!_timer.Enabled)
        {
            _timer.Start();
            StatusText = "正在监听播放…";
        }
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void TickInterpolation()
    {
        if (!_isPlaying)
        {
            return;
        }

        UpdateLyricFrame(GetInterpolatedProgressMs());
    }

    public async Task SetSourceAsync(string sourceName)
    {
        _lyricsActiveSource = sourceName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveSource)));
        // 用户显式切源：绕过 override，直接按 source 名取。
        await LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitSource,
            ExplicitSourceName = sourceName,
            FlashOnLoading = true,
            PromptLocalMissing = true,
            FlushFrameImmediately = false,
        }).ConfigureAwait(false);
    }

    public async Task ForceReloadAsync()
    {
        if (_loadedTrackId is null) return;
        // 🔄 强制走在线源兜底链，不读 override；和"切源"语义对齐。
        await LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.Default,
            FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
            FlashOnLoading = true,
            FlushFrameImmediately = false,
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 显式重载当前曲目。等价于按当前源（"Local" 时弹缺失提示）重新取一遍。
    /// 保留原方法名以便测试与老代码使用。
    /// </summary>
    internal async Task ReloadCurrentTrackAsync()
    {
        if (_loadedTrackId is null) return;
        // 按当前源选择：Local → 走 ExplicitSource + 提示；其它 → 也走 ExplicitSource；
        // 空源 → 走默认 LRCLIB 兜底。
        if (string.IsNullOrWhiteSpace(_lyricsActiveSource))
        {
            await LoadLyricsAsync(new LyricsLoadRequest
            {
                Source = LyricsLoadSource.Default,
                FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
                FlashOnLoading = true,
                FlushFrameImmediately = false,
            }).ConfigureAwait(false);
            return;
        }

        await LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitSource,
            ExplicitSourceName = _lyricsActiveSource,
            FlashOnLoading = _lyricsActiveSource != "Local",
            PromptLocalMissing = true,
            FlushFrameImmediately = false,
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 清除当前曲目的覆盖项（picker 的 ✕ 按钮走这里），然后按"无 override"重新加载。
    /// </summary>
    public Task ReloadWithoutOverrideAsync()
    {
        if (_loadedTrackId is null) return Task.CompletedTask;
        var track = SnapshotCurrentTrack();
        var key = TrackKey.From(track);
        _overrides.Remove(key);
        return LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.Default,
            FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
            FlashOnLoading = true,
            FlushFrameImmediately = false,
        });
    }

    /// <summary>
    /// 唯一的"加载歌词"入口。所有 UI 操作都收敛到这里。
    /// 副作用（LoadingFlash / StatusText / LyricsSource / 引擎喂入 / Local 缺失提示）一律集中处理。
    /// </summary>
    internal async Task LoadLyricsAsync(LyricsLoadRequest request)
    {
        if (_loadedTrackId is null) return;

        var track = SnapshotCurrentTrack();

        if (request.FlashOnLoading && ShouldFlashFor(request))
        {
            StatusText = BuildLoadingMessage(request);
            LoadingFlash?.Invoke();
        }

        var candidate = await ResolveCandidateAsync(track, request).ConfigureAwait(false);

        ApplyCandidateToEngine(candidate, track.DurationMs);
        _overrideCandidate = candidate;
        LyricsSource = ResolveLyricsSource(candidate, request);
        StatusText = ResolveFinalStatusText(candidate, request);

        if (request.PromptLocalMissing
            && request.Source == LyricsLoadSource.ExplicitSource
            && request.ExplicitSourceName == "Local"
            && candidate is null)
        {
            TriggerLocalMissingPrompt(track);
        }

        if (request.FlushFrameImmediately)
        {
            UpdateLyricFrame(GetInterpolatedProgressMs());
        }
    }

    private TrackInfo SnapshotCurrentTrack() => new()
    {
        Id = _loadedTrackId ?? string.Empty,
        Name = _trackTitle,
        Artist = _artistName,
        Album = _trackAlbum,
        DurationMs = _trackDurationMs,
    };

    private static bool ShouldFlashFor(LyricsLoadRequest request) => request.Source switch
    {
        LyricsLoadSource.ExplicitSource => request.ExplicitSourceName != "Local",
        LyricsLoadSource.Default => true,
        _ => true,
    };

    private static string BuildLoadingMessage(LyricsLoadRequest request) => request.Source switch
    {
        LyricsLoadSource.ExplicitSource => $"正在尝试 {request.ExplicitSourceName}…",
        LyricsLoadSource.Default => "正在尝试 LRCLIB…",
        _ => "正在加载歌词…",
    };

    private async Task<LyricsCandidate?> ResolveCandidateAsync(TrackInfo track, LyricsLoadRequest request)
    {
        switch (request.Source)
        {
            case LyricsLoadSource.ExplicitCandidate:
                return NormalizeCandidate(request.ExplicitCandidate);

            case LyricsLoadSource.ExplicitSource:
                {
                    var source = request.ExplicitSourceName ?? string.Empty;
                    var candidate = await _lyricsService.FetchFromSourceAsync(track, source).ConfigureAwait(false);
                    return NormalizeCandidate(candidate);
                }

            case LyricsLoadSource.OverrideOnly:
                {
                    if (request.OverrideOrigin is null) return null;
                    var candidate = await _lyricsService
                        .FetchCandidateAsync(track, request.OverrideOrigin).ConfigureAwait(false);
                    return NormalizeCandidate(candidate);
                }

            case LyricsLoadSource.Default:
                {
                    var key = TrackKey.From(track);
                    if (_overrides.TryGetValue(key, out var persisted))
                    {
                        var candidate = await _lyricsService
                            .FetchCandidateAsync(track, persisted).ConfigureAwait(false);
                        if (candidate is not null)
                        {
                            return NormalizeCandidate(candidate);
                        }
                        // PreserveOverride：override 失败保留状态，不回退；由 ResolveFinalStatusText 提示。
                        if (request.FailurePolicy == LyricsLoadFailurePolicy.PreserveOverride)
                        {
                            return null;
                        }
                    }

                    var lyrics = await _lyricsService.FetchLyricsAsync(track).ConfigureAwait(false);
                    return NormalizeCandidate(lyrics);
                }

            default:
                return null;
        }
    }

    private static LyricsCandidate? NormalizeCandidate(LyricsCandidate? candidate)
    {
        if (candidate is null) return null;
        var (synced, plain) = LyricsTextNormalizer.NormalizeAll(candidate.SyncedLyrics, candidate.PlainLyrics);
        return new LyricsCandidate
        {
            Source = candidate.Source,
            Label = candidate.Label,
            SyncedLyrics = synced,
            PlainLyrics = plain,
            DurationMs = candidate.DurationMs,
            Origin = candidate.Origin,
            IsAvailable = candidate.IsAvailable,
        };
    }

    private string ResolveLyricsSource(LyricsCandidate? candidate, LyricsLoadRequest request)
    {
        if (candidate is not null) return candidate.Source;
        // 没拿到歌词时：保留原有"显示当前来源"的语义。
        return request.Source switch
        {
            LyricsLoadSource.ExplicitSource => request.ExplicitSourceName ?? string.Empty,
            LyricsLoadSource.OverrideOnly => string.Empty,
            _ => string.IsNullOrEmpty(_lyricsActiveSource) ? string.Empty : _lyricsActiveSource,
        };
    }

    private string ResolveFinalStatusText(LyricsCandidate? candidate, LyricsLoadRequest request)
    {
        if (candidate is not null) return string.Empty;

        // override 失败且策略是保留 → 显式提示，避免看起来像"暂无歌词"。
        if (request.Source == LyricsLoadSource.OverrideOnly
            || (request.Source == LyricsLoadSource.Default && request.FailurePolicy == LyricsLoadFailurePolicy.PreserveOverride))
        {
            return "覆盖项源暂不可用，请稍候重试";
        }

        return "暂无歌词";
    }

    private void TriggerLocalMissingPrompt(TrackInfo track)
    {
        bool added;
        lock (_localPromptLock)
        {
            added = _localPromptedTrackIds.Add(track.Id);
        }
        if (added && _lyricsBehavior.Current.PromptForLocalLyricsOnMissing)
        {
            LocalLyricsMissing?.Invoke(track);
        }
    }

    private async Task PollAsync()
    {
        try
        {
            if (_activePlaybackSource is null || !_activePlaybackSource.IsConnected)
            {
                Stop();
                StatusText = _activePlaybackSource is null
                    ? "未配置播放来源"
                    : $"请先连接 {_activePlaybackSource.DisplayName}";
                return;
            }

var playback = await _activePlaybackSource.GetSnapshotAsync().ConfigureAwait(false);
        if (playback is null)
        {
            var wasPlaying = _isPlaying;
            _isPlaying = false;
            if (wasPlaying != _isPlaying)
            {
                IsPlayingChanged?.Invoke(_isPlaying);
            }
            ClearLyrics();
            TrackTitle = string.Empty;
            ArtistName = string.Empty;
            StatusText = "未在播放";
            return;
        }

        TrackTitle = playback.Track.Name;
        ArtistName = playback.Track.Artist;
        var wasPlaying2 = _isPlaying;
        _isPlaying = playback.IsPlaying;
        if (wasPlaying2 != _isPlaying)
        {
            IsPlayingChanged?.Invoke(_isPlaying);
        }
        _lastProgressMs = playback.ProgressMs;
        _lastSampleAt = DateTimeOffset.UtcNow;

        if (_loadedTrackId != playback.Track.Id)
        {
            await LoadTrackAsync(playback.Track).ConfigureAwait(false);
        }

        UpdateLyricFrame(GetInterpolatedProgressMs());
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private async Task LoadTrackAsync(TrackInfo track)
    {
        _loadedTrackId = track.Id;
        _trackDurationMs = track.DurationMs;
        _trackAlbum = track.Album;
        TrackTitle = track.Name;
        ArtistName = track.Artist;
        _trackInfoLayout.ResetForNewTrack();
        ShouldCenterTrackInfo = true;
        ClearLyricLines();
        // 立刻同步清空引擎，防止 await 网络/文件 IO 期间 PollAsync 用旧引擎数据
        // 重新喂出上一首歌的 CurrentLine/PreviousLine（Bug 3）。
        _syncEngine.Clear();

        var key = TrackKey.From(track);
        if (_overrides.TryGetValue(key, out var persisted))
        {
            await LoadLyricsAsync(new LyricsLoadRequest
            {
                Source = LyricsLoadSource.OverrideOnly,
                OverrideOrigin = persisted,
                FailurePolicy = LyricsLoadFailurePolicy.PreserveOverride,
                FlashOnLoading = false,
                FlushFrameImmediately = false,
            }).ConfigureAwait(false);
            return;
        }

        await LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.Default,
            FailurePolicy = LyricsLoadFailurePolicy.FallbackToNext,
            FlashOnLoading = true,
            FlushFrameImmediately = false,
        }).ConfigureAwait(false);
    }

    private long GetInterpolatedProgressMs()
    {
        if (!_isPlaying)
        {
            return Math.Max(0, _lastProgressMs - _syncOffsetMs);
        }

        var elapsed = (long)(DateTimeOffset.UtcNow - _lastSampleAt).TotalMilliseconds;
        // clamp 上限设为 4 个 Poll 周期：HTTP 抖动常使 PollAsync 1~3s 才回来一次，
        // 旧值 PollIntervalMs (800ms) 会让进度被"压"住从而歌词落后真实播放 1+ 秒。
        // 超过该阈值时放弃插值、用上次 Poll 的进度，宁可歌词"停一下"也不要持续滞后。
        const int InterpolationClampMs = PollIntervalMs * 4;
        var clamped = Math.Min(elapsed, InterpolationClampMs);
        return _lastProgressMs + clamped - _syncOffsetMs;
    }

    /// <summary>
    /// 触发候选选择窗口请求事件，由 <see cref="App"/> 在 UI 线程响应并弹出窗口。
    /// </summary>
    public void OpenCandidatePicker()
    {
        CandidatePickerRequested?.Invoke();
    }

    /// <summary>
    /// 用户在候选窗口里点了 [确认]：立即把指定候选喂入主引擎并持久化为覆盖项。
    /// 同步触发一次 <see cref="UpdateLyricFrame"/>，让 AppBar/Overlay 立刻拿到新歌词行
    /// （否则要等下次 Poll 周期——若用户暂停中，可能要等很久）。
    /// </summary>
    public async Task ApplyCandidateAsync(LyricsCandidate candidate)
    {
        if (_loadedTrackId is null) return;

        var track = SnapshotCurrentTrack();
        var key = TrackKey.From(track);
        _overrides[key] = candidate.Origin;
        _overrideStore.Save(key, candidate.Origin);

        await LoadLyricsAsync(new LyricsLoadRequest
        {
            Source = LyricsLoadSource.ExplicitCandidate,
            ExplicitCandidate = candidate,
            FlashOnLoading = true,
            FlushFrameImmediately = true,
        }).ConfigureAwait(false);
    }

    private void ApplyCandidateToEngine(LyricsCandidate? candidate, int durationMs)
    {
        _syncEngine.SetDurationMs(durationMs);
        if (candidate is null)
        {
            _syncEngine.Clear();
            return;
        }

        if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
        {
            var data = LrcParser.Parse(candidate.SyncedLyrics);
            var plain = string.IsNullOrWhiteSpace(candidate.PlainLyrics)
                ? Array.Empty<string>()
                : candidate.PlainLyrics.Split(
                    ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _syncEngine.LoadParsed(data, plain);
        }
        else if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics))
        {
            var plain = candidate.PlainLyrics.Split(
                ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _syncEngine.LoadParsed(null, plain);
        }
        else
        {
            _syncEngine.Clear();
        }
    }

    private void UpdateLyricFrame(long progressMs)
    {
        var frame = _syncEngine.GetFrame(progressMs);
        var shouldShowLyrics = frame.IsActive && !string.IsNullOrWhiteSpace(frame.CurrentLine);
        IsLyricsActive = shouldShowLyrics;

        if (_trackInfoLayout.Update(shouldShowLyrics, frame.MsUntilNextNonEmptyLine))
        {
            ShouldCenterTrackInfo = _trackInfoLayout.ShouldCenter;
        }

        if (shouldShowLyrics)
        {
            CurrentLine = frame.CurrentLine;
            PreviousLine = frame.PreviousLine;
            NextLine = frame.NextLine;
        }
        else
        {
            CurrentLine = string.Empty;
            PreviousLine = string.Empty;
            NextLine = string.Empty;
        }

        ProgressMsChanged?.Invoke(progressMs);
    }

    private void ResetDisplay(string status)
    {
        StatusText = status;
        TrackTitle = string.Empty;
        ArtistName = string.Empty;
        LyricsSource = string.Empty;
        _loadedTrackId = null;
        ClearLyrics();
    }

    private void ClearLyrics()
    {
        ClearLyricLines();
        _syncEngine.Clear();
        lock (_localPromptLock)
        {
            _localPromptedTrackIds.Clear();
        }
    }

    private void ClearLyricLines()
    {
        _trackInfoLayout.Reset();
        ShouldCenterTrackInfo = true;
        IsLyricsActive = false;
        CurrentLine = string.Empty;
        PreviousLine = string.Empty;
        NextLine = string.Empty;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void InjectLoadedTrack(TrackInfo track)
    {
        _loadedTrackId = track.Id;
        _trackDurationMs = track.DurationMs;
        _trackAlbum = track.Album;
        TrackTitle = track.Name;
        ArtistName = track.Artist;
    }

    internal void ClearLocalPromptState()
    {
        lock (_localPromptLock)
        {
            _localPromptedTrackIds.Clear();
        }
    }

    public void Dispose()
    {
        DetachActiveSourceEvents(_activePlaybackSource);
        _timer.Stop();
        _timer.Dispose();
    }

    private void AttachActiveSourceEvents(IPlaybackSource? source)
    {
        if (source is null) return;
        source.AuthenticationFailed += OnActiveSourceAuthenticationFailed;
    }

    private void DetachActiveSourceEvents(IPlaybackSource? source)
    {
        if (source is null) return;
        source.AuthenticationFailed -= OnActiveSourceAuthenticationFailed;
    }
}
