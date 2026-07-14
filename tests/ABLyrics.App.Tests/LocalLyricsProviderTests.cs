using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using Xunit;

namespace ABLyrics.App.Tests;

public class LocalLyricsProviderTests : IDisposable
{
    private readonly string _dir;

    public LocalLyricsProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private LocalLyricsProvider NewProvider()
    {
        var settings = new AppSettings
        {
            Lyrics = new LyricsSettings { LocalPath = _dir },
        };
        return new LocalLyricsProvider(settings);
    }

    private static TrackInfo Track(string id = "t1") => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
    };

    [Fact]
    public void LibraryPath_UsesConfiguredDirectory()
    {
        var provider = NewProvider();
        Assert.Equal(_dir, provider.LibraryPath);
    }

    [Fact]
    public void GetAsync_ReturnsNullWhenMissing()
    {
        var provider = NewProvider();
        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.Null(result);
    }

    [Fact]
    public void ImportAsync_CopiesFileWithExpectedName()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "[00:01.00] hello");

        provider.ImportAsync(source, Track()).GetAwaiter().GetResult();

        var dest = Path.Combine(_dir, "Artist - Song.lrc");
        Assert.True(File.Exists(dest));
        Assert.Equal("[00:01.00] hello", File.ReadAllText(dest));
    }

    [Fact]
    public void ImportAsync_OverwritesExisting()
    {
        var provider = NewProvider();
        var dest = Path.Combine(_dir, "Artist - Song.lrc");
        File.WriteAllText(dest, "old content");
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "new content");

        provider.ImportAsync(source, Track()).GetAwaiter().GetResult();

        Assert.Equal("new content", File.ReadAllText(dest));
    }

    [Fact]
    public void ImportAsync_SanitizesInvalidChars()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        provider.ImportAsync(source, new TrackInfo
        {
            Id = "t2",
            Name = "Bad/Name:1",
            Artist = "A|B",
        }).GetAwaiter().GetResult();

        var expected = Path.Combine(_dir, "A_B - Bad_Name_1.lrc");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void GetAsync_ReadsContentWhenPresent()
    {
        var provider = NewProvider();
        File.WriteAllText(Path.Combine(_dir, "Artist - Song.lrc"), "lyrics body");

        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("Local", result!.Source);
        Assert.Equal("lyrics body", result.SyncedLyrics);
        Assert.Equal("lyrics body", result.PlainLyrics);
    }
}