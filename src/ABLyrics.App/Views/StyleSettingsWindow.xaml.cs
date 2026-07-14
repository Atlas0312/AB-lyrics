using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ABLyrics.App.Configuration;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Playback;
using Wpf.Ui.Controls;

namespace ABLyrics.App.Views;

public partial class StyleSettingsWindow : FluentWindow
{
    private readonly DisplaySettingsService _displaySettings;
    private readonly LyricsBehaviorService _lyricsBehavior;
    private PlaybackCoordinator _coordinator;
    private PlaybackSourceRegistry? _sourceRegistry;
    private readonly IReadOnlyList<string> _fontChoices;
    private bool _isLoading;

    public StyleSettingsWindow(
        DisplaySettingsService displaySettings,
        PlaybackCoordinator coordinator,
        LyricsBehaviorService lyricsBehavior)
    {
        _displaySettings = displaySettings;
        _coordinator = coordinator;
        _lyricsBehavior = lyricsBehavior;
        _fontChoices = SystemFontCatalog.GetInstalledFontFamilies();

        try
        {
            InitializeComponent();
            FontFamilyCombo.ItemsSource = _fontChoices;
            WireEvents();
            LoadFrom(_displaySettings.Current);
            LoadLyricsBehaviorFrom(_lyricsBehavior.Current);
            _sourceRegistry = App.GetPlaybackSourceRegistry();
            SetCoordinatorReference(_coordinator);
            _coordinator.SourceStateChanged += () => Dispatcher.BeginInvoke(RefreshPlaybackSourcePanel);
            RefreshPlaybackSourcePanel();
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "初始化样式设置窗口失败");
            throw;
        }
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem { TargetPageTag: var tag } || string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (StyleSettingsTabRouter.Resolve(tag) is not { } pageName)
        {
            return;
        }

        AppearancePage.Visibility = Visibility.Collapsed;
        SyncPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
        PlaybackSourcePage.Visibility = Visibility.Collapsed;
        LyricsPage.Visibility = Visibility.Collapsed;

