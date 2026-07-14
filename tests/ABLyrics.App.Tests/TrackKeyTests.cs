using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using Xunit;

namespace ABLyrics.App.Tests;

public class TrackKeyTests
{
    [Fact]
    public void From_TrimsAndCollapsesWhitespace()
    {
        var key = TrackKey.From("  The   Beatles  ", " Abbey   Road ", " Come   Together ");

        Assert.Equal("the beatles||abbey road||come together", key);
    }

    [Fact]
    public void From_IsCaseInsensitive()
    {
        var lower = TrackKey.From("artist", "album", "song");
        var upper = TrackKey.From("ARTIST", "ALBUM", "SONG");
        var mixed = TrackKey.From("Artist", "Album", "Song");

        Assert.Equal(lower, upper);
        Assert.Equal(lower, mixed);
    }

    [Fact]
    public void From_EmptyAlbum_KeepsSeparator()
    {
        var key = TrackKey.From("Artist", "", "Song");

        // Album 为空时仍保留四段分隔符："artist||||song"
        Assert.Equal("artist||||song", key);
    }

    [Fact]
    public void From_WhitespaceAlbum_KeepsSeparator()
    {
        var key = TrackKey.From("Artist", "   ", "Song");

        // 全空白 album 视为空，仍保留分隔符
        Assert.Equal("artist||||song", key);
    }

    [Fact]
    public void From_SameInputs_ProducesSameKey()
    {
        var a = TrackKey.From("Artist", "Album", "Song");
        var b = TrackKey.From("Artist", "Album", "Song");

        Assert.Equal(a, b);
    }

    [Fact]
    public void From_DifferentInputs_ProducesDifferentKey()
    {
        var a = TrackKey.From("Artist", "Album", "Song");
        var b = TrackKey.From("Artist", "Album", "OtherSong");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void From_TrackInfoDelegatesCorrectly()
    {
        var track = new TrackInfo
        {
            Id = "t1",
            Name = "Come Together",
            Artist = "The Beatles",
            Album = "Abbey Road",
        };

        var key = TrackKey.From(track);

        Assert.Equal("the beatles||abbey road||come together", key);
    }

    [Fact]
    public void From_TrackInfoWithEmptyAlbum_KeepsSeparator()
    {
        var track = new TrackInfo
        {
            Id = "t1",
            Name = "Song",
            Artist = "Artist",
            Album = string.Empty,
        };

        var key = TrackKey.From(track);

        Assert.Equal("artist||||song", key);
    }
}
