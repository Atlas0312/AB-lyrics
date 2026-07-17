namespace ABLyrics.App.Services;

/// <summary>
/// 控制曲名区域何时居中：首句前居中；有歌词左对齐；
/// 空档时预读下一句非空时长，≥3s（或无下一句）立刻居中，并在本段空档锁定。
/// </summary>
internal sealed class TrackInfoLayoutState
{
    public const int CenterAfterEmptyMs = 3000;

    private bool _hasFirstLyricAppeared;
    private bool _longSilenceLocked;

    public bool ShouldCenter { get; private set; } = true;

    public bool Update(bool shouldShowLyrics, long msUntilNextNonEmpty)
    {
        var center = ComputeShouldCenter(shouldShowLyrics, msUntilNextNonEmpty);
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
        _longSilenceLocked = false;
        ShouldCenter = true;
    }

    public void Reset()
    {
        ResetForNewTrack();
    }

    private bool ComputeShouldCenter(bool shouldShowLyrics, long msUntilNextNonEmpty)
    {
        if (shouldShowLyrics)
        {
            _hasFirstLyricAppeared = true;
            _longSilenceLocked = false;
            return false;
        }

        if (!_hasFirstLyricAppeared)
        {
            return true;
        }

        if (msUntilNextNonEmpty >= CenterAfterEmptyMs)
        {
            _longSilenceLocked = true;
            return true;
        }

        return _longSilenceLocked;
    }
}
