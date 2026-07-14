using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsOverrideStoreTests : IDisposable
{
    private readonly string _tmpPath;

    public LyricsOverrideStoreTests()
    {
        _tmpPath = Path.Combine(
            Path.GetTempPath(),
            "lyrics-override-" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tmpPath)) File.Delete(_tmpPath);
    }

    private static string Key(string suffix) => $"artist||album||song{suffix}";

    private static CandidateOrigin LocalOrigin(string path = "/tmp/song.lrc") =>
        new CandidateOrigin.Local(path);

    private static CandidateOrigin LrclibOrigin(int id = 12345) =>
        new CandidateOrigin.Lrclib(id);

    // ---------- Load ----------

    [Fact]
    public void Load_FileMissing_ReturnsEmpty()
    {
        var store = new LyricsOverrideStore();
        var dict = store.Load(_tmpPath);

        Assert.NotNull(dict);
        Assert.Empty(dict);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_tmpPath, "{ not valid json ##");

        var store = new LyricsOverrideStore();

        // JSON 损坏 → 容错返回空字典（开发模式下静默报告，避免打断用户）
        var dict = store.Load(_tmpPath);

        Assert.NotNull(dict);
        Assert.Empty(dict);
    }

    // ---------- Save / Load round-trip ----------

    [Fact]
    public void Save_AndLoad_RoundTrips_Local()
    {
        var store = new LyricsOverrideStore();
        var origin = new CandidateOrigin.Local("/path/to/Artist - Album - Song.lrc");

        store.Save(Key("1"), origin, _tmpPath);
        var dict = store.Load(_tmpPath);

        var actual = Assert.Single(dict);
        Assert.Equal(Key("1"), actual.Key);
        var loadedOrigin = Assert.IsType<CandidateOrigin.Local>(actual.Value);
        Assert.Equal("/path/to/Artist - Album - Song.lrc", loadedOrigin.FilePath);
    }

    [Fact]
    public void Save_AndLoad_RoundTrips_Lrclib()
    {
        var store = new LyricsOverrideStore();
        var origin = new CandidateOrigin.Lrclib(987654321);

        store.Save(Key("2"), origin, _tmpPath);
        var dict = store.Load(_tmpPath);

        var actual = Assert.Single(dict);
        Assert.Equal(Key("2"), actual.Key);
        var loadedOrigin = Assert.IsType<CandidateOrigin.Lrclib>(actual.Value);
        Assert.Equal(987654321, loadedOrigin.LrclibId);
    }

    [Fact]
    public void Save_AndLoad_RoundTrips_Netease()
    {
        var store = new LyricsOverrideStore();
        var origin = new CandidateOrigin.Netease(424242424242L);

        store.Save(Key("net"), origin, _tmpPath);
        var dict = store.Load(_tmpPath);

        var actual = Assert.Single(dict);
        var loadedOrigin = Assert.IsType<CandidateOrigin.Netease>(actual.Value);
        Assert.Equal(424242424242L, loadedOrigin.NeteaseSongId);
    }

    [Fact]
    public void Save_ExistingKey_Replaces()
    {
        var store = new LyricsOverrideStore();
        var key = Key("dup");

        store.Save(key, LocalOrigin("/first.lrc"), _tmpPath);
        store.Save(key, LrclibOrigin(99), _tmpPath);

        var dict = store.Load(_tmpPath);
        var actual = Assert.Single(dict);
        var loadedOrigin = Assert.IsType<CandidateOrigin.Lrclib>(actual.Value);
        Assert.Equal(99, loadedOrigin.LrclibId);

        // 字典里不能残留第一次写入的 Local
        Assert.DoesNotContain(dict.Values, v => v is CandidateOrigin.Local);
    }

    [Fact]
    public void Save_MultipleKeys_PreservesAll()
    {
        var store = new LyricsOverrideStore();

        store.Save(Key("a"), LocalOrigin("/a.lrc"), _tmpPath);
        store.Save(Key("b"), LrclibOrigin(10), _tmpPath);
        store.Save(Key("c"), new CandidateOrigin.Netease(11), _tmpPath);

        var dict = store.Load(_tmpPath);

        Assert.Equal(3, dict.Count);
        Assert.IsType<CandidateOrigin.Local>(dict[Key("a")]);
        Assert.IsType<CandidateOrigin.Lrclib>(dict[Key("b")]);
        Assert.IsType<CandidateOrigin.Netease>(dict[Key("c")]);
    }

    // ---------- Remove ----------

    [Fact]
    public void Remove_ExistingKey_Deletes()
    {
        var store = new LyricsOverrideStore();
        var key = Key("rm");

        store.Save(key, LocalOrigin("/x.lrc"), _tmpPath);
        store.Save(Key("keep"), LrclibOrigin(1), _tmpPath);

        store.Remove(key, _tmpPath);

        var dict = store.Load(_tmpPath);
        Assert.False(dict.ContainsKey(key));
        Assert.True(dict.ContainsKey(Key("keep")));
    }

    [Fact]
    public void Remove_NonExistingKey_NoOp()
    {
        var store = new LyricsOverrideStore();
        var key = Key("keep");

        store.Save(key, LrclibOrigin(7), _tmpPath);

        // 不抛异常 + 已有 key 仍在
        store.Remove(Key("ghost"), _tmpPath);

        var dict = store.Load(_tmpPath);
        Assert.True(dict.ContainsKey(key));
    }

    [Fact]
    public void Remove_OnMissingFile_NoOp()
    {
        var store = new LyricsOverrideStore();

        // 文件不存在也不能崩
        store.Remove(Key("any"), _tmpPath);

        var dict = store.Load(_tmpPath);
        Assert.Empty(dict);
    }

    // ---------- WriteIndented / 文件格式 ----------

    [Fact]
    public void Save_WritesIndentedJson()
    {
        var store = new LyricsOverrideStore();

        store.Save(Key("fmt"), LocalOrigin("/abc.lrc"), _tmpPath);

        var json = File.ReadAllText(_tmpPath);
        // WriteIndented 出来的 JSON 含换行 + 缩进
        Assert.Contains("\n", json);
        Assert.Contains("version", json);
        Assert.Contains("overrides", json);
    }
}
