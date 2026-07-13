using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ABLyrics.App.Configuration;
using ABLyrics.App.Native;
using ABLyrics.App.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ABLyrics.App.Views;

public partial class AppBarWindow : Window
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly LyricsHostLifecycle _lifecycle;
    private readonly Forms.ContextMenuStrip _sourceMenu = new();
    private AppBarController? _appBarController;

    public AppBarWindow(PlaybackCoordinator coordinator, DisplaySettingsService displaySettings)
    {
        InitializeComponent();
        _coordinator = coordinator;
        DataContext = coordinator;

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
            var item = new Forms.ToolStripMenuItem(source)
            {
                Tag = source,
                Checked = source == _coordinator.ActiveSource,
            };
            _sourceMenu.Items.Add(item);
        }
        _sourceMenu.Show(GetSourceTagScreenPos());
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
