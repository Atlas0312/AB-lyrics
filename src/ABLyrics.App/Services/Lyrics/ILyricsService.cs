using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

public interface ILyricsService
{
    Task<LyricsCandidate?> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    Task<LyricsCandidate?> FetchFromSourceAsync(TrackInfo track, string sourceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按指定 <see cref="CandidateOrigin"/> 拉取该曲目在指定来源下的歌词。
    /// 给"覆盖项"路径用：用户上次选择某个具体版本时，回放时应能精准拉到那份内容，
    /// 而不走 <see cref="FetchLyricsAsync"/> 的兜底链。
    /// 返回的候选 <see cref="LyricsCandidate.Origin"/> 即为传入的 <paramref name="origin"/>。
    /// 本期 Netease 路径返回 null（spec 已声明不在多候选范围内）。
    /// </summary>
    Task<LyricsCandidate?> FetchCandidateAsync(
        TrackInfo track,
        CandidateOrigin origin,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> AvailableSources { get; }
}
