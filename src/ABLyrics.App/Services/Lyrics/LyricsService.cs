using System.IO;
using Lyricify.Lyrics.Providers.Web.Netease;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

public sealed class LyricsService : ILyricsService
{
    private readonly NetEaseSettings _netEaseSettings;
    private readonly LrcLibClient _lrcLibClient;
    private readonly Api _neteaseApi = new();
    private readonly LocalLyricsProvider _localProvider;
    private readonly IReadOnlyList<string> _availableSources;

    public LyricsService(AppSettings settings)
        : this(settings, new LrcLibClient(settings.Lyrics.UserAgent))
    {
    }

    /// <summary>
    /// 测试构造：注入自定义 <see cref="LrcLibClient"/>（例如带
    /// <see cref="HttpMessageHandler"/> 替身的实例）以模拟 LRCLIB 网络响应。
    /// </summary>
    internal LyricsService(AppSettings settings, LrcLibClient lrcLibClient)
    {
        _netEaseSettings = settings.NetEase;
        _lrcLibClient = lrcLibClient;
        _localProvider = new LocalLyricsProvider(settings);

        var sources = new List<string> { "LRCLIB" };
        if (!string.IsNullOrWhiteSpace(settings.NetEase.MusicU))
            sources.Add("Netease");
        sources.Add("Local");
        _availableSources = sources.AsReadOnly();
    }

    public async Task<LyricsCandidate?> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // FetchFromLrcLibAsync 已丢弃 plain-only；命中即带 SyncedLyrics。
        var lrcLibResult = await FetchFromLrcLibAsync(track, cancellationToken).ConfigureAwait(false);
        if (lrcLibResult is not null)
        {
            return lrcLibResult;
        }

        // LRCLIB miss（含仅有 plain）→ Netease（若已配置）→ Local
        if (!string.IsNullOrWhiteSpace(_netEaseSettings.MusicU))
        {
            var netease = await FetchFromNetEaseAsync(track, cancellationToken).ConfigureAwait(false);
            if (netease is not null && !string.IsNullOrWhiteSpace(netease.SyncedLyrics))
            {
                return netease;
            }
        }

        var local = await _localProvider.GetAsync(track, cancellationToken).ConfigureAwait(false);
        return local is not null && !string.IsNullOrWhiteSpace(local.SyncedLyrics) ? local : null;
    }

    public async Task<LyricsCandidate?> FetchFromSourceAsync(TrackInfo track, string sourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (sourceName)
        {
            case "LRCLIB":
                return await FetchFromLrcLibAsync(track, cancellationToken).ConfigureAwait(false);
            case "Netease":
                return await FetchFromNetEaseAsync(track, cancellationToken).ConfigureAwait(false);
            case "Local":
                return await _localProvider.GetAsync(track, cancellationToken).ConfigureAwait(false);
            default:
                return null;
        }
    }

    public IReadOnlyList<string> AvailableSources => _availableSources;

    public async Task<LyricsCandidate?> FetchCandidateAsync(
        TrackInfo track,
        CandidateOrigin origin,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (origin)
        {
            case CandidateOrigin.Local local:
                {
                    if (!File.Exists(local.FilePath)) return null;
                    var content = await File.ReadAllTextAsync(local.FilePath, cancellationToken)
                        .ConfigureAwait(false);
                    return new LyricsCandidate
                    {
                        Source = "Local",
                        Label = Path.GetFileNameWithoutExtension(local.FilePath),
                        SyncedLyrics = content,
                        PlainLyrics = content,
                        DurationMs = track.DurationMs,
                        Origin = origin,
                    };
                }
            case CandidateOrigin.Lrclib lrclibOrigin:
                {
                    var resp = await _lrcLibClient
                        .GetByIdAsync(lrclibOrigin.LrclibId, cancellationToken)
                        .ConfigureAwait(false);
                    if (resp is null) return null;

                    var synced = Normalize(resp.SyncedLyrics);
                    var plain = Normalize(resp.PlainLyrics);
                    if (string.IsNullOrWhiteSpace(synced))
                    {
                        return null;
                    }

                    return new LyricsCandidate
                    {
                        Source = "LRCLIB",
                        Label = "LRCLIB",
                        SyncedLyrics = synced,
                        PlainLyrics = plain,
                        DurationMs = track.DurationMs,
                        Origin = origin,
                    };
                }
            case CandidateOrigin.Netease:
                // 本期 Netease 不作为覆盖项的常见来源：覆盖项恢复路径不实现。
                return null;
            default:
                return null;
        }
    }

    private async Task<LyricsCandidate?> FetchFromLrcLibAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        var durationSeconds = track.DurationMs > 0 ? track.DurationMs / 1000.0 : (double?)null;
        var lrcResult = await _lrcLibClient
            .GetAsync(track.Name, track.Artist, track.Album, durationSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (lrcResult is not null)
        {
            var synced = Normalize(lrcResult.SyncedLyrics);
            var plain = Normalize(lrcResult.PlainLyrics);
            if (!string.IsNullOrWhiteSpace(synced))
            {
                return new LyricsCandidate
                {
                    Source = "LRCLIB",
                    Label = "LRCLIB",
                    SyncedLyrics = synced,
                    PlainLyrics = plain,
                    DurationMs = track.DurationMs,
                    Origin = new CandidateOrigin.Lrclib(lrcResult.Id),
                };
            }

        }

        // 精确 get 未命中 synced（404 / plain-only）：改走官网同款 q= 搜索，按时长取最近 synced。
        var hits = await _lrcLibClient
            .SearchAsync(track.Name, track.Artist, track.Album, cancellationToken)
            .ConfigureAwait(false);
        var best = hits
            .Where(h => !string.IsNullOrWhiteSpace(h.SyncedLyrics))
            .OrderBy(h => Math.Abs(h.DurationMs - track.DurationMs))
            .FirstOrDefault();


        if (best is null)
        {
            return null;
        }

        var bestSynced = Normalize(best.SyncedLyrics);
        if (string.IsNullOrWhiteSpace(bestSynced))
        {
            return null;
        }

        return new LyricsCandidate
        {
            Source = "LRCLIB",
            Label = "LRCLIB",
            SyncedLyrics = bestSynced,
            PlainLyrics = Normalize(best.PlainLyrics),
            DurationMs = track.DurationMs,
            Origin = new CandidateOrigin.Lrclib(best.Id),
        };
    }

    private async Task<LyricsCandidate?> FetchFromNetEaseAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keyword = $"{track.Artist} {track.Name}".Trim();
        var search = await _neteaseApi.SearchNew(keyword).ConfigureAwait(false);
        var songs = search?.Result?.Songs;
        if (songs is null || songs.Length == 0)
        {
            return null;
        }

        var best = songs
            .OrderBy(song => Math.Abs(song.Duration - track.DurationMs))
            .First();

        var lyric = await _neteaseApi.GetLyric(best.Id).ConfigureAwait(false);
        var synced = Normalize(lyric?.Lrc?.Lyric);
        var plain = synced;

        if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain))
        {
            return null;
        }

        var neteaseId = long.TryParse(best.Id, out var parsedId) ? parsedId : 0L;

        return new LyricsCandidate
        {
            Source = "Netease",
            Label = "Netease",
            SyncedLyrics = synced,
            PlainLyrics = plain,
            DurationMs = track.DurationMs,
            Origin = new CandidateOrigin.Netease(neteaseId),
        };
    }

    private static string? Normalize(string? value) => LyricsTextNormalizer.Normalize(value);
}
