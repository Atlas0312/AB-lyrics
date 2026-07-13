using System.Windows;
using System.Windows.Threading;
using SpotLyrics.App.Configuration;
using SpotLyrics.App.Services;
using SpotLyrics.App.Services.Lyrics;
using SpotLyrics.App.Services.Spotify;
using SpotLyrics.App.Views;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SpotLyrics.App;

public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static PlaybackCoordinator Coordinator { get; private set; } = null!;
    public static DisplaySettingsService DisplaySettings { get; private set; } = null!;

    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayContextMenu;
    private AppBarWindow? _appBarWindow;
    private OverlayWindow? _overlayWindow;

    private Forms.ToolStripMenuItem? _loginMenuItem;
    private Forms.ToolStripMenuItem? _overlayToggle;

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
        Coordinator = new PlaybackCoordinator(authService, playbackService, lyricsService, DisplaySettings);
        Coordinator.IsPlayingChanged += OnIsPlayingChanged;

        CreateTrayIcon();

        _ = TryRestoreSessionAsync();

        base.OnStartup(e);
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            if (await Coordinator.TryRestoreSessionAsync())
            {
                Coordinator.Start();
                UpdateMenuStates();
                UpdateTooltip();

                if (Coordinator.IsPlaying)
                {
                    ShowAppBar();
                }
            }
            else
            {
                UpdateTooltip();
            }
        }
        catch (Exception ex)
        {
            if (DevExceptionReporter.IsEnabled)
            {
                DevExceptionReporter.Show(ex, "Spotify 会话恢复失败");
            }
            UpdateTooltip();
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(
                System.Reflection.Assembly.GetExecutingAssembly().Location),
            Text = "SpotLyrics",
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

        menu.Items.Add(new Forms.ToolStripMenuItem("SpotLyrics") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());

        _loginMenuItem = new Forms.ToolStripMenuItem("登录 Spotify");
        _loginMenuItem.Click += (_, _) => OnLoginLogoutClick();
        menu.Items.Add(_loginMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        _overlayToggle = new Forms.ToolStripMenuItem("悬浮歌词");
        _overlayToggle.Click += (_, _) => ToggleOverlay();
        menu.Items.Add(new Forms.ToolStripSeparator());

        var styleItem = new Forms.ToolStripMenuItem("样式设置…");
        styleItem.Click += (_, _) => OnStyleSettingsClick();
        menu.Items.Add(styleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void UpdateMenuStates()
    {
        if (_loginMenuItem is null) return;

        _loginMenuItem.Text = Coordinator.IsAuthenticated ? "退出登录" : "登录 Spotify";
        _overlayToggle!.Checked = _overlayWindow is not null;
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null) return;

        if (!Coordinator.IsAuthenticated)
        {
            _trayIcon.Text = "SpotLyrics\n未登录 Spotify";
            return;
        }

        var title = Coordinator.TrackTitle;
        var artist = Coordinator.ArtistName;
        var line = Coordinator.CurrentLine;
        var source = Coordinator.LyricsSource;

        if (string.IsNullOrWhiteSpace(title))
        {
            _trayIcon.Text = "SpotLyrics\n未在播放";
            return;
        }

        var tip = $"SpotLyrics\n🎵 {title} - {artist}";
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

    private async void OnLoginLogoutClick()
    {
        try
        {
            if (Coordinator.IsAuthenticated)
            {
                Coordinator.Logout();
                _appBarWindow?.Close();
                _appBarWindow = null;
                _overlayWindow?.Close();
                _overlayWindow = null;
            }
            else
            {
                await Coordinator.LoginAsync();
                Coordinator.Start();
            }
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "Spotify 登录/登出失败");
            System.Windows.MessageBox.Show(ex.Message, "Spotify", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            UpdateMenuStates();
            UpdateTooltip();
        }
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
            var window = new StyleSettingsWindow(DisplaySettings, Coordinator);
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
