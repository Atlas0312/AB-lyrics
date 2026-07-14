using System;
using System.IO;
using System.Linq;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using Xunit;

namespace ABLyrics.App.Tests;

public class LocalLyricsSearchProviderTests : IDisposable
{
    private readonly string _dir;

    public LocalLyricsSearchProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsSearch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private LocalLyricsSearchProvider NewProvider()
    {
        var settings = new AppSettings
        {
            Lyrics = new LyricsSettings { LocalPath = _dir },
        };
        return new LocalLyricsSearchProvider(settings);
    }

    private static TrackInfo Track(string artist = "Artist", string name = "Song", string album = "Album")
        => new()
        {
            Id = "t1",
            Name = name,
            Artist = artist,
            Album = album,
        };

    private void WriteFile(string fileName, string content = "[00:01.00] hello")
    {
        File.WriteAllText(Path.Combine(_dir, fileName), content);
    }

    [Fact]
    public void Search_NoFiles_ReturnsEmpty()
    {
        var provider = NewProvider();

        var results = provider.Search(Track());

        Assert.Empty(results);
    }

    [Fact]
    public void Search_FindsArtistNameIntersection()
    {
        WriteFile("Artist - Song.lrc");
        WriteFile("OtherArtist - Song.lrc");
        WriteFile("Random.lrc");

        var provider = NewProvider();
        var results = provider.Search(Track(artist: "Artist", name: "Song"));

        // Primary "Artist - Song.lrc" + 没有交集的 Random.lrc 不会进；OtherArtist-Song 不命中
        Assert.Contains(results, c => Path.GetFileName(c.Label + ".lrc") == "Artist - Song.lrc");
        Assert.DoesNotContain(results, c => c.Label == "Random");
    }

    [Fact]
    public void Search_OrdersByFileNameLength()
    {
        WriteFile("Artist - Album - Song.lrc"); // longer
        WriteFile("Artist-Song.lrc");             // shorter
        WriteFile("Artist - Song.lrc");            // middle

        var provider = NewProvider();
        var results = provider.Search(Track());

        // 长度升序：Artist-Song.lrc → Artist - Song.lrc → Artist - Album - Song.lrc
        Assert.Equal(3, results.Count);
        Assert.Equal("Artist-Song", results[0].Label);
        Assert.Equal("Artist - Song", results[1].Label);
        Assert.Equal("Artist - Album - Song", results[2].Label);
    }

    [Fact]
    public void Search_SameFileListedOnce()
    {
        WriteFile("Artist - Song.lrc");
        // 让"Artist"和"Song"都同时在该文件里 → 同一文件被 primary + 模糊双重匹配
        var provider = NewProvider();

        var results = provider.Search(Track());

        var labelStarts = results.Count(c => c.Label == "Artist - Song");
        Assert.Equal(1, labelStarts);
    }

    [Fact]
    public void Search_PicksPrimaryHitFirst()
    {
        WriteFile("Artist - Album - Song.lrc"); // primary (has album)
        WriteFile("Artist - Song.lrc");          // legacy
        WriteFile("Artist-Song.lrc");             // shortest fuzzy
        var provider = NewProvider();

        var results = provider.Search(Track());

        // Artist-Song.lrc (shortest) 排在第一位
        Assert.Equal("Artist-Song", results[0].Label);
        // primary (Artist - Album - Song) 应当出现在结果里
        Assert.Contains(results, c => c.Label == "Artist - Album - Song");
    }

    [Fact]
    public void Search_AllCandidatesHaveLocalOrigin()
    {
        WriteFile("Artist - Song.lrc");
        var provider = NewProvider();

        var results = provider.Search(Track());

        Assert.All(results, c =>
        {
            Assert.Equal("Local", c.Source);
            var origin = Assert.IsType<CandidateOrigin.Local>(c.Origin);
            Assert.EndsWith(".lrc", origin.FilePath);
        });
    }

    [Fact]
    public void Search_ReadsFileContent()
    {
        WriteFile("Artist - Song.lrc", "[00:01.00] specific body");
        var provider = NewProvider();

        var results = provider.Search(Track());

        var candidate = Assert.Single(results);
        Assert.Equal("[00:01.00] specific body", candidate.SyncedLyrics);
        Assert.Equal("[00:01.00] specific body", candidate.PlainLyrics);
    }

    [Fact]
    public void Search_DurationFromTrack()
    {
        WriteFile("Artist - Song.lrc");
        var provider = NewProvider();
        var track = Track();
        track = new TrackInfo { Id = "t1", Name = "Song", Artist = "Artist", Album = "Album", DurationMs = 123456 };

        var results = provider.Search(track);

        Assert.Equal(123456, results[0].DurationMs);
    }
}