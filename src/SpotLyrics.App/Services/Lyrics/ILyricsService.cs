using SpotLyrics.App.Models;

namespace SpotLyrics.App.Services.Lyrics;

public interface ILyricsService
{
    Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string sourceName, CancellationToken cancellationToken = default);

    IReadOnlyList<string> AvailableSources { get; }
}
