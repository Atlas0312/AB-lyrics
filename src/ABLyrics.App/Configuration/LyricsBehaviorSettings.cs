namespace ABLyrics.App.Configuration;

public sealed class LyricsBehaviorSettings
{
    public bool PromptForLocalLyricsOnMissing { get; set; } = true;

    public LyricsBehaviorSettings Clone()
    {
        return new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = PromptForLocalLyricsOnMissing,
        };
    }
}