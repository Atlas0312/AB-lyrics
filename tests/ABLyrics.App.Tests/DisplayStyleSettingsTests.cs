using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class DisplayStyleSettingsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var settings = new DisplayStyleSettings();

        Assert.Equal("#FFFFFF", settings.ForegroundColor);
        Assert.Equal(0.78, settings.ForegroundOpacity);
        Assert.Equal("#000000", settings.OverlayBaseColor);
    }

    [Fact]
    public void Defaults_DoNotContainLegacyFiveColorFields()
    {
        var settings = new DisplayStyleSettings();
        var type = typeof(DisplayStyleSettings);

        Assert.Null(type.GetProperty("TrackTitleColor"));
        Assert.Null(type.GetProperty("ArtistNameColor"));
        Assert.Null(type.GetProperty("PrimaryLineColor"));
        Assert.Null(type.GetProperty("SecondaryLineColor"));
        Assert.Null(type.GetProperty("SourceTagColor"));
    }

    [Fact]
    public void Clone_CopiesNewFields()
    {
        var original = new DisplayStyleSettings
        {
            ForegroundColor = "#FF8800",
            ForegroundOpacity = 0.5,
            OverlayBaseColor = "#222222",
        };

        var clone = original.Clone();

        Assert.Equal("#FF8800", clone.ForegroundColor);
        Assert.Equal(0.5, clone.ForegroundOpacity);
        Assert.Equal("#222222", clone.OverlayBaseColor);
        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectOriginal()
    {
        var original = new DisplayStyleSettings();
        var clone = original.Clone();

        clone.ForegroundColor = "#FF0000";
        clone.ForegroundOpacity = 0.3;

        Assert.Equal("#FFFFFF", original.ForegroundColor);
        Assert.Equal(0.78, original.ForegroundOpacity);
    }
}
