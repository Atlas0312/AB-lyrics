using ABLyrics.App.Services;
using Xunit;

namespace ABLyrics.App.Tests;

public class ColorContrastHelperTests
{
    [Fact]
    public void WhiteBackground_PicksBlackForeground()
    {
        var fg = ColorContrastHelper.RecommendForeground("#FFFFFF", 1.0, "#000000");
        Assert.Equal("#000000", fg);
    }

    [Fact]
    public void BlackBackground_PicksWhiteForeground()
    {
        var fg = ColorContrastHelper.RecommendForeground("#000000", 1.0, "#000000");
        Assert.Equal("#FFFFFF", fg);
    }

    [Fact]
    public void MidGrayOpaque_PrefersBlackOverWhite()
    {
        // #808080: 黑色对比度 ≈ 5.32, 白色对比度 ≈ 3.95 → 选黑
        var fg = ColorContrastHelper.RecommendForeground("#808080", 1.0, "#000000");
        Assert.Equal("#000000", fg);
    }

    [Fact]
    public void BrightYellowWithTransparency_PicksBlackAfterOverlayBlend()
    {
        // #FFFF00 × 50% on #000000 → effective ≈ #808000
        // 黑对比度 ≈ 5.01 ≥ 4.5, 白对比度 ≈ 4.19 < 4.5 → 选黑
        var fg = ColorContrastHelper.RecommendForeground("#FFFF00", 0.5, "#000000");
        Assert.Equal("#000000", fg);
    }

    [Fact]
    public void TransparentBackground_OverlayColorInfluencesResult()
    {
        // 完全透明 + 白色 overlay → effective 白底 → 选黑
        var withWhiteOverlay = ColorContrastHelper.RecommendForeground("#000000", 0.0, "#FFFFFF");
        Assert.Equal("#000000", withWhiteOverlay);

        // 完全不透明 + 黑底 → effective 黑底 → 选白
        var opaqueBlack = ColorContrastHelper.RecommendForeground("#000000", 1.0, "#FFFFFF");
        Assert.Equal("#FFFFFF", opaqueBlack);
    }

    [Fact]
    public void InvalidHex_FallsBackToBlack()
    {
        var fg = ColorContrastHelper.RecommendForeground("not-a-color", 1.0, "#000000");
        Assert.Equal("#000000", fg);
    }
}
