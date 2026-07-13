using System.ComponentModel;
using System.Runtime.CompilerServices;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Spotify;

namespace ABLyrics.App.Services;

public sealed class PlaybackCoordinator : INotifyPropertyChanged, IDisposable
{
    private const int PollIntervalMs = 800;

    private readonly ISpotifyAuthService _authService;
    private readonly ISpotifyPlaybackService _playbackService;
    private readonly ILyricsService _lyricsService;
    private readonly LyricsSyncEngine _syncEngine = new();
    private readonly TrackInfoLayoutState _trackInfoLayout = new();
    private readonly System.Timers.Timer _timer;

    private string _activeSource = string.Empty;
    private string _statusText = "未连接 Spotify";
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
        ISpotifyAuthService authService,
        ISpotifyPlaybackService playbackService,
        ILyricsService lyricsService,
        DisplaySettingsService? displaySettings = null)
    {
        _authService = authService;
        _playbackService = playbackService;
        _lyricsService = lyricsService;

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
    public string ActiveSource => _activeSource;
    public IReadOnlyList<string> AvailableSources => _lyricsService.AvailableSources;
    public string? GetCurrentTrackId() => _loadedTrackId;
    public event Action? LoadingFlash;
    public event Action<bool>? IsPlayingChanged;

    public bool IsRunning => _timer.Enabled;

    public bool IsAuthenticated => _authService.IsAuthenticated;

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!_authService.IsAuthenticated)
        {
            return false;
        }

        if (!await _authService.TryRestoreSessionAsync().ConfigureAwait(false))
        {
            ResetDisplay("登录已过期，请重新登录");
            return false;
        }

        StatusText = "已连接 Spotify";
        return true;
    }

    public async Task LoginAsync()
    {
        if (_authService.IsAuthenticated)
        {
            await _authService.EnsureAuthenticatedAsync().ConfigureAwait(false);
        }
        else
        {
            await _authService.LoginInteractiveAsync().ConfigureAwait(false);
        }

        StatusText = "已连接 Spotify";
    }

    public void Logout()
    {
        Stop();
        _authService.Logout();
        ResetDisplay("已退出 Spotify 登录");
    }

    public void Start()
    {
        if (!_timer.Enabled)
        {
            _timer.Start();
            StatusText = _authService.IsAuthenticated ? "正在监听播放…" : "请先登录 Spotify";
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
        _activeSource = sourceName;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveSource)));
        await ReloadCurrentTrackAsync().ConfigureAwait(false);
    }

    public async Task ForceReloadAsync()
    {
        if (_loadedTrackId is null) return;
        await ReloadCurrentTrackAsync().ConfigureAwait(false);
    }

    private async Task ReloadCurrentTrackAsync()
    {
        if (_loadedTrackId is null) return;

        var currentTrack = new TrackInfo
        {
            Id = _loadedTrackId,
            Name = TrackTitle,
            Artist = ArtistName,
            DurationMs = _trackDurationMs,
        };

        LyricsResult? lyrics;
        if (!string.IsNullOrWhiteSpace(_activeSource))
        {
            if (_activeSource != "Local")
            {
                StatusText = $"正在尝试 {_activeSource}…";
                LoadingFlash?.Invoke();
            }
            lyrics = await _lyricsService.FetchFromSourceAsync(currentTrack, _activeSource).ConfigureAwait(false);
        }
        else
        {
            StatusText = "正在尝试 LRCLIB…";
            LoadingFlash?.Invoke();
            lyrics = await _lyricsService.FetchLyricsAsync(currentTrack).ConfigureAwait(false);
        }

        _syncEngine.SetDurationMs(_trackDurationMs);
        _syncEngine.Load(lyrics);
        LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_activeSource) ? string.Empty : _activeSource);
        StatusText = lyrics is null ? "暂无歌词" : string.Empty;
    }

    private async Task PollAsync()
    {
        try
        {
            if (!_authService.IsAuthenticated)
            {
                ResetDisplay("请先登录 Spotify");
                return;
            }

            var playback = await _playbackService.GetCurrentPlaybackAsync().ConfigureAwait(false);
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
        _trackInfoLayout.ResetForNewTrack();
        ShouldCenterTrackInfo = true;
        ClearLyricLines();

        StatusText = "正在尝试 LRCLIB…";
        LoadingFlash?.Invoke();
        var lyrics = await _lyricsService.FetchLyricsAsync(track).ConfigureAwait(false);
        _syncEngine.SetDurationMs(_trackDurationMs);
        _syncEngine.Load(lyrics);
        LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_activeSource) ? string.Empty : _activeSource);
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

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();

        if (_playbackService is IDisposable disposablePlayback)
        {
            disposablePlayback.Dispose();
        }

        if (_authService is IDisposable disposableAuth)
        {
            disposableAuth.Dispose();
        }
    }
}
