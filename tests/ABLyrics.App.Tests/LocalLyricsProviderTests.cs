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

    private static TrackInfo Track(string id = "t1", string album = "Album") => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
        Album = album,
    };

    private static TrackInfo TrackNoAlbum(string id = "t1") => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
        Album = string.Empty,
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
        var result = provider.GetAsync(TrackNoAlbum()).GetAwaiter().GetResult();
        Assert.Null(result);
    }

    [Fact]
    public void ImportAsync_CopiesFileWithExpectedName()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "[00:01.00] hello");

        provider.ImportAsync(source, TrackNoAlbum()).GetAwaiter().GetResult();

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

        provider.ImportAsync(source, TrackNoAlbum()).GetAwaiter().GetResult();

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

        var result = provider.GetAsync(TrackNoAlbum()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("Local", result!.Source);
        Assert.Equal("lyrics body", result.SyncedLyrics);
        Assert.Equal("lyrics body", result.PlainLyrics);
    }

    [Fact]
    public void GetAsync_WithAlbumPresent_FindsAlbumNamedFile()
    {
        var provider = NewProvider();
        var expected = Path.Combine(_dir, "Artist - Album - Song.lrc");
        File.WriteAllText(expected, "album body");

        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("album body", result!.SyncedLyrics);
    }

    [Fact]
    public void GetAsync_WithAlbumPresent_FallsBackToLegacyFileWhenNewMissing()
    {
        var provider = NewProvider();
        var legacy = Path.Combine(_dir, "Artist - Song.lrc");
        File.WriteAllText(legacy, "legacy body");

        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("legacy body", result!.SyncedLyrics);
    }

    [Fact]
    public void GetAsync_WithAlbumPresent_NewWinsOverLegacyWhenBothPresent()
    {
        var provider = NewProvider();
        var album = Path.Combine(_dir, "Artist - Album - Song.lrc");
        var legacy = Path.Combine(_dir, "Artist - Song.lrc");
        File.WriteAllText(album, "album body");
        File.WriteAllText(legacy, "legacy body");

        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("album body", result!.SyncedLyrics);
    }

    [Fact]
    public void GetAsync_AlbumEmpty_FindsLegacyFile()
    {
        var provider = NewProvider();
        var legacy = Path.Combine(_dir, "Artist - Song.lrc");
        File.WriteAllText(legacy, "legacy body");

        var result = provider.GetAsync(TrackNoAlbum()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("legacy body", result!.SyncedLyrics);
    }

    [Fact]
    public void GetAsync_AlbumEmpty_DoesNotMatchAlbumSuffixedFile()
    {
        var provider = NewProvider();
        var album = Path.Combine(_dir, "Artist - Album - Song.lrc");
        File.WriteAllText(album, "album body");

        var result = provider.GetAsync(TrackNoAlbum()).GetAwaiter().GetResult();
        Assert.Null(result);
    }

    [Fact]
    public void ImportAsync_WithAlbum_WritesAlbumNamedFile()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        provider.ImportAsync(source, Track()).GetAwaiter().GetResult();

        var dest = Path.Combine(_dir, "Artist - Album - Song.lrc");
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void ImportAsync_AlbumEmpty_WritesLegacyFileName()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        provider.ImportAsync(source, TrackNoAlbum()).GetAwaiter().GetResult();

        var dest = Path.Combine(_dir, "Artist - Song.lrc");
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void ImportAsync_VeryLongAlbum_TruncatesSafely()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        provider.ImportAsync(source, Track(album: new string('a', 500))).GetAwaiter().GetResult();

        // 不抛异常 + 至少有一个非 source 的 .lrc 文件写入 + 文件名长度受控
        var written = Directory.EnumerateFiles(_dir, "*.lrc")
            .Where(f => !string.Equals(Path.GetFileName(f), "source.lrc", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(written);
        var fileName = Path.GetFileName(written[0]);
        Assert.True(fileName.Length <= 200, $"文件名长度 {fileName.Length} 超过 200");
        Assert.EndsWith(".lrc", fileName);
        Assert.EndsWith("Song.lrc", fileName);
    }

    [Fact]
    public void ImportAsync_LongTotal_TruncatesAlbumNotName()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        // 现实场景：name 在 100 字符内是上限，album 才是容易过长的段
        var longName = new string('n', 80);
        var longAlbum = new string('a', 200);
        var track = new TrackInfo
        {
            Id = "t3",
            Name = longName,
            Artist = "Artist",
            Album = longAlbum,
        };
        provider.ImportAsync(source, track).GetAwaiter().GetResult();

        var written = Directory.EnumerateFiles(_dir, "*.lrc")
            .Where(f => !string.Equals(Path.GetFileName(f), "source.lrc", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(written);
        var fileName = Path.GetFileName(written[0]);
        Assert.True(fileName.Length <= 200, $"文件名长度 {fileName.Length} 超过 200");
        // Name 完整保留在文件名末尾
        Assert.EndsWith(longName + ".lrc", fileName);
        // Album 被截断
        Assert.DoesNotContain(longAlbum, fileName);
    }
}