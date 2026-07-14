using System.Windows.Media;
using ABLyrics.App.Views;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsStyleApplierForegroundTests
{
    [Fact]
    public void CreateForegroundBrush_HexWithoutHash_ReturnsExpectedColor()
    {
        var brush = LyricsStyleApplier.CreateForegroundBrush("FF0000");

        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void CreateForegroundBrush_HexWithHash_ReturnsExpectedColor()
    {
        var brush = LyricsStyleApplier.CreateForegroundBrush("#3344FF");

        Assert.Equal(Color.FromRgb(0x33, 0x44, 0xFF), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void CreateForegroundBrush_WithAlpha_PreservesAlpha()
    {
        var brush = LyricsStyleApplier.CreateForegroundBrush("#FFFFFF", 0.78);

        Assert.Equal(Color.FromArgb(0xC7, 0xFF, 0xFF, 0xFF), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void CreateForegroundBrush_FullOpacity_AlphaIs255()
    {
        var brush = LyricsStyleApplier.CreateForegroundBrush("#FFFFFF", 1.0);

        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), ((SolidColorBrush)brush).Color);
    }
}
