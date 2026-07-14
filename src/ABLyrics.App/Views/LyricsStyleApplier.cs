using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ABLyrics.App.Configuration;

namespace ABLyrics.App.Views;

internal static class LyricsStyleApplier
{
    private const double PrimaryLineAlpha = 1.0;

    public static void ApplyAppBar(
        Window window,
        Border chrome,
        TextBlock trackTitle,
        TextBlock artistName,
        TextBlock primaryLine,
        TextBlock secondaryLine,
        TextBlock sourceTag,
        DisplayStyleSettings style)
    {
        var fontFamily = CreateFontFamily(style.FontFamily);
        var background = CreateBackgroundBrush(style);

        window.Height = style.BarHeight;
        chrome.Background = background;
        chrome.Padding = new Thickness(
            style.PaddingLeft,
            style.PaddingTop,
            style.PaddingRight,
            style.PaddingBottom);

        var fg = style.ForegroundColor;
        var secondaryAlpha = style.ForegroundOpacity;

        ApplyTextStyle(trackTitle, fontFamily, style.SongInfoFontSize, FontWeights.SemiBold, fg, PrimaryLineAlpha, style.LetterSpacing);
        ApplyTextStyle(artistName, fontFamily, style.SongInfoFontSize, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
        ApplyTextStyle(primaryLine, fontFamily, style.FontSize, FontWeights.Normal, fg, PrimaryLineAlpha, style.LetterSpacing);
        ApplyTextStyle(secondaryLine, fontFamily, style.FontSize * 0.82, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
        ApplyTextStyle(sourceTag, fontFamily, style.FontSize * 0.72, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);

        secondaryLine.Visibility = style.LineCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    public static void ApplyPreview(
        Border chrome,
        TextBlock primaryLine,
        TextBlock secondaryLine,
        TextBlock? englishLine,
        DisplayStyleSettings style)
    {
        var fontFamily = CreateFontFamily(style.FontFamily);
        chrome.Background = CreateBackgroundBrush(style);
        chrome.Padding = new Thickness(
            style.PaddingLeft,
            style.PaddingTop,
            style.PaddingRight,
            style.PaddingBottom);

        var fg = style.ForegroundColor;
        var secondaryAlpha = style.ForegroundOpacity;

        ApplyTextStyle(primaryLine, fontFamily, style.FontSize, FontWeights.Normal, fg, PrimaryLineAlpha, style.LetterSpacing);
        ApplyTextStyle(secondaryLine, fontFamily, style.FontSize * 0.82, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
        secondaryLine.Visibility = style.LineCount >= 2 ? Visibility.Visible : Visibility.Collapsed;

        if (englishLine is not null)
        {
            ApplyTextStyle(englishLine, fontFamily, style.FontSize * 0.82, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
            englishLine.Visibility = Visibility.Visible;
        }
    }

    public static void ApplyOverlay(
        Window window,
        Border chrome,
        TextBlock trackTitle,
        TextBlock artistName,
        TextBlock primaryLine,
        TextBlock secondaryLine,
        DisplayStyleSettings style)
    {
        var fontFamily = CreateFontFamily(style.FontFamily);
        var background = CreateBackgroundBrush(style);

        chrome.Background = background;
        chrome.Padding = new Thickness(
            style.PaddingLeft,
            style.PaddingTop,
            style.PaddingRight,
            style.PaddingBottom);

        var overlayHeight = style.BarHeight + (style.LineCount >= 2 ? style.FontSize * 1.4 : 0) + 28;
        window.MinHeight = overlayHeight;
        window.Height = overlayHeight;

        var fg = style.ForegroundColor;
        var secondaryAlpha = style.ForegroundOpacity;

        ApplyTextStyle(trackTitle, fontFamily, style.SongInfoFontSize, FontWeights.SemiBold, fg, PrimaryLineAlpha, style.LetterSpacing);
        ApplyTextStyle(artistName, fontFamily, style.SongInfoFontSize, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
        ApplyTextStyle(primaryLine, fontFamily, style.FontSize, FontWeights.Normal, fg, PrimaryLineAlpha, style.LetterSpacing);
        ApplyTextStyle(secondaryLine, fontFamily, style.FontSize * 0.82, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);

        secondaryLine.Visibility = style.LineCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ApplyTextStyle(
        TextBlock textBlock,
        FontFamily fontFamily,
        double fontSize,
        FontWeight fontWeight,
        string colorHex,
        double alphaMultiplier,
        double letterSpacing)
    {
        var resolvedSize = Math.Max(8, fontSize);
        textBlock.FontFamily = fontFamily;
        textBlock.FontSize = resolvedSize;
        textBlock.FontWeight = fontWeight;
        textBlock.Foreground = CreateForegroundBrush(colorHex, alphaMultiplier);
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.ClearType);
        LetterSpacingHelper.SetLetterSpacing(textBlock, letterSpacing);
    }

    private static FontFamily CreateFontFamily(string familyName)
    {
        try
        {
            return new FontFamily(familyName);
        }
        catch
        {
            return new FontFamily("Microsoft YaHei UI");
        }
    }

    private static Brush CreateBackgroundBrush(DisplayStyleSettings style)
    {
        var color = ParseColor(style.BackgroundColor);
        var alpha = (byte)Math.Clamp((int)Math.Round(style.BackgroundOpacity * 255), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    internal static Brush CreateForegroundBrush(string hex, double alphaMultiplier = 1.0)
    {
        var color = ParseColor(hex);
        var alpha = (byte)Math.Clamp((int)Math.Round(alphaMultiplier * 255), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public static void ApplyFlash(Border chrome, double opacity)
    {
        chrome.BorderThickness = new Thickness(1);
        var alpha = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        chrome.BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 0x7A, 0xD7, 0xFF));
    }

    public static void ClearFlash(Border chrome)
    {
        chrome.BorderBrush = Brushes.Transparent;
        chrome.BorderThickness = new Thickness(0);
    }

    private static (byte R, byte G, byte B) ParseColor(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 6 &&
            byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return (r, g, b);
        }

        return (0x10, 0x10, 0x10);
    }
}
