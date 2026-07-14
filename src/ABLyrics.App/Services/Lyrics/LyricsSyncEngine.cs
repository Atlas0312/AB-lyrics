using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

public sealed class LyricsSyncEngine
{
    private LyricsData? _lyricsData;
    private string[] _plainLines = [];
    private int _durationMs;

    public void SetDurationMs(int durationMs)
    {
        _durationMs = durationMs;
    }

    public void Load(LyricsResult? lyrics)
    {
        if (lyrics is null)
        {
            _lyricsData = null;
            _plainLines = [];
            return;
        }

        if (!string.IsNullOrWhiteSpace(lyrics.SyncedLyrics))
        {
            _lyricsData = LrcParser.Parse(lyrics.SyncedLyrics);
            if (_lyricsData?.Lines is { Count: > 0 })
            {
                _plainLines = [];
                return;
            }
        }

        _lyricsData = null;
        _plainLines = (lyrics.PlainLyrics ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 直接喂入已解析的 <see cref="LyricsData"/> 与纯文本行：对比区的多个引擎实例
    /// 只在候选变更时由调用方 Parse 一次（避免每列重复 Parse 同样的 LRC），
    /// 再走这个重载跳过 <see cref="LrcParser.Parse"/>。语义与 <see cref="Load"/>
    /// 在解析之后的状态完全一致。
    /// </summary>
    public void LoadParsed(LyricsData? data, string[] plainLines)
    {
        _lyricsData = data;
        _plainLines = plainLines ?? Array.Empty<string>();
    }

    public LyricsFrame GetFrame(long progressMs)
    {
        if (_lyricsData?.Lines is { Count: > 0 } lines)
        {
            var firstStart = lines[0].StartTime ?? 0;
            if (progressMs < firstStart)
            {
                return new LyricsFrame(string.Empty, string.Empty, string.Empty, true, false);
            }

            var index = FindLineIndex(lines, progressMs);
            var current = lines[index].Text;
            var previous = index > 0 ? lines[index - 1].Text : string.Empty;
            var next = index < lines.Count - 1 ? lines[index + 1].Text : string.Empty;
            return new LyricsFrame(current, previous, next, true, true);
        }

        if (_plainLines.Length > 0)
        {
            var durationMs = _durationMs > 0 ? _durationMs : _plainLines.Length * 3000L;
            var index = (int)(progressMs * _plainLines.Length / durationMs);
            index = Math.Clamp(index, 0, _plainLines.Length - 1);
            return new LyricsFrame(_plainLines[index], string.Empty, string.Empty, false, true);
        }

        return LyricsFrame.Empty;
    }

    private static int FindLineIndex(IReadOnlyList<ILineInfo> lines, long progressMs)
    {
        var index = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var start = lines[i].StartTime ?? 0;
            if (start <= progressMs)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }
}

public readonly record struct LyricsFrame(
    string CurrentLine,
    string PreviousLine,
    string NextLine,
    bool IsSynced,
    bool IsActive)
{
    public static LyricsFrame Empty { get; } = new(string.Empty, string.Empty, string.Empty, false, false);
}
