using System.IO;
using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class DisplaySettingsStoreDefaultsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public DisplaySettingsStoreDefaultsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "display-settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultsIncludingSongInfoFontSize()
    {
        var defaults = new DisplayStyleSettings
        {
            FontSize = 18,
            SongInfoFontSize = 12,
        };

        var loaded = DisplaySettingsStore.Load(_path, defaults);

        Assert.Equal(12, loaded.SongInfoFontSize);
    }

    [Fact]
    public void Load_WhenFieldMissingInJson_FallsBackToDefaultSongInfoFontSize()
    {
        // Older config files serialized before SongInfoFontSize existed.
        File.WriteAllText(_path, """
            {
              "FontFamily": "Segoe UI",
              "FontSize": 20
            }
            """);

        var defaults = new DisplayStyleSettings
        {
            FontSize = 18,
            SongInfoFontSize = 12,
        };

        var loaded = DisplaySettingsStore.Load(_path, defaults);

        Assert.Equal("Segoe UI", loaded.FontFamily);
        Assert.Equal(20, loaded.FontSize);
        Assert.Equal(12, loaded.SongInfoFontSize);
    }

    [Fact]
    public void Load_WhenSongInfoFontSizeIsZero_FallsBackToDefault()
    {
        File.WriteAllText(_path, """
            {
              "FontSize": 20,
              "SongInfoFontSize": 0
            }
            """);

        var defaults = new DisplayStyleSettings { SongInfoFontSize = 12 };

        var loaded = DisplaySettingsStore.Load(_path, defaults);

        Assert.Equal(12, loaded.SongInfoFontSize);
    }

    [Fact]
    public void Load_WhenSongInfoFontSizeIsExplicit_KeepsIt()
    {
        File.WriteAllText(_path, """
            {
              "FontSize": 20,
              "SongInfoFontSize": 16
            }
            """);

        var defaults = new DisplayStyleSettings { SongInfoFontSize = 12 };

        var loaded = DisplaySettingsStore.Load(_path, defaults);

        Assert.Equal(16, loaded.SongInfoFontSize);
    }
}
