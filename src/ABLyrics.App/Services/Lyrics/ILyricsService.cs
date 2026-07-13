using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

public interface ILyricsService
{
    Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string sourceName, CancellationToken cancellationToken = default);

    IReadOnlyList<string> AvailableSources { get; }
}
