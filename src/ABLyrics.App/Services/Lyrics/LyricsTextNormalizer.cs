using OpenccNetLib;

namespace ABLyrics.App.Services.Lyrics;

/// <summary>
/// 共享的歌词文本规范化器：把所有来源（LRCLIB / Netease / 本地 .lrc）
/// 的文本统一做繁体→简体（t2s）转换，再喂给候选选择器或主引擎，
/// 避免 AppBar 显示与 picker 显示、跨来源显示不一致。
/// 任何要构造 <see cref="LyricsCandidate"/> / <see cref="LyricsResult"/>
/// 的路径都应先调用本类的 Normalize / NormalizeAll 方法。
/// </summary>
public static class LyricsTextNormalizer
{
    private static readonly Opencc _chineseConverter = new("t2s");

    /// <summary>输入为 null/空白则返回 null；否则返回 t2s 转换后的字符串。</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return _chineseConverter.Convert(value.Trim());
    }

    /// <summary>
    /// 同时对 SyncedLyrics / PlainLyrics 做 Normalize。
    /// 任一字段为 null/空白时回退到原值，保证不丢内容。
    /// </summary>
    public static (string? Synced, string? Plain) NormalizeAll(string? synced, string? plain)
    {
        return (Normalize(synced) ?? synced, Normalize(plain) ?? plain);
    }
}
