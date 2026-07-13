namespace ABLyrics.App.Models;

public sealed class PlaybackState
{
    public required TrackInfo Track { get; init; }
    public long ProgressMs { get; init; }
    public bool IsPlaying { get; init; }
}
