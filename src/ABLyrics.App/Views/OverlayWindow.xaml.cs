using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using ABLyrics.App.Configuration;
using ABLyrics.App.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ABLyrics.App.Views;

public partial class OverlayWindow : Window
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly LyricsHostLifecycle _lifecycle;
    private readonly Forms.ContextMenuStrip _sourceMenu = new();

    public OverlayWindow(PlaybackCoordinator coordinator, DisplaySettingsService displaySettings)
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

        TrackInfoPanel.MouseLeftButtonDown += OnTrackInfoMouseLeftButtonDown;

        _lifecycle = new LyricsHostLifecycle(
            this,
            coordinator,
            displaySettings,
            ApplyStyle,
            ApplyLayout,
            ChromeBorder);
    }

    private void ApplyStyle(DisplayStyleSettings style)
    {
        LyricsStyleApplier.ApplyOverlay(
            this,
            ChromeBorder,
            TrackTitleText,
            ArtistNameText,
            PrimaryLineText,
            SecondaryLineText,
            style);
    }

    private void ApplyLayout()
    {
        LyricsLayoutController.ApplyOverlayLayout(
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

    private Drawing.Point GetSourceTagScreenPos()
    {
        var pos = SourceTagText.PointToScreen(new System.Windows.Point(0, 0));
        return new Drawing.Point((int)pos.X, (int)pos.Y + (int)SourceTagText.ActualHeight);
    }

    private void OnTrackInfoMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var trackId = _coordinator.GetCurrentTrackId();
            if (string.IsNullOrWhiteSpace(trackId)) return;

            try
            {
                Process.Start(new ProcessStartInfo("spotify:track:" + trackId) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;
        DragMove();
    }
}
