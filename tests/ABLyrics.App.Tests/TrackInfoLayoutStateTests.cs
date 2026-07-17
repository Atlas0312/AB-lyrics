using ABLyrics.App.Services;
using Xunit;

namespace ABLyrics.App.Tests;

public class TrackInfoLayoutStateTests
{
    [Fact]
    public void BeforeFirstLyric_Centers()
    {
        var state = new TrackInfoLayoutState();
        Assert.True(state.ShouldCenter);

        state.Update(shouldShowLyrics: false, msUntilNextNonEmpty: 500);
        Assert.True(state.ShouldCenter);
    }

    [Fact]
    public void WithLyrics_LeftAligns()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        Assert.False(state.ShouldCenter);
    }

    [Fact]
    public void ShortEmptyGap_StaysLeftAligned()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, msUntilNextNonEmpty: 1500);
        Assert.False(state.ShouldCenter);
    }

    [Fact]
    public void LongEmptyGap_CentersImmediately()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        var changed = state.Update(false, msUntilNextNonEmpty: 5000);
        Assert.True(changed);
        Assert.True(state.ShouldCenter);
    }

    [Fact]
    public void LongGapLock_KeepsCenteredWhenRemainingDropsBelowThreshold()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, 5000);
        Assert.True(state.ShouldCenter);

        state.Update(false, msUntilNextNonEmpty: 500);
        Assert.True(state.ShouldCenter);
    }

    [Fact]
    public void LyricsReturn_ClearsLockAndLeftAligns()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, 5000);
        state.Update(true, 0);
        Assert.False(state.ShouldCenter);
    }

    [Fact]
    public void NoNextLine_CentersImmediately()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, long.MaxValue);
        Assert.True(state.ShouldCenter);
    }

    [Fact]
    public void Reset_RestoresCenteredBeforeFirstLyric()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, 5000);
        state.Reset();
        Assert.True(state.ShouldCenter);

        state.Update(false, 500);
        Assert.True(state.ShouldCenter);
    }

    [Fact]
    public void SeekIntoLongGapTail_WithoutPriorLock_StaysLeftAligned()
    {
        var state = new TrackInfoLayoutState();
        state.Update(true, 0);
        state.Update(false, msUntilNextNonEmpty: 1500);
        Assert.False(state.ShouldCenter);
    }
}
