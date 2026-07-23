using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ABLyrics.App.Configuration;
using ABLyrics.App.Services;

namespace ABLyrics.App.Views;

/// <summary>
/// 歌词展示窗口的共享生命周期：插值刷新、样式/布局响应、设置变更订阅。
/// </summary>
internal sealed class LyricsHostLifecycle : IDisposable
{
    private const double FlashOpacityMin = 0.6;
    private const double FlashOpacityMax = 1.0;
    private const double FlashPulsePeriodMs = 700;

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
    private DispatcherTimer? _flashPulseTimer;
    private DispatcherTimer? _flashFadeTimer;
    private int _activeFlashCount;
    private int _pulseElapsedMs;
    private double _currentFlashOpacity = FlashOpacityMax;

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
        _coordinator.LoadingFlashCompleted += OnLoadingFlashCompleted;

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
            StopFlashFadeTimer();
            _activeFlashCount++;
            if (_activeFlashCount == 1)
            {
                StartFlashPulse();
            }
        });
    }

    private void OnLoadingFlashCompleted()
    {
        _host.Dispatcher.Invoke(() =>
        {
            _activeFlashCount = Math.Max(0, _activeFlashCount - 1);
            if (_activeFlashCount > 0)
            {
                return;
            }

            StopFlashPulseTimer();
            StartFlashFadeOut();
        });
    }

    private void StartFlashPulse()
    {
        StopFlashPulseTimer();
        _pulseElapsedMs = 0;
        _currentFlashOpacity = FlashOpacityMax;
        LyricsStyleApplier.ApplyFlash(_chromeBorder, _currentFlashOpacity);

        var pulseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _flashPulseTimer = pulseTimer;
        pulseTimer.Tick += (_, _) =>
        {
            _pulseElapsedMs += 16;
            // sine 波在 [0,1]：0.6 ↔ 1.0 之间来回浮动
            var wave = (Math.Sin(_pulseElapsedMs / FlashPulsePeriodMs * Math.PI * 2) + 1) * 0.5;
            _currentFlashOpacity = FlashOpacityMin + (FlashOpacityMax - FlashOpacityMin) * wave;
            LyricsStyleApplier.ApplyFlash(_chromeBorder, _currentFlashOpacity);
        };
        pulseTimer.Start();
    }

    private void StartFlashFadeOut()
    {
        StopFlashFadeTimer();
        var startOpacity = _currentFlashOpacity;
        var flashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _flashFadeTimer = flashTimer;
        var elapsed = 0;
        flashTimer.Tick += (_, _) =>
        {
            elapsed += 16;
            var progress = Math.Min(1.0, elapsed / 150.0);
            LyricsStyleApplier.ApplyFlash(_chromeBorder, startOpacity * (1.0 - progress));
            if (progress >= 1.0)
            {
                flashTimer.Stop();
                if (ReferenceEquals(_flashFadeTimer, flashTimer))
                {
                    _flashFadeTimer = null;
                }

                LyricsStyleApplier.ClearFlash(_chromeBorder);
                _currentFlashOpacity = 0;
            }
        };
        flashTimer.Start();
    }

    private void StopFlashPulseTimer()
    {
        if (_flashPulseTimer is null)
        {
            return;
        }

        _flashPulseTimer.Stop();
        _flashPulseTimer = null;
    }

    private void StopFlashFadeTimer()
    {
        if (_flashFadeTimer is null)
        {
            return;
        }

        _flashFadeTimer.Stop();
        _flashFadeTimer = null;
    }

    public void Dispose()
    {
        _coordinator.PropertyChanged -= OnCoordinatorPropertyChanged;
        _coordinator.LoadingFlash -= OnLoadingFlash;
        _coordinator.LoadingFlashCompleted -= OnLoadingFlashCompleted;
        _displaySettings.SettingsChanged -= OnDisplaySettingsChanged;
        StopFlashPulseTimer();
        StopFlashFadeTimer();
        _interpolationTimer.Stop();
        _onClosed?.Invoke();
    }
}
