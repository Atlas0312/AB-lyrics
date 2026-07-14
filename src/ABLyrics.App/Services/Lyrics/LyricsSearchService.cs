using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace ABLyrics.App.Services.Lyrics;

/// <summary>
/// <see cref="ILyricsSearchService"/> 的实现：把本地 / LRCLIB / Netease 三种
/// 搜索结果统一投影为 <see cref="LyricsCandidate"/>，并负责启动时的探活。
/// 不参与"主歌词流"（那是 <see cref="LyricsService"/> 的兜底链职责），只服务
/// 候选选择窗口和覆盖项的发现路径。
/// </summary>
internal sealed class LyricsSearchService : ILyricsSearchService
{
    private readonly AppSettings _settings;
    private readonly LrcLibClient _lrcLibClient;
    private readonly LocalLyricsSearchProvider _localSearch;

    public LyricsSearchService(AppSettings settings)
        : this(settings, new LrcLibClient(settings.Lyrics.UserAgent))
    {
    }

    /// <summary>
    /// 测试构造：注入自定义 <see cref="LrcLibClient"/>（例如带
    /// <see cref="HttpMessageHandler"/> 替身的实例）以模拟 LRCLIB 网络响应。
    /// </summary>
    internal LyricsSearchService(AppSettings settings, LrcLibClient lrcLibClient)
    {
        _settings = settings;
        _lrcLibClient = lrcLibClient;
        _localSearch = new LocalLyricsSearchProvider(settings);
    }

    public Task<IReadOnlyList<LyricsCandidate>> SearchAsync(
        TrackInfo track,
        string library,
        CancellationToken cancellationToken = default)
    {
        return library switch
        {
            "Local" => Task.FromResult(_localSearch.Search(track)),
            "LRCLIB" => SearchLrclibAsync(track, cancellationToken),
            "Netease" => SearchNeteaseAsync(track, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<LyricsCandidate>>(Array.Empty<LyricsCandidate>()),
        };
    }

    public async Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["LRCLIB"] = await ProbeLrclibAsync(cancellationToken).ConfigureAwait(false),
            ["Local"] = ProbeLocal(),
        };

        if (!string.IsNullOrWhiteSpace(_settings.NetEase.MusicU))
        {
            result["Netease"] = await ProbeNeteaseAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    // ---------- 库特定实现 ----------

    private async Task<IReadOnlyList<LyricsCandidate>> SearchLrclibAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        var hits = await _lrcLibClient
            .SearchAsync(track.Name, track.Artist, track.Album, cancellationToken)
            .ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return Array.Empty<LyricsCandidate>();
        }

        var result = new List<LyricsCandidate>(hits.Count);
        foreach (var hit in hits.OrderBy(h => Math.Abs(h.DurationMs - track.DurationMs)))
        {
            result.Add(new LyricsCandidate
            {
                Source = "LRCLIB",
                Label = BuildLrclibLabel(hit),
                SyncedLyrics = hit.SyncedLyrics,
                PlainLyrics = hit.PlainLyrics,
                DurationMs = hit.DurationMs,
                Origin = new CandidateOrigin.Lrclib(hit.Id),
            });
        }
        return result;
    }

    private async Task<IReadOnlyList<LyricsCandidate>> SearchNeteaseAsync(
        TrackInfo track,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.NetEase.MusicU))
        {
            return Array.Empty<LyricsCandidate>();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var api = new Api();
            var keyword = $"{track.Artist} {track.Name}".Trim();
            var search = await api.SearchNew(keyword).ConfigureAwait(false);
            var songs = search?.Result?.Songs;
            if (songs is null || songs.Length == 0)
            {
                return Array.Empty<LyricsCandidate>();
            }

            // 单最佳匹配：按 duration 距离排序取首条
            var trackDurationMs = (long)track.DurationMs;
            var best = songs.OrderBy(s => Math.Abs(s.Duration - trackDurationMs)).First();

            var lyric = await api.GetLyric(best.Id).ConfigureAwait(false);
            var synced = lyric?.Lrc?.Lyric;

            // 字符串 Id 转 long 用于 CandidateOrigin.Netease；解析失败时回退 0，
            // 不应阻塞候选展示。
            var neteaseId = long.TryParse(best.Id, out var parsedId) ? parsedId : 0L;

            return new[]
            {
                new LyricsCandidate
                {
                    Source = "Netease",
                    Label = best.Name ?? track.Name,
                    SyncedLyrics = synced,
                    PlainLyrics = synced,
                    DurationMs = (int)best.Duration,
                    Origin = new CandidateOrigin.Netease(neteaseId),
                },
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 与 LyricsService.FetchFromNetEaseAsync 同样的容错：网络问题不应拖累搜索流程
            return Array.Empty<LyricsCandidate>();
        }
    }

    private async Task<bool> ProbeLrclibAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            return await _lrcLibClient.ProbeAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private bool ProbeLocal()
    {
        try
        {
            // LocalLyricsSearchProvider 构造时已 CreateDirectory；只要路径可读即视为可达。
            // 不强求目录里必须有 .lrc —— 启动时库为空是合法状态。
            var path = _settings.Lyrics.LocalPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = _localSearch.LibraryPath;
            }
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ProbeNeteaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.NetEase.MusicU)) return false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var api = new Api();
            await api.SearchNew("test").ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildLrclibLabel(LrcLibSearchHit hit)
    {
        var hasAlbum = !string.IsNullOrWhiteSpace(hit.AlbumName);
        var dur = FormatDuration(hit.DurationMs);
        return hasAlbum ? $"{hit.AlbumName} · {dur}" : dur;
    }

    private static string FormatDuration(int durationMs)
    {
        var totalSeconds = Math.Max(0, durationMs / 1000);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }
}