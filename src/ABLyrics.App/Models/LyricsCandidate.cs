namespace ABLyrics.App.Models;

/// <summary>
/// 统一的歌词候选：本地文件与在线源（LRCLIB / Netease）都映射为同一形态，
/// 让 UI 不必区分"本地 vs 网络"。一条候选对应一个 <see cref="CandidateOrigin"/>，
/// 即可被 <c>LyricsOverrideStore</c> 持久化为"用户上次的选择"。
/// </summary>
public sealed class LyricsCandidate
{
    /// <summary>来源标签，供 UI 显示："Local" / "LRCLIB" / "Netease"。</summary>
    public required string Source { get; init; }

    /// <summary>人类可读的副标题（专辑名、时长、文件名等）。</summary>
    public required string Label { get; init; }

    /// <summary>LRC 同步歌词（[mm:ss.xx] 行格式）。可以为空。</summary>
    public string? SyncedLyrics { get; init; }

    /// <summary>纯文本歌词。可以为空。</summary>
    public string? PlainLyrics { get; init; }

    /// <summary>该候选对应的时长（毫秒）。</summary>
    public int DurationMs { get; init; }

    /// <summary>唯一标识来源，可被序列化并用于覆盖项恢复。</summary>
    public required CandidateOrigin Origin { get; init; }

    /// <summary>UI 圆点与可选性。false → 不可选。</summary>
    public bool IsAvailable { get; init; } = true;
}

/// <summary>
/// 候选来源的标签联合：用 algebraic data type 把"来源"具象化到类型层，
/// 让调用方在写覆盖项、做匹配时无法忽略来源类型差异。
/// </summary>
public abstract record CandidateOrigin
{
    /// <summary>本地 .lrc 文件。</summary>
    public sealed record Local(string FilePath) : CandidateOrigin;

    /// <summary>LRCLIB 条目 id。</summary>
    public sealed record Lrclib(int LrclibId) : CandidateOrigin;

    /// <summary>网易云歌曲 id。</summary>
    public sealed record Netease(long NeteaseSongId) : CandidateOrigin;
}
