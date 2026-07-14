using System.Windows;
using System.Windows.Threading;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using ABLyrics.App.Services.Spotify;
using ABLyrics.App.Views;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ABLyrics.App;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static PlaybackCoordinator Coordinator { get; private set; } = null!;
    public static DisplaySettingsService DisplaySettings { get; private set; } = null!;
    public static LyricsBehaviorService LyricsBehavior { get; private set; } = null!;

    public static PlaybackSourceRegistry GetPlaybackSourceRegistry()
    {
        var app = (App)Current;
        return app._sourceRegistry ?? throw new InvalidOperationException("Registry 尚未初始化。");
    }

    internal static Services.Lyrics.LocalLyricsProvider GetLocalLyricsProvider()
    {
        return new Services.Lyrics.LocalLyricsProvider(Settings);
    }

    private void OnCoordinatorSourceStateChanged()
    {
        UpdateTooltip();
        UpdateMenuStates();
    }

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayContextMenu;
    private AppBarWindow? _appBarWindow;
    private OverlayWindow? _overlayWindow;

    private Forms.ToolStripMenuItem? _overlayToggle;
    private PlaybackSourceRegistry? _sourceRegistry;
    private Services.Lyrics.LyricsSearchService? _searchService;
    private Configuration.LyricsOverrideStore? _overrideStore;
    private Views.LyricsCandidatePickerWindow? _candidatePicker;
    private IReadOnlyDictionary<string, bool> _libraryAvailability = new Dictionary<string, bool>();
    public static event Action<IReadOnlyDictionary<string, bool>>? LibraryAvailabilityChanged;
    public static IReadOnlyDictionary<string, bool> LibraryAvailability =>
        Current is App app ? app._libraryAvailability : new Dictionary<string, bool>();

    protected override void OnStartup(StartupEventArgs e)
    {
        if (DevExceptionReporter.IsEnabled)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        Settings = ConfigurationLoader.Load();

        var displayDefaults = new DisplayStyleSettings
        {
            BarHeight = Settings.Ui.AppBarHeight,
        };
        DisplaySettings = new DisplaySettingsService(displayDefaults);

        var authService = new SpotifyAuthService(Settings);
        var playbackService = new SpotifyPlaybackService(authService);
        var lyricsService = new LyricsService(Settings);
        LyricsBehavior = new LyricsBehaviorService(new LyricsBehaviorSettings());

        _sourceRegistry = new PlaybackSourceRegistry();
        _sourceRegistry.Register(new SpotifyPlaybackSource(authService, playbackService, Settings));

        _searchService = new Services.Lyrics.LyricsSearchService(Settings);
        _overrideStore = new Configuration.LyricsOverrideStore();

        Coordinator = new PlaybackCoordinator(
            _sourceRegistry,
            Settings.Playback.ActiveSource,
            lyricsService,
            LyricsBehavior,
            DisplaySettings,
            _searchService,
            _overrideStore);
        Coordinator.IsPlayingChanged += OnIsPlayingChanged;
        Coordinator.SourceStateChanged += () => Dispatcher.BeginInvoke(OnCoordinatorSourceStateChanged);
        Coordinator.CandidatePickerRequested += () => Dispatcher.BeginInvoke(ShowCandidatePicker);

        CreateTrayIcon();

        _ = TryRestoreSessionAsync();
        _ = ProbeLibraryAvailabilityAsync();

        base.OnStartup(e);
    }

    private async Task ProbeLibraryAvailabilityAsync()
    {
        if (_searchService is null) return;
        try
        {
            var result = await _searchService.ProbeAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                _libraryAvailability = result;
                LibraryAvailabilityChanged?.Invoke(result);
            });
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "歌词库探活失败");
        }
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            if (await Coordinator.TryRestoreSessionAsync())
            {
                Coordinator.Start();
            }
        }
        catch (Exception ex)
        {
            if (DevExceptionReporter.IsEnabled)
            {
                DevExceptionReporter.Show(ex, "播放来源会话恢复失败");
            }
        }
        finally
        {
            UpdateMenuStates();
            UpdateTooltip();
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetExecutingAssembly().Location),
            Text = "ABLyrics",
            Visible = true,
        };

        _trayContextMenu = BuildTrayContextMenu();
        _trayIcon.ContextMenuStrip = _trayContextMenu;
    }

    /// <summary>
    /// 外部可以通过 App.GetTrayContextMenu() 获取与托盘相同的右键菜单。
    /// </summary>
    public static Forms.ContextMenuStrip GetTrayContextMenu()
    {
        var app = (App)Current;
        return app._trayContextMenu ?? app.BuildTrayContextMenu();
    }

    private Forms.ContextMenuStrip BuildTrayContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add(new Forms.ToolStripMenuItem("ABLyrics") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());

        _overlayToggle = new Forms.ToolStripMenuItem("悬浮歌词");
        _overlayToggle.Click += (_, _) => ToggleOverlay();
        menu.Items.Add(_overlayToggle);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var pickerItem = new Forms.ToolStripMenuItem("选择歌词版本…");
        pickerItem.Click += (_, _) => Dispatcher.BeginInvoke(ShowCandidatePicker);
        menu.Items.Add(pickerItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var settingsItem = new Forms.ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => OnStyleSettingsClick();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void UpdateMenuStates()
    {
        _overlayToggle!.Checked = _overlayWindow is not null;
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null) return;

        if (Coordinator.ActivePlaybackSource is null)
        {
            _trayIcon.Text = "ABLyrics\n未配置播放来源";
            return;
        }

        if (!Coordinator.IsSourceConnected)
        {
            _trayIcon.Text = $"ABLyrics\n请先连接 {Coordinator.ActivePlaybackSource.DisplayName}";
            return;
        }

        var title = Coordinator.TrackTitle;
        var artist = Coordinator.ArtistName;
        var line = Coordinator.CurrentLine;
        var source = Coordinator.LyricsSource;

        if (string.IsNullOrWhiteSpace(title))
        {
            _trayIcon.Text = "ABLyrics\n未在播放";
            return;
        }

        var tip = $"ABLyrics\n🎵 {title} - {artist}";
        if (!string.IsNullOrWhiteSpace(line))
        {
            tip += $"\n歌词：{line}";
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            tip += $" | 来源：{source}";
        }

        _trayIcon.Text = tip.Length > 128 ? tip[..125] + "…" : tip;
    }

    private void ToggleAppBar()
    {
        if (_appBarWindow is not null)
        {
            _appBarWindow.Close();
            _appBarWindow = null;
        }
        else
        {
            ShowAppBar();
        }
        UpdateMenuStates();
    }

    private void ShowAppBar()
    {
        if (_appBarWindow is not null) return;

        _appBarWindow = new AppBarWindow(Coordinator, DisplaySettings);
        _appBarWindow.Closed += (_, _) =>
        {
            _appBarWindow = null;
            UpdateMenuStates();
        };
        _appBarWindow.Show();
    }

    private void ToggleOverlay()
    {
        if (_overlayWindow is not null)
        {
            _overlayWindow.Close();
            _overlayWindow = null;
        }
        else
        {
            _overlayWindow = new OverlayWindow(Coordinator, DisplaySettings)
            {
                Left = SystemParameters.WorkArea.Right - 740,
                Top = SystemParameters.WorkArea.Bottom - 180,
            };
            _overlayWindow.Closed += (_, _) =>
            {
                _overlayWindow = null;
                UpdateMenuStates();
            };
            _overlayWindow.Show();
        }
        UpdateMenuStates();
    }

    private void OnStyleSettingsClick()
    {
        try
        {
            var window = new StyleSettingsWindow(DisplaySettings, Coordinator, LyricsBehavior);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "打开样式设置失败");
            if (!DevExceptionReporter.IsEnabled)
            {
                System.Windows.MessageBox.Show(ex.Message, "样式设置", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    /// 创建或激活选版本窗口。单例：关闭按钮 = Hide，不释放。
    /// </summary>
    public void ShowCandidatePicker()
    {
        if (_searchService is null || _overrideStore is null) return;

        TrackInfo? track = null;
        if (Coordinator.GetCurrentTrackId() is { } trackId
            && !string.IsNullOrEmpty(trackId))
        {
            track = new TrackInfo
            {
                Id = trackId,
                Name = Coordinator.TrackTitle,
                Artist = Coordinator.ArtistName,
                Album = Coordinator.TrackAlbum,
                DurationMs = 0,
            };
        }

        if (_candidatePicker is null)
        {
            _candidatePicker = new LyricsCandidatePickerWindow(
                Coordinator, _searchService, _overrideStore, track);
        }

        if (_candidatePicker.WindowState == WindowState.Minimized)
        {
            _candidatePicker.WindowState = WindowState.Normal;
        }
        _candidatePicker.Show();
        _candidatePicker.Activate();
    }

    private void OnIsPlayingChanged(bool isPlaying)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateTooltip();
            if (isPlaying && _appBarWindow is null)
            {
                ShowAppBar();
                UpdateMenuStates();
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (DevExceptionReporter.IsEnabled)
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        _appBarWindow?.Close();
        _overlayWindow?.Close();
        _candidatePicker?.Close();
        Coordinator.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DevExceptionReporter.Show(e.Exception, "未处理的 UI 线程异常");
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            DevExceptionReporter.Show(ex, "未处理的应用域异常");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DevExceptionReporter.Show(e.Exception, "未观察到的 Task 异常");
        e.SetObserved();
    }
}
