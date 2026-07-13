namespace ABLyrics.App.Models;

public sealed class TrackInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Artist { get; init; }
    public string Album { get; init; } = string.Empty;
    public int DurationMs { get; init; }
    public string? Isrc { get; init; }
}
