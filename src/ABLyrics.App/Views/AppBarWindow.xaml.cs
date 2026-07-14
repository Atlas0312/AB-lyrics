using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Native;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ABLyrics.App.Views;

public partial class AppBarWindow : Window
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly LocalLyricsProvider _localProvider;
    private readonly LyricsHostLifecycle _lifecycle;
    private readonly Forms.ContextMenuStrip _sourceMenu = new();
    private AppBarController? _appBarController;

    public AppBarWindow(PlaybackCoordinator coordinator, DisplaySettingsService displaySettings)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _localProvider = App.GetLocalLyricsProvider();
        DataContext = coordinator;

        _coordinator.LocalLyricsMissing += OnLocalLyricsMissing;

        _sourceMenu.ItemClicked += (_, e) =>
        {
            if (e.ClickedItem?.Tag is string source)
            {
                _ = _coordinator.SetSourceAsync(source);
            }
        };

        _lifecycle = new LyricsHostLifecycle(
            this,
            coordinator,
            displaySettings,
            ApplyStyle,
            ApplyLayout,
            ChromeBorder,
            onClosed: () =>
            {
                _appBarController?.Dispose();
                _appBarController = null;
            });

        TrackInfoPanel.MouseLeftButtonDown += OnTrackInfoMouseLeftButtonDown;
        PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _appBarController = new AppBarController(this, App.DisplaySettings.Current.BarHeight);
        _appBarController.Attach(hwnd);
        _appBarController.Register();
    }

    private void ApplyStyle(DisplayStyleSettings style)
    {
        LyricsStyleApplier.ApplyAppBar(
            this,
            ChromeBorder,
            TrackTitleText,
            ArtistNameText,
            PrimaryLineText,
            SecondaryLineText,
            SourceTagText,
            style);
        _appBarController?.UpdateHeight(style.BarHeight);
    }

    private void ApplyLayout()
    {
        LyricsLayoutController.ApplyAppBarLayout(
            TrackInfoPanel,
            LyricsPanel,
            _coordinator.ShouldCenterTrackInfo);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _ = _coordinator.ForceReloadAsync();
    }

    private void OnSourceTagClick(object sender, RoutedEventArgs e)
    {
        _sourceMenu.Items.Clear();

        foreach (var source in _coordinator.AvailableSources)
        {
            var top = new Forms.ToolStripMenuItem(source)
            {
                Tag = source,
                Checked = source == _coordinator.ActiveSource,
            };

            if (source == "Local")
            {
                var trackId = _coordinator.GetCurrentTrackId();
                var importItem = new Forms.ToolStripMenuItem("导入歌词文件…")
                {
                    Enabled = !string.IsNullOrEmpty(trackId),
                };
                importItem.Click += (_, _) => _ = PromptForLocalLyricsAsync(GetCurrentTrack());
                top.DropDownItems.Add(importItem);

                var openFolderItem = new Forms.ToolStripMenuItem("打开歌词库文件夹");
                openFolderItem.Click += (_, _) => OpenLocalLyricsLibrary();
                top.DropDownItems.Add(openFolderItem);
            }

            _sourceMenu.Items.Add(top);
        }

        _sourceMenu.Show(GetSourceTagScreenPos());
    }

    private void OnLocalLyricsMissing(TrackInfo track)
    {
        Dispatcher.BeginInvoke(() => _ = PromptForLocalLyricsAsync(track));
    }

    private TrackInfo GetCurrentTrack()
    {
        return new TrackInfo
        {
            Id = _coordinator.GetCurrentTrackId() ?? string.Empty,
            Name = _coordinator.TrackTitle,
            Artist = _coordinator.ArtistName,
        };
    }

    private async Task PromptForLocalLyricsAsync(TrackInfo track)
    {
        if (string.IsNullOrEmpty(track.Id)) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"为 {track.Artist} - {track.Name} 选择本地歌词",
            Filter = "LRC 歌词 (*.lrc)|*.lrc|文本歌词 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FilterIndex = 1,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await _localProvider.ImportAsync(dialog.FileName, track);
            if (_coordinator.ActiveSource != "Local")
            {
                await _coordinator.SetSourceAsync("Local");
            }
            else
            {
                await _coordinator.ForceReloadAsync();
            }
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "导入本地歌词失败");
        }
    }

    private void OpenLocalLyricsLibrary()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_localProvider.LibraryPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "打开歌词库失败");
        }
    }

    private void OnTrackInfoMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OpenSpotifyTrack();
        }
    }

    private void OpenSpotifyTrack()
    {
        var trackId = _coordinator.GetCurrentTrackId();
        if (string.IsNullOrWhiteSpace(trackId)) return;

        try
        {
            Process.Start(new ProcessStartInfo("spotify:track:" + trackId) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = App.GetTrayContextMenu();
        var pos = PointToScreen(e.GetPosition(this));
        menu.Show(new Drawing.Point((int)pos.X, (int)pos.Y));
        e.Handled = true;
    }

    private Drawing.Point GetSourceTagScreenPos()
    {
        var pos = SourceTagText.PointToScreen(new System.Windows.Point(0, 0));
        return new Drawing.Point((int)pos.X, (int)pos.Y + (int)SourceTagText.ActualHeight);
    }
}
