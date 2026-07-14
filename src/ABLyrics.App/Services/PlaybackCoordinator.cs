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
        await ReloadCurrentTrackAsync().ConfigureAwait(false);
    }

    public async Task ForceReloadAsync()
    {
        if (_loadedTrackId is null) return;
        await ReloadCurrentTrackAsync().ConfigureAwait(false);
    }

    internal async Task ReloadCurrentTrackAsync()
    {
        if (_loadedTrackId is null) return;

        var currentTrack = new TrackInfo
        {
            Id = _loadedTrackId,
            Name = TrackTitle,
            Artist = ArtistName,
            Album = _trackAlbum,
            DurationMs = _trackDurationMs,
        };

        LyricsResult? lyrics;
        if (!string.IsNullOrWhiteSpace(_lyricsActiveSource))
        {
            if (_lyricsActiveSource != "Local")
            {
                StatusText = $"正在尝试 {_lyricsActiveSource}…";
                LoadingFlash?.Invoke();
            }
            lyrics = await _lyricsService.FetchFromSourceAsync(currentTrack, _lyricsActiveSource).ConfigureAwait(false);
        }
        else
        {
            StatusText = "正在尝试 LRCLIB…";
            LoadingFlash?.Invoke();
            lyrics = await _lyricsService.FetchLyricsAsync(currentTrack).ConfigureAwait(false);
        }

        _syncEngine.SetDurationMs(_trackDurationMs);
        _syncEngine.Load(lyrics);
        LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_lyricsActiveSource) ? string.Empty : _lyricsActiveSource);
        StatusText = lyrics is null ? "暂无歌词" : string.Empty;

        if (_lyricsActiveSource == "Local" && lyrics is null)
        {
            bool added;
            lock (_localPromptLock)
            {
                added = _localPromptedTrackIds.Add(currentTrack.Id);
            }
            if (added && _lyricsBehavior.Current.PromptForLocalLyricsOnMissing)
            {
                LocalLyricsMissing?.Invoke(currentTrack);
            }
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

        var key = TrackKey.From(track);
        if (_overrides.TryGetValue(key, out var persistedOrigin))
        {
            var candidate = await TryLoadFromOriginAsync(track, persistedOrigin).ConfigureAwait(false);
            if (candidate is not null)
            {
                _overrideCandidate = candidate;
                ApplyCandidateToEngine(candidate);
                LyricsSource = candidate.Source;
                StatusText = string.Empty;
                return;
            }
            // 文件丢失：移除 override 并回退
            _overrideStore.Remove(key);
            _overrides.Remove(key);
        }

        StatusText = "正在尝试 LRCLIB…";
        LoadingFlash?.Invoke();
        var lyrics = await _lyricsService.FetchLyricsAsync(track).ConfigureAwait(false);
        _syncEngine.SetDurationMs(_trackDurationMs);
        _syncEngine.Load(lyrics);
        LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_lyricsActiveSource) ? string.Empty : _lyricsActiveSource);
        StatusText = lyrics is null ? "暂无歌词" : string.Empty;
    }

    private long GetInterpolatedProgressMs()
    {
        if (!_isPlaying)
        {
            return Math.Max(0, _lastProgressMs - _syncOffsetMs);
        }

        var elapsed = DateTimeOffset.UtcNow - _lastSampleAt;
        var clamped = Math.Min((long)elapsed.TotalMilliseconds, PollIntervalMs);
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
    /// </summary>
    public async Task ApplyCandidateAsync(LyricsCandidate candidate)
    {
        var track = new TrackInfo
        {
            Id = _loadedTrackId ?? string.Empty,
            Name = _trackTitle,
            Artist = _artistName,
            Album = _trackAlbum,
            DurationMs = _trackDurationMs,
        };
        var key = TrackKey.From(track);
        _overrides[key] = candidate.Origin;
        _overrideStore.Save(key, candidate.Origin);
        _overrideCandidate = candidate;
        ApplyCandidateToEngine(candidate);
        LyricsSource = candidate.Source;
        StatusText = string.Empty;
        await Task.CompletedTask;
    }

    private async Task<LyricsCandidate?> TryLoadFromOriginAsync(TrackInfo track, CandidateOrigin origin)
    {
        var result = await _lyricsService.FetchCandidateAsync(track, origin).ConfigureAwait(false);
        if (result is null) return null;
        return new LyricsCandidate
        {
            Source = result.Source,
            Label = "覆盖项",
            SyncedLyrics = result.SyncedLyrics,
            PlainLyrics = result.PlainLyrics,
            DurationMs = track.DurationMs,
            Origin = origin,
        };
    }

    private void ApplyCandidateToEngine(LyricsCandidate candidate)
    {
        _syncEngine.SetDurationMs(candidate.DurationMs);
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
            _syncEngine.LoadParsed(null, Array.Empty<string>());
        }
    }

    private void UpdateLyricFrame(long progressMs)
    {
        var frame = _syncEngine.GetFrame(progressMs);
        var shouldShowLyrics = frame.IsActive && !string.IsNullOrWhiteSpace(frame.CurrentLine);
        IsLyricsActive = shouldShowLyrics;

        if (_trackInfoLayout.Update(shouldShowLyrics))
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
        _syncEngine.Load(null);
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
        _timer.Stop();
        _timer.Dispose();
    }
}