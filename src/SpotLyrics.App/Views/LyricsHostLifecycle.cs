using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SpotLyrics.App.Configuration;
using SpotLyrics.App.Services;

namespace SpotLyrics.App.Views;

/// <summary>
/// 歌词展示窗口的共享生命周期：插值刷新、样式/布局响应、设置变更订阅。
/// </summary>
internal sealed class LyricsHostLifecycle : IDisposable
{
    private readonly Window _host;
    private readonly PlaybackCoordinator _coordinator;
    private readonly DisplaySettingsService _displaySettings;
    private readonly Action<DisplayStyleSettings> _applyStyle;
    private readonly Action _applyLayout;
    private readonly Action? _onClosed;
    private readonly Border _chromeBorder;
    private readonly DispatcherTimer _interpolationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16),
    };

    public LyricsHostLifecycle(
        Window host,
        PlaybackCoordinator coordinator,
        DisplaySettingsService displaySettings,
        Action<DisplayStyleSettings> applyStyle,
        Action applyLayout,
        Border chromeBorder,
        Action? onClosed = null)
    {
        _host = host;
        _coordinator = coordinator;
        _displaySettings = displaySettings;
        _applyStyle = applyStyle;
        _applyLayout = applyLayout;
        _onClosed = onClosed;
        _chromeBorder = chromeBorder;

        _interpolationTimer.Tick += (_, _) => _coordinator.TickInterpolation();
        _interpolationTimer.Start();
        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        _coordinator.LoadingFlash += OnLoadingFlash;

        _host.Loaded += OnHostLoaded;
        _host.Closed += OnHostClosed;
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        _applyStyle(_displaySettings.Current);
        _applyLayout();
        _displaySettings.SettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, DisplayStyleSettings style)
    {
        _host.Dispatcher.Invoke(() => _applyStyle(style));
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlaybackCoordinator.ShouldCenterTrackInfo))
        {
            _host.Dispatcher.Invoke(_applyLayout);
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnLoadingFlash()
    {
        _host.Dispatcher.Invoke(() =>
        {
            var flashTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            var elapsed = 0;
            flashTimer.Tick += (_, _) =>
            {
                elapsed += 16;
                var progress = Math.Min(1.0, elapsed / 150.0);
                LyricsStyleApplier.ApplyFlash(_chromeBorder, 1.0 - progress);
                if (progress >= 1.0)
                {
                    flashTimer.Stop();
                    LyricsStyleApplier.ClearFlash(_chromeBorder);
                }
            };
            flashTimer.Start();
        });
    }

    public void Dispose()
    {
        _coordinator.PropertyChanged -= OnCoordinatorPropertyChanged;
        _coordinator.LoadingFlash -= OnLoadingFlash;
        _displaySettings.SettingsChanged -= OnDisplaySettingsChanged;
        _interpolationTimer.Stop();
        _onClosed?.Invoke();
    }
}
