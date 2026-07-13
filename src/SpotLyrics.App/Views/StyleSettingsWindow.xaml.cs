using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SpotLyrics.App.Configuration;
using SpotLyrics.App.Services;
using Wpf.Ui.Controls;

namespace SpotLyrics.App.Views;

public partial class StyleSettingsWindow : FluentWindow
{
    private readonly DisplaySettingsService _displaySettings;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IReadOnlyList<string> _fontChoices;
    private bool _isLoading;

    public StyleSettingsWindow(DisplaySettingsService displaySettings, PlaybackCoordinator coordinator)
    {
        _displaySettings = displaySettings;
        _coordinator = coordinator;
        _fontChoices = SystemFontCatalog.GetInstalledFontFamilies();

        try
        {
            InitializeComponent();
            FontFamilyCombo.ItemsSource = _fontChoices;
            WireEvents();
            LoadFrom(_displaySettings.Current);
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
        LayoutPage.Visibility = Visibility.Collapsed;
        ColorPage.Visibility = Visibility.Collapsed;
        SyncPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;

        switch (pageName)
        {
            case nameof(AppearancePage): AppearancePage.Visibility = Visibility.Visible; break;
            case nameof(LayoutPage): LayoutPage.Visibility = Visibility.Visible; break;
            case nameof(ColorPage): ColorPage.Visibility = Visibility.Visible; break;
            case nameof(SyncPage): SyncPage.Visibility = Visibility.Visible; break;
            case nameof(AboutPage): AboutPage.Visibility = Visibility.Visible; break;
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
        LetterSpacingSlider.ValueChanged += OnSliderValueChanged;
        LetterSpacingSlider.LostFocus += OnControlLostFocus;
        BarHeightSlider.ValueChanged += OnSliderValueChanged;
        BarHeightSlider.LostFocus += OnControlLostFocus;
        OpacitySlider.ValueChanged += OnSliderValueChanged;
        OpacitySlider.LostFocus += OnControlLostFocus;
        SyncOffsetSlider.ValueChanged += OnSyncOffsetChanged;
        SyncOffsetSlider.LostFocus += OnControlLostFocus;

        BackgroundColorBox.TextChanged += (_, _) => ApplyPreviewIfReady();
        BackgroundColorBox.LostFocus += OnControlLostFocus;
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
    }

    private void LoadFrom(DisplayStyleSettings style)
    {
        _isLoading = true;
        try
        {
            SelectFontFamily(style.FontFamily);
            FontSizeSlider.Value = Clamp(style.FontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
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
            UpdateLabels();
            ApplyPreview();
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
        LetterSpacingLabel.Text = $"{LetterSpacingSlider.Value:0.#}";
        BarHeightLabel.Text = $"{BarHeightSlider.Value:0}";
        OpacityLabel.Text = $"{OpacitySlider.Value:P0}";
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

        LyricsStyleApplier.ApplyPreview(PreviewBorder, PreviewPrimary, PreviewSecondary, style);
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
