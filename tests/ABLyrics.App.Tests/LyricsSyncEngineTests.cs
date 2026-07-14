using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsSyncEngineTests
{
    /// <summary>
    /// 用真实 LrcParser 拿到一个 Lines 已初始化的 LyricsData，
    /// 避免依赖默认构造时 Lines 是否为 null 的内部细节。
    /// </summary>
    private static LyricsData ParseData(string lrc)
    {
        var parsed = LrcParser.Parse(lrc);
        if (parsed is null)
        {
            // 极端兜底：确保 Lines 始终非 null，方便测试断言
            parsed = new LyricsData { Lines = new System.Collections.Generic.List<ILineInfo>() };
        }
        return parsed;
    }

    private static LyricsData NewDataWith(params ILineInfo[] lines)
    {
        var data = ParseData("[00:01.00] placeholder");
        data.Lines!.Clear();
        foreach (var l in lines)
        {
            data.Lines.Add(l);
        }
        return data;
    }

    [Fact]
    public void LoadParsed_WithSyncedData_GetFrameAtProgress_ReturnsExpectedLine()
    {
        var data = NewDataWith(new LineInfo
        {
            Text = "current",
            StartTime = 5000,
        });

        var engine = new LyricsSyncEngine();
        engine.LoadParsed(data, plainLines: new[] { "current" });

        var frame = engine.GetFrame(6000);

        Assert.Equal("current", frame.CurrentLine);
        Assert.True(frame.IsSynced);
    }

    [Fact]
    public void LoadParsed_WithPlainLines_GetFrameAtProgress_ReturnsExpectedLine()
    {
        var plainLines = new[] { "line1", "line2", "line3" };

        var engine = new LyricsSyncEngine();
        engine.SetDurationMs(plainLines.Length * 3000);
        engine.LoadParsed(data: null, plainLines: plainLines);

        var frame = engine.GetFrame(plainLines.Length * 3000 / 2);

        Assert.False(frame.IsSynced);
        Assert.Equal("line2", frame.CurrentLine);
    }

    [Fact]
    public void LoadParsed_WithNullData_EmptyLines_GetFrameReturnsEmpty()
    {
        var engine = new LyricsSyncEngine();
        engine.LoadParsed(data: null, plainLines: System.Array.Empty<string>());

        var frame = engine.GetFrame(1000);

        Assert.False(frame.IsActive);
        Assert.Equal(string.Empty, frame.CurrentLine);
        Assert.Equal(string.Empty, frame.PreviousLine);
        Assert.Equal(string.Empty, frame.NextLine);
    }

    [Fact]
    public void LoadParsed_AfterSyncedData_ThenLoadAgainWithPlainLines_OverwritesState()
    {
        var data = NewDataWith(new LineInfo { Text = "synced", StartTime = 1000 });

        var engine = new LyricsSyncEngine();
        engine.LoadParsed(data, new[] { "synced" });

        // 再 LoadParsed 一次完全不同的纯文本
        engine.LoadParsed(null, new[] { "plain-a", "plain-b" });

        var frame = engine.GetFrame(0);
        Assert.False(frame.IsSynced);
        Assert.Equal("plain-a", frame.CurrentLine);
    }
}