        switch (pageName)
        {
            case nameof(AppearancePage):
                AppearancePage.Visibility = Visibility.Visible;
                RefreshForegroundRecommendations();
                break;
            case nameof(SyncPage): SyncPage.Visibility = Visibility.Visible; break;
            case nameof(AboutPage): AboutPage.Visibility = Visibility.Visible; break;
            case nameof(PlaybackSourcePage):
                PlaybackSourcePage.Visibility = Visibility.Visible;
                RefreshPlaybackSourcePanel();
                break;
            case nameof(LyricsPage): LyricsPage.Visibility = Visibility.Visible; break;
        }
    }

    private void AttachPlaybackSourceRegistry(PlaybackSourceRegistry registry)
    {
        _sourceRegistry = registry;
    }

    private void SetCoordinatorReference(PlaybackCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    private void RefreshPlaybackSourcePanel()
    {
        if (_sourceRegistry is null || _coordinator is null) return;

        var registry = _sourceRegistry;
        var coordinator = _coordinator;

        var active = coordinator.ActivePlaybackSource;
        ActiveSourceNameText.Text = active?.DisplayName ?? "(未选择)";
        ActiveSourceStatusText.Text = active is null
            ? "尚未选择播放来源"
            : active.IsConnected ? "已连接" : (active.IsAvailable ? "未连接" : "不可用：缺少配置");

        ConnectSourceButton.IsEnabled = active is { IsAvailable: true, IsConnected: false };
        DisconnectSourceButton.IsEnabled = active is { IsConnected: true };

        SpotifyDetailCard.Visibility = active is SpotifyPlaybackSource ? Visibility.Visible : Visibility.Collapsed;
        if (active is SpotifyPlaybackSource spotify)
        {
            SpotifyStatusDetailText.Text = spotify.IsConnected
                ? "Spotify 已连接，可开始播放。"
                : (spotify.IsAvailable ? "Spotify 未登录。" : "Spotify 不可用：未配置 ClientId。");
            SpotifyUnavailableHint.Visibility = spotify.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        }

        var items = registry.All.Select(s => new
        {
            s.Id,
            s.DisplayName,
            StatusText = !s.IsAvailable ? "不可用：缺少配置"
                : s.IsConnected ? "已连接"
                : (s.Id == coordinator.ActivePlaybackSource?.Id ? "当前未连接" : "未连接"),
        }).ToList();
        AvailableSourcesList.ItemsSource = items;
    }

    private async void OnConnectSourceClick(object sender, RoutedEventArgs e)
    {
        if (_coordinator?.ActivePlaybackSource is null) return;
        try
        {
            await _coordinator.ActivePlaybackSource.ConnectAsync();
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "连接播放来源失败");
        }
        finally
        {
            RefreshPlaybackSourcePanel();
        }
    }

    private void OnDisconnectSourceClick(object sender, RoutedEventArgs e)
    {
        if (_coordinator?.ActivePlaybackSource is null) return;
        try
        {
            _coordinator.ActivePlaybackSource.Disconnect();
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "断开播放来源失败");
        }
        finally
        {
            RefreshPlaybackSourcePanel();
        }
    }

    private async void OnUseSourceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { Tag: string id }) return;
        if (_coordinator is null) return;
        try
        {
            await _coordinator.SetActiveSourceAsync(id);
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "切换播放来源失败");
        }
        finally
        {
            RefreshPlaybackSourcePanel();
        }
    }

    private void OnFontComboPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false } combo)
        {
            combo.Focus();
            combo.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void OnFontComboPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        if (!FontFamilyCombo.IsDropDownOpen)
        {
            FontFamilyCombo.IsDropDownOpen = true;
            e.Handled = true;
        }

        Dispatcher.BeginInvoke(ApplyPreviewIfReady, DispatcherPriority.Background);
    }

    private void WireEvents()
    {
        FontFamilyCombo.SelectionChanged += (_, _) => ApplyPreviewIfReady();
        FontFamilyCombo.DropDownClosed += (_, _) => ApplySettingsIfReady();

        FontSizeSlider.ValueChanged += OnSliderValueChanged;
        FontSizeSlider.LostFocus += OnControlLostFocus;
        SongInfoFontSizeSlider.ValueChanged += OnSliderValueChanged;
        SongInfoFontSizeSlider.LostFocus += OnControlLostFocus;
        LetterSpacingSlider.ValueChanged += OnSliderValueChanged;
        LetterSpacingSlider.LostFocus += OnControlLostFocus;
        BarHeightSlider.ValueChanged += OnSliderValueChanged;
        BarHeightSlider.LostFocus += OnControlLostFocus;
        OpacitySlider.ValueChanged += OnSliderValueChanged;
        OpacitySlider.LostFocus += OnControlLostFocus;
        SyncOffsetSlider.ValueChanged += OnSyncOffsetChanged;
        SyncOffsetSlider.LostFocus += OnControlLostFocus;

        BackgroundColorBox.TextChanged += (_, _) => { ApplyPreviewIfReady(); RefreshForegroundRecommendations(); };
        BackgroundColorBox.LostFocus += OnControlLostFocus;
        OverlayBaseColorBox.TextChanged += (_, _) => RefreshForegroundRecommendations();
        OverlayBaseColorBox.LostFocus += OnControlLostFocus;
        OpacitySlider.ValueChanged += (_, _) => RefreshForegroundRecommendations();
        PaddingLeftBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        PaddingLeftBox.LostFocus += OnControlLostFocus;
        PaddingTopBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        PaddingTopBox.LostFocus += OnControlLostFocus;
        PaddingRightBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        PaddingRightBox.LostFocus += OnControlLostFocus;
        PaddingBottomBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        PaddingBottomBox.LostFocus += OnControlLostFocus;

        OneLineRadio.Checked += (_, _) => ApplyPreviewIfReady();
        OneLineRadio.LostFocus += OnControlLostFocus;
        TwoLineRadio.Checked += (_, _) => ApplyPreviewIfReady();
        TwoLineRadio.LostFocus += OnControlLostFocus;

        ForegroundColorBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        ForegroundColorBox.LostFocus += OnControlLostFocus;
        ForegroundOpacitySlider.ValueChanged += (_, _) => ApplyPreviewIfReady();
        ForegroundOpacitySlider.LostFocus += OnControlLostFocus;
    }

    private void LoadFrom(DisplayStyleSettings style)
    {
        _isLoading = true;
        try
        {
            SelectFontFamily(style.FontFamily);
            FontSizeSlider.Value = Clamp(style.FontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
            SongInfoFontSizeSlider.Value = Clamp(style.SongInfoFontSize, SongInfoFontSizeSlider.Minimum, SongInfoFontSizeSlider.Maximum);
            LetterSpacingSlider.Value = Clamp(style.LetterSpacing, LetterSpacingSlider.Minimum, LetterSpacingSlider.Maximum);
            BarHeightSlider.Value = Clamp(style.BarHeight, BarHeightSlider.Minimum, BarHeightSlider.Maximum);
            BackgroundColorBox.Text = style.BackgroundColor;
            OpacitySlider.Value = Clamp(style.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
            SyncOffsetSlider.Value = Clamp(style.SyncOffsetMs, SyncOffsetSlider.Minimum, SyncOffsetSlider.Maximum);
            PaddingLeftBox.Text = style.PaddingLeft.ToString(CultureInfo.InvariantCulture);
            PaddingTopBox.Text = style.PaddingTop.ToString(CultureInfo.InvariantCulture);
            PaddingRightBox.Text = style.PaddingRight.ToString(CultureInfo.InvariantCulture);
            PaddingBottomBox.Text = style.PaddingBottom.ToString(CultureInfo.InvariantCulture);
OneLineRadio.IsChecked = style.LineCount <= 1;
            TwoLineRadio.IsChecked = style.LineCount >= 2;
            ForegroundColorBox.Text = style.ForegroundColor;
            ForegroundOpacitySlider.Value = Clamp(style.ForegroundOpacity, ForegroundOpacitySlider.Minimum, ForegroundOpacitySlider.Maximum);
            OverlayBaseColorBox.Text = style.OverlayBaseColor;
            UpdateLabels();
            ApplyPreview();
            RefreshForegroundRecommendations();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SelectFontFamily(string fontFamily)
    {
        var match = _fontChoices.FirstOrDefault(
            name => string.Equals(name, fontFamily, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            FontFamilyCombo.SelectedItem = match;
            FontFamilyCombo.Text = match;
            return;
        }

        FontFamilyCombo.SelectedItem = null;
        FontFamilyCombo.Text = string.IsNullOrWhiteSpace(fontFamily)
            ? _fontChoices.FirstOrDefault() ?? "Microsoft YaHei UI"
            : fontFamily;
    }

    private DisplayStyleSettings ReadFromForm()
    {
        return new DisplayStyleSettings
        {
            FontFamily = ReadFontFamily(),
            FontSize = FontSizeSlider.Value,
            SongInfoFontSize = SongInfoFontSizeSlider.Value,
            LetterSpacing = LetterSpacingSlider.Value,
            BarHeight = (int)BarHeightSlider.Value,
            BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColorBox.Text)
                ? "#101010"
                : BackgroundColorBox.Text.Trim(),
            BackgroundOpacity = OpacitySlider.Value,
            PaddingLeft = ParseDouble(PaddingLeftBox.Text, 16),
            PaddingTop = ParseDouble(PaddingTopBox.Text, 4),
            PaddingRight = ParseDouble(PaddingRightBox.Text, 16),
            PaddingBottom = ParseDouble(PaddingBottomBox.Text, 4),
            LineCount = TwoLineRadio.IsChecked == true ? 2 : 1,
            SyncOffsetMs = (int)SyncOffsetSlider.Value,
            ForegroundColor = string.IsNullOrWhiteSpace(ForegroundColorBox.Text) ? "#FFFFFF" : ForegroundColorBox.Text.Trim(),
            ForegroundOpacity = ForegroundOpacitySlider.Value,
            OverlayBaseColor = string.IsNullOrWhiteSpace(OverlayBaseColorBox.Text) ? "#000000" : OverlayBaseColorBox.Text.Trim(),
        };
    }

    private string ReadFontFamily()
    {
        if (FontFamilyCombo.SelectedItem is string selected)
        {
            return selected;
        }

        var text = FontFamilyCombo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return "Microsoft YaHei UI";
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }

        UpdateLabels();
        ApplyPreview();
    }

    private void ApplyPreviewIfReady()
    {
        if (_isLoading)
        {
            return;
        }

        ApplyPreview();
    }

    private void UpdateLabels()
    {
        FontSizeLabel.Text = $"{FontSizeSlider.Value:0}";
        SongInfoFontSizeLabel.Text = $"{SongInfoFontSizeSlider.Value:0}";
        LetterSpacingLabel.Text = $"{LetterSpacingSlider.Value:0.#}";
        BarHeightLabel.Text = $"{BarHeightSlider.Value:0}";
        OpacityLabel.Text = $"{OpacitySlider.Value:P0}";
        ForegroundOpacityLabel.Text = $"{ForegroundOpacitySlider.Value:P0}";
        SyncOffsetLabel.Text = $"{SyncOffsetSlider.Value:0}";
    }

    private void ApplyPreview()
    {
        var style = ReadFromForm();
        if (FontFamilyCombo.IsDropDownOpen)
        {
            var browsingFont = FontFamilyCombo.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(browsingFont))
            {
                style.FontFamily = browsingFont;
            }
        }

        LyricsStyleApplier.ApplyPreview(PreviewBorder, PreviewPrimary, PreviewSecondary, PreviewEnglish, style);
        PreviewSecondary.Text = style.LineCount >= 2 ? "上一行歌词预览" : string.Empty;
        PreviewPrimary.Text = "当前行歌词预览";
    }

    private void OnControlLostFocus(object sender, RoutedEventArgs e)
    {
        ApplySettingsIfReady();
    }

    private void ApplySettingsIfReady()
    {
        if (_isLoading)
        {
            return;
        }

        ApplySettings();
    }

    private void ApplySettings()
    {
        try
        {
            _displaySettings.Update(ReadFromForm());
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "应用样式设置失败");
        }
    }

    private void OnSyncOffsetChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        var offset = (int)SyncOffsetSlider.Value;
        _coordinator.SyncOffsetMs = offset;
        SyncOffsetLabel.Text = $"{offset}";
    }

    private void LoadLyricsBehaviorFrom(LyricsBehaviorSettings settings)
    {
        _isLoading = true;
        try
        {
            PromptLocalMissingSwitch.IsChecked = settings.PromptForLocalLyricsOnMissing;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnPromptLocalMissingToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        try
        {
            _lyricsBehavior.Update(new LyricsBehaviorSettings
            {
                PromptForLocalLyricsOnMissing = PromptLocalMissingSwitch.IsChecked == true,
            });
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "保存歌词行为失败");
        }
    }

    private void OnRecommendSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { Tag: string tag }) return;
        var color = tag switch
        {
            "white" => "#FFFFFF",
            "black" => "#000000",
            _ => null,
        };
        if (color is null) return;
        ForegroundColorBox.Text = color;
        ApplyPreviewIfReady();
    }

    private void RefreshForegroundRecommendations()
    {
        if (_isLoading) return;
        var bg = BackgroundColorBox.Text;
        var overlay = string.IsNullOrWhiteSpace(OverlayBaseColorBox.Text) ? "#000000" : OverlayBaseColorBox.Text.Trim();
        var opacity = OpacitySlider.Value;
        var rec = ColorContrastHelper.RecommendForeground(bg, opacity, overlay);
        HighlightSwatch(RecommendForegroundWhiteButton, rec == "#FFFFFF");
        HighlightSwatch(RecommendForegroundBlackButton, rec == "#000000");
    }

    private static void HighlightSwatch(Wpf.Ui.Controls.Button button, bool highlight)
    {
        if (button?.Content is Border border)
        {
            border.BorderThickness = highlight ? new Thickness(3) : new Thickness(1);
            border.BorderBrush = highlight ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.Gray;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
