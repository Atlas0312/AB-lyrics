namespace ABLyrics.App.Models;

public sealed class LyricsResult
{
    public required string Source { get; init; }
    public string? SyncedLyrics { get; init; }
    public string? PlainLyrics { get; init; }
    public bool HasSyncedLyrics => !string.IsNullOrWhiteSpace(SyncedLyrics);
}
