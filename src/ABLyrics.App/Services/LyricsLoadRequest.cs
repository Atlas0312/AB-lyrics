using ABLyrics.App.Models;

namespace ABLyrics.App.Services;

/// <summary>
/// "为什么加载歌词"的统一描述。所有 UI 操作（Poll / SetSource / ForceReload /
/// ApplyCandidate / Local 导入 / 覆盖项清除）都把意图翻译成 <see cref="LyricsLoadRequest"/>
/// 后再交给 <see cref="PlaybackCoordinator.LoadLyricsAsync"/>，副作用（LoadingFlash、
/// StatusText、LyricsSource、引擎喂入、Local 缺失提示）一律由统一入口集中处理。
/// </summary>
internal sealed record LyricsLoadRequest
{
    /// <summary>取歌词的来源策略。</summary>
    public required LyricsLoadSource Source { get; init; }

    /// <summary>加载失败时的兜底策略。</summary>
    public LyricsLoadFailurePolicy FailurePolicy { get; init; } = LyricsLoadFailurePolicy.None;

    /// <summary>是否触发 LoadingFlash 与"正在尝试 X…"状态文本。Local 源默认 false。</summary>
    public bool FlashOnLoading { get; init; } = true;

    /// <summary>取不到时是否触发 Local 缺失提示。</summary>
    public bool PromptLocalMissing { get; init; }

    /// <summary>加载完成后立刻刷一帧（让暂停状态下 UI 立即反映新歌词）。</summary>
    public bool FlushFrameImmediately { get; init; }

    /// <summary><see cref="LyricsLoadSource.OverrideOnly"/> 时指定要恢复的覆盖项 origin。</summary>
    public CandidateOrigin? OverrideOrigin { get; init; }

    /// <summary><see cref="LyricsLoadSource.ExplicitSource"/> 时指定 "Local"/"LRCLIB"/...。</summary>
    public string? ExplicitSourceName { get; init; }

    /// <summary><see cref="LyricsLoadSource.ExplicitCandidate"/> 时直接给候选。</summary>
    public LyricsCandidate? ExplicitCandidate { get; init; }
}

internal enum LyricsLoadSource
{
    /// <summary>默认行为：覆盖项 → 失败保留 / 不存在则走 LRCLIB→Netease 兜底链。</summary>
    Default,

    /// <summary>仅按指定 <see cref="LyricsLoadRequest.OverrideOrigin"/> 取候选；失败由 FailurePolicy 决定。</summary>
    OverrideOnly,

    /// <summary>按用户显式指定的来源名取（不读 override，不走 fallback chain）。</summary>
    ExplicitSource,

    /// <summary>调用方已经持有候选，直接喂入引擎。</summary>
    ExplicitCandidate,
}

internal enum LyricsLoadFailurePolicy
{
    /// <summary>无特殊处理（fetch 失败返回 null，不报错）。</summary>
    None,

    /// <summary>取 override 失败时保留 override 状态，不回退到其它来源。</summary>
    PreserveOverride,

    /// <summary>当前来源失败时允许走下一级 fallback（LRCLIB→Netease）。</summary>
    FallbackToNext,
}