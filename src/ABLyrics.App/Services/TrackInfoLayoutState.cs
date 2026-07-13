namespace ABLyrics.App.Services;

/// <summary>
/// 控制曲名区域何时居中：首句歌词前居中，短空白保持左对齐，长间奏（≥3s）再居中。
/// </summary>
internal sealed class TrackInfoLayoutState
{
    public const int CenterAfterEmptyMs = 3000;

    private bool _hasFirstLyricAppeared;
    private DateTimeOffset? _lyricsEmptySince;

    public bool ShouldCenter { get; private set; } = true;

    public bool Update(bool shouldShowLyrics)
    {
        var center = ComputeShouldCenter(shouldShowLyrics);
        if (ShouldCenter == center)
        {
            return false;
        }

        ShouldCenter = center;
        return true;
    }

    public void ResetForNewTrack()
    {
        _hasFirstLyricAppeared = false;
        _lyricsEmptySince = null;
        ShouldCenter = true;
    }

    public void Reset()
    {
        ResetForNewTrack();
    }

    private bool ComputeShouldCenter(bool shouldShowLyrics)
    {
        if (shouldShowLyrics)
        {
            _hasFirstLyricAppeared = true;
        }

        if (!_hasFirstLyricAppeared)
        {
            return true;
        }

        if (shouldShowLyrics)
        {
            _lyricsEmptySince = null;
            return false;
        }

        _lyricsEmptySince ??= DateTimeOffset.UtcNow;
        var emptyMs = (DateTimeOffset.UtcNow - _lyricsEmptySince.Value).TotalMilliseconds;
        return emptyMs >= CenterAfterEmptyMs;
    }
}
