using System.IO;
using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackStateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PlaybackStateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "playback-state.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultSpotify()
    {
        var settings = new PlaybackStateStore(_path).Load();
        Assert.Equal("Spotify", settings.ActiveSource);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsActiveSource()
    {
        var store = new PlaybackStateStore(_path);
        store.Save(new PlaybackSettings { ActiveSource = "Spotify" });
        var loaded = store.Load();
        Assert.Equal("Spotify", loaded.ActiveSource);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefault()
    {
        File.WriteAllText(_path, "{ not json");
        var loaded = new PlaybackStateStore(_path).Load();
        Assert.Equal("Spotify", loaded.ActiveSource);
    }
}
