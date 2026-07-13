using SpotLyrics.App.Views;
using Xunit;

namespace SpotLyrics.App.Tests;

public class StyleSettingsTabRouterTests
{
    [Theory]
    [InlineData(StyleSettingsTabRouter.AppearanceTag, "AppearancePage")]
    [InlineData(StyleSettingsTabRouter.LayoutTag, "LayoutPage")]
    [InlineData(StyleSettingsTabRouter.ColorTag, "ColorPage")]
    [InlineData(StyleSettingsTabRouter.SyncTag, "SyncPage")]
    [InlineData(StyleSettingsTabRouter.AboutTag, "AboutPage")]
    public void Resolve_KnownTag_ReturnsMatchingPageName(string tag, string expectedPage)
    {
        Assert.Equal(expectedPage, StyleSettingsTabRouter.Resolve(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Appearance")]      // wrong case
    [InlineData("APPEARANCE")]
    [InlineData("settings")]
    [InlineData("foo bar")]
    public void Resolve_UnknownOrEmptyTag_ReturnsNull(string? tag)
    {
        Assert.Null(StyleSettingsTabRouter.Resolve(tag));
    }

    [Fact]
    public void Resolve_DoesNotMutateInput()
    {
        const string original = StyleSettingsTabRouter.AppearanceTag;
        var copy = new string(original);
        StyleSettingsTabRouter.Resolve(copy);
        Assert.Equal(original, copy);
    }

    [Fact]
    public void KnownTags_ContainsExactlyFiveTags()
    {
        Assert.Equal(5, StyleSettingsTabRouter.KnownTags.Count);
        Assert.Contains(StyleSettingsTabRouter.AppearanceTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.LayoutTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.ColorTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.SyncTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.AboutTag, StyleSettingsTabRouter.KnownTags);
    }
}
