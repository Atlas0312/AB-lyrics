using Lyricify.Lyrics.Providers.Web.Netease;
using OpenccNetLib;
using SpotLyrics.App.Configuration;
using SpotLyrics.App.Models;

namespace SpotLyrics.App.Services.Lyrics;

public sealed class LyricsService : ILyricsService
{
    private static readonly Opencc _chineseConverter = new("t2s");
    private readonly NetEaseSettings _netEaseSettings;
    private readonly LrcLibClient _lrcLibClient;
    private readonly Api _neteaseApi = new();
    private readonly LocalLyricsProvider _localProvider;
    private readonly IReadOnlyList<string> _availableSources;

    public LyricsService(AppSettings settings)
    {
        _netEaseSettings = settings.NetEase;
        _lrcLibClient = new LrcLibClient(settings.Lyrics.UserAgent);
        _localProvider = new LocalLyricsProvider(settings);

        var sources = new List<string> { "LRCLIB" };
        if (!string.IsNullOrWhiteSpace(settings.NetEase.MusicU))
            sources.Add("Netease");
        sources.Add("Local");
        _availableSources = sources.AsReadOnly();
    }

    public async Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lrcLibResult = await FetchFromLrcLibAsync(track, cancellationToken).ConfigureAwait(false);

        if (lrcLibResult is not null)
        {
            if (!string.IsNullOrWhiteSpace(lrcLibResult.SyncedLyrics))
            {
                return lrcLibResult;
            }

            // LRCLIB returned only plain lyrics
            if (!string.IsNullOrWhiteSpace(_netEaseSettings.MusicU))
            {
                var netease = await FetchFromNetEaseAsync(track, cancellationToken).ConfigureAwait(false);
                if (netease is not null && !string.IsNullOrWhiteSpace(netease.SyncedLyrics))
                {
                    return new LyricsResult
                    {
                        Source = netease.Source,
                        SyncedLyrics = netease.SyncedLyrics,
                        PlainLyrics = lrcLibResult.PlainLyrics,
                    };
                }
            }

            return lrcLibResult;
        }

        if (string.IsNullOrWhiteSpace(_netEaseSettings.MusicU))
        {
            return null;
        }

        return await FetchFromNetEaseAsync(track, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string sourceName, CancellationToken cancellationToken = default)
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

    private async Task<LyricsResult?> FetchFromLrcLibAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        var durationSeconds = track.DurationMs > 0 ? track.DurationMs / 1000.0 : (double?)null;
        var lrcResult = await _lrcLibClient
            .GetAsync(track.Name, track.Artist, track.Album, durationSeconds, cancellationToken)
            .ConfigureAwait(false);

        if (lrcResult is null) return null;

        var synced = Normalize(lrcResult.SyncedLyrics);
        var plain = Normalize(lrcResult.PlainLyrics);

        if (!string.IsNullOrWhiteSpace(synced))
            return new LyricsResult { Source = "LRCLIB", SyncedLyrics = synced, PlainLyrics = plain };

        return !string.IsNullOrWhiteSpace(plain)
            ? new LyricsResult { Source = "LRCLIB", PlainLyrics = plain }
            : null;
    }

    private async Task<LyricsResult?> FetchFromNetEaseAsync(TrackInfo track, CancellationToken cancellationToken)
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

        return new LyricsResult
        {
            Source = "Netease",
            SyncedLyrics = synced,
            PlainLyrics = plain,
        };
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return _chineseConverter.Convert(value.Trim());
    }
}
