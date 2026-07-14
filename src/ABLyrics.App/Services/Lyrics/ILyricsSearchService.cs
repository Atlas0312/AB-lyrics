using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

/// <summary>
/// 歌词库聚合层：对上层（候选选择窗口、PlaybackCoordinator）暴露统一的多候选
/// 搜索 + 启动时探活入口，把 Local / LRCLIB / Netease 三个源封到同一接口后面。
/// </summary>
public interface ILyricsSearchService
{
    /// <summary>
    /// 在指定库下搜索某曲目的所有候选。library 取值 "Local" / "LRCLIB" / "Netease"；
    /// 未知库返回空数组而非抛异常。
    /// </summary>
    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(
        TrackInfo track,
        string library,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动时探活：返回每个库当前是否可达。Netease 只在 MusicU 非空时纳入结果。
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken cancellationToken = default);
}