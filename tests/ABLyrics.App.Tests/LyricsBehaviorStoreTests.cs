using System.IO;
using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsBehaviorStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LyricsBehaviorStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "lyrics-behavior.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultsClone()
    {
        var defaults = new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false };
        var loaded = new LyricsBehaviorStore(_path).Load(defaults);
        Assert.False(loaded.PromptForLocalLyricsOnMissing);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsCloneNotOriginal()
    {
        var defaults = new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false };
        var loaded = new LyricsBehaviorStore(_path).Load(defaults);
        loaded.PromptForLocalLyricsOnMissing = true;
        Assert.False(defaults.PromptForLocalLyricsOnMissing);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsFalse()
    {
        var store = new LyricsBehaviorStore(_path);
        store.Save(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false });
        var loaded = store.Load(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });
        Assert.False(loaded.PromptForLocalLyricsOnMissing);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaultsClone()
    {
        File.WriteAllText(_path, "{ not json");
        var defaults = new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false };
        var loaded = new LyricsBehaviorStore(_path).Load(defaults);
        Assert.False(loaded.PromptForLocalLyricsOnMissing);
    }
}