# 曲名居中预读静默 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把曲名居中从「空档墙钟满 3 秒」改为「预读下一句非空歌词；长静默空档一开始即居中」。

**架构：** `LyricsSyncEngine.GetFrame` 增加 `MsUntilNextNonEmptyLine`；`TrackInfoLayoutState` 用该预读值 + 长静默锁定取代 `DateTimeOffset` 计时；`PlaybackCoordinator.UpdateLyricFrame` 把预读传入 Layout。

**技术栈：** .NET 8 + xUnit；规格见 `docs/superpowers/specs/2026-07-17-track-info-center-lookahead-design.md`。

---

## 文件结构

**修改：**
- `src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs` — `LyricsFrame` 增加 `MsUntilNextNonEmptyLine`；同步路径计算预读；纯文本返回 `0`；空帧返回 `long.MaxValue`
- `src/ABLyrics.App/Services/TrackInfoLayoutState.cs` — 去掉墙钟；`Update(shouldShowLyrics, msUntilNextNonEmpty)` + `_longSilenceLocked`
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs` — `UpdateLyricFrame` 传入预读字段

**新建测试：**
- `tests/ABLyrics.App.Tests/TrackInfoLayoutStateTests.cs`
- 在 `tests/ABLyrics.App.Tests/LyricsSyncEngineTests.cs` 追加预读用例

**职责边界：**
- 引擎只算「距下一句非空还有多久」，不决定是否居中
- Layout 只根据 `shouldShowLyrics` + 预读 ms 决定居中
- Coordinator 只做接线

---

### 任务 1：`LyricsFrame` 预读字段 + 引擎计算（TDD）

**文件：**
- 修改：`src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs`
- 测试：`tests/ABLyrics.App.Tests/LyricsSyncEngineTests.cs`

- [ ] **步骤 1：编写失败的预读测试**

在 `LyricsSyncEngineTests.cs` 末尾追加：

```csharp
[Fact]
public void GetFrame_OnEmptyLine_ReportsMsUntilNextNonEmpty()
{
    var data = NewDataWith(
        new LineInfo { Text = "verse", StartTime = 0 },
        new LineInfo { Text = "   ", StartTime = 5000 },
        new LineInfo { Text = "chorus", StartTime = 12000 });

    var engine = new LyricsSyncEngine();
    engine.LoadParsed(data, plainLines: []);

    var frame = engine.GetFrame(5000);

    Assert.True(string.IsNullOrWhiteSpace(frame.CurrentLine));
    Assert.Equal(7000, frame.MsUntilNextNonEmptyLine);
}

[Fact]
public void GetFrame_SkipsConsecutiveEmptyLines_WhenComputingLookahead()
{
    var data = NewDataWith(
        new LineInfo { Text = "a", StartTime = 0 },
        new LineInfo { Text = "", StartTime = 1000 },
        new LineInfo { Text = "  ", StartTime = 2000 },
        new LineInfo { Text = "b", StartTime = 8000 });

    var engine = new LyricsSyncEngine();
    engine.LoadParsed(data, plainLines: []);

    var frame = engine.GetFrame(1000);

    Assert.Equal(7000, frame.MsUntilNextNonEmptyLine);
}

[Fact]
public void GetFrame_WhenNoLaterNonEmpty_ReturnsMaxValue()
{
    var data = NewDataWith(
        new LineInfo { Text = "last", StartTime = 0 },
        new LineInfo { Text = "", StartTime = 4000 });

    var engine = new LyricsSyncEngine();
    engine.LoadParsed(data, plainLines: []);

    var frame = engine.GetFrame(4000);

    Assert.Equal(long.MaxValue, frame.MsUntilNextNonEmptyLine);
}

[Fact]
public void GetFrame_BeforeFirstLine_ReportsMsUntilFirstNonEmpty()
{
    var data = NewDataWith(new LineInfo { Text = "hello", StartTime = 5000 });

    var engine = new LyricsSyncEngine();
    engine.LoadParsed(data, plainLines: []);

    var frame = engine.GetFrame(2000);

    Assert.False(frame.IsActive);
    Assert.Equal(3000, frame.MsUntilNextNonEmptyLine);
}

[Fact]
public void GetFrame_PlainLines_MsUntilNextNonEmptyIsZero()
{
    var engine = new LyricsSyncEngine();
    engine.SetDurationMs(9000);
    engine.LoadParsed(null, new[] { "a", "b", "c" });

    var frame = engine.GetFrame(1000);

    Assert.Equal(0, frame.MsUntilNextNonEmptyLine);
}

