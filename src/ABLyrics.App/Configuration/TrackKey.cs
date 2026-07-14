using ABLyrics.App.Models;

namespace ABLyrics.App.Configuration;

/// <summary>
/// 静态助手：将曲目三要素 (Artist, Album, Name) 规范化为稳定的字符串键，
/// 用于跨会话的覆盖项持久化、本地回写等场景。规范化为 trim + 合并空白 + 小写，
/// Album 为空时仍保留分隔符（"a1||a2||a3" → 空 album 时为 "a1||||a3"）。
/// </summary>
public static class TrackKey
{
    /// <summary>
    /// 由 <see cref="TrackInfo"/> 三要素生成键。委托给三元组重载。
    /// </summary>
    public static string From(TrackInfo track) =>
        From(track.Artist, track.Album, track.Name);

    /// <summary>
    /// 由三个字符串字段生成键。规范化规则：
    /// 单字段 trim 头尾空白 → 合并内部连续空白为单个空格 → 转小写（invariant 文化）。
    /// 空或全空白字段视为空串，但仍占位（保留 `||` 分隔符）。
    /// </summary>
    public static string From(string artist, string album, string name)
    {
        var a = Normalize(artist);
        var al = Normalize(album);
        var n = Normalize(name);
        // 显式四段：Album 为空时为 "artist||||name"，非空为 "artist||album||name"
        return $"{a}||{al}||{n}";
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        // Trim → 按默认分隔符切分并丢弃空段 → 单空格 join → 小写
        var trimmed = s.Trim();
        var parts = trimmed.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }
}