[Fact]
public void GetFrame_EmptyEngine_MsUntilNextNonEmptyIsMaxValue()
{
    var engine = new LyricsSyncEngine();
    var frame = engine.GetFrame(0);
    Assert.Equal(long.MaxValue, frame.MsUntilNextNonEmptyLine);
}
```

- [ ] **步骤 2：运行测试确认失败**

```powershell
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~LyricsSyncEngineTests.GetFrame_OnEmptyLine"
```

预期：编译失败或断言失败（`MsUntilNextNonEmptyLine` 尚不存在）。

- [ ] **步骤 3：扩展 `LyricsFrame` 并实现预读**

将 `LyricsFrame` 改为：

```csharp
public readonly record struct LyricsFrame(
    string CurrentLine,
    string PreviousLine,
    string NextLine,
    bool IsSynced,
    bool IsActive,
    long MsUntilNextNonEmptyLine)
{
    public static LyricsFrame Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, false, false, long.MaxValue);
}
```

在 `LyricsSyncEngine` 中增加：

```csharp
private static long FindMsUntilNextNonEmpty(IReadOnlyList<ILineInfo> lines, long progressMs)
{
    for (var i = 0; i < lines.Count; i++)
    {
        var start = lines[i].StartTime ?? 0;
        if (start <= progressMs)
        {
            continue;
        }

        if (!string.IsNullOrWhiteSpace(lines[i].Text))
        {
            return start - progressMs;
        }
    }

    return long.MaxValue;
}
```

更新 `GetFrame`：

```csharp
public LyricsFrame GetFrame(long progressMs)
{
    if (_lyricsData?.Lines is { Count: > 0 } lines)
    {
        var msUntilNext = FindMsUntilNextNonEmpty(lines, progressMs);
        var firstStart = lines[0].StartTime ?? 0;
        if (progressMs < firstStart)
        {
            return new LyricsFrame(string.Empty, string.Empty, string.Empty, true, false, msUntilNext);
        }

        var index = FindLineIndex(lines, progressMs);
        var current = lines[index].Text ?? string.Empty;
        var previous = index > 0 ? lines[index - 1].Text ?? string.Empty : string.Empty;
        var next = index < lines.Count - 1 ? lines[index + 1].Text ?? string.Empty : string.Empty;
        return new LyricsFrame(current, previous, next, true, true, msUntilNext);
    }

    if (_plainLines.Length > 0)
    {
        var durationMs = _durationMs > 0 ? _durationMs : _plainLines.Length * 3000L;
        var index = (int)(progressMs * _plainLines.Length / durationMs);
        index = Math.Clamp(index, 0, _plainLines.Length - 1);
        return new LyricsFrame(_plainLines[index], string.Empty, string.Empty, false, true, 0);
    }

    return LyricsFrame.Empty;
}
```

注意：工程内所有 `new LyricsFrame(...)` 仅在本文件；改完后全量编译。

- [ ] **步骤 4：运行测试确认通过**

```powershell
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~LyricsSyncEngineTests"
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```powershell
git add src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs tests/ABLyrics.App.Tests/LyricsSyncEngineTests.cs
git commit -m "feat(lyrics): GetFrame 预读距下一句非空的毫秒数"
```

---

### 任务 2：`TrackInfoLayoutState` 改为预读 + 锁定（TDD）

**文件：**
- 修改：`src/ABLyrics.App/Services/TrackInfoLayoutState.cs`
- 新建：`tests/ABLyrics.App.Tests/TrackInfoLayoutStateTests.cs`

- [ ] **步骤 1：编写失败的 Layout 测试**

新建 `tests/ABLyrics.App.Tests/TrackInfoLayoutStateTests.cs`：

```csharp
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
```

- [ ] **步骤 2：运行测试确认失败**

```powershell
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~TrackInfoLayoutStateTests"
```

预期：编译失败（`Update` 仍是单参数）。

- [ ] **步骤 3：重写 `TrackInfoLayoutState`**

用下列完整实现替换 `TrackInfoLayoutState.cs`：

```csharp
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
```

- [ ] **步骤 4：运行测试确认通过**

```powershell
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~TrackInfoLayoutStateTests"
```

预期：全部 PASS。此时 `PlaybackCoordinator` 可能暂时无法编译（仍调用旧 `Update`）——若整仓 `dotnet build` 失败，继续任务 3，不要回退本任务测试。

- [ ] **步骤 5：Commit**（若 Coordinator 未改导致 build 红，可与任务 3 合并一次 commit；优先分开：先改 Coordinator 再一起 commit，见任务 3 步骤 5）

本任务若单独 commit 会留下红 build，因此**本任务不单独 commit**，与任务 3 同一 commit。

---

### 任务 3：接线 `PlaybackCoordinator`

**文件：**
- 修改：`src/ABLyrics.App/Services/PlaybackCoordinator.cs`（`UpdateLyricFrame` 附近）

- [ ] **步骤 1：更新 `UpdateLyricFrame`**

将：

```csharp
if (_trackInfoLayout.Update(shouldShowLyrics))
{
    ShouldCenterTrackInfo = _trackInfoLayout.ShouldCenter;
}
```

改为：

```csharp
if (_trackInfoLayout.Update(shouldShowLyrics, frame.MsUntilNextNonEmptyLine))
{
    ShouldCenterTrackInfo = _trackInfoLayout.ShouldCenter;
}
```

全文件搜索确认无其它 `_trackInfoLayout.Update(` 调用。

- [ ] **步骤 2：全量构建与测试**

```powershell
dotnet build
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj
```

预期：Build 成功；相关测试 PASS。

- [ ] **步骤 3：Commit（含任务 2 + 3）**

```powershell
git add src/ABLyrics.App/Services/TrackInfoLayoutState.cs src/ABLyrics.App/Services/PlaybackCoordinator.cs tests/ABLyrics.App.Tests/TrackInfoLayoutStateTests.cs
git commit -m "feat(layout): 曲名居中改为预读静默时长并锁定长间奏"
```

---

## 自检对照规格

| 规格条目 | 任务 |
|----------|------|
| 引擎预读 `MsUntilNextNonEmptyLine` | 任务 1 |
| 空行跳过、无下一句 → MaxValue、纯文本 → 0 | 任务 1 |
| Layout 去墙钟、≥3s 立刻居中、锁定、Reset | 任务 2 |
| Coordinator 接线 | 任务 3 |
| 不做时间戳间隔推断 / 不做 B2 提前居中 / 不改 UI | 全计划未包含 |

---

## 执行交接

计划保存到 `docs/superpowers/plans/2026-07-17-track-info-center-lookahead.md`。

**两种执行方式：**

1. **子代理驱动（推荐）** — 每任务一个子代理，任务间审查  
2. **内联执行** — 当前会话用 executing-plans，批量推进并设检查点  

选哪种？
