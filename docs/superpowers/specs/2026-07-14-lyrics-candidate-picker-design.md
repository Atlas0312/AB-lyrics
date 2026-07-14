---
title: 歌词库内版本选择/切换窗口（带覆盖项持久化）
date: 2026-07-14
status: proposed
---

# 歌词库内版本选择/切换窗口（带覆盖项持久化）

## 背景与目标

当前 `LyricsService.FetchLyricsAsync` 对每首曲目只返回一个 `LyricsResult`（兜底链 LRCLIB → Netease → Local）。当同一首歌存在多个版本（翻唱、live、再版、不同步精度）时，用户没有任何手段告诉 AppBar "这次我要用版本 X"。

同期内"本地库内错误或版本不同"已经因为命名模板演化 + 用户手工复制产生真实的多文件场景。LRCLIB 公共 API 已有 `/api/search` 返回最多 20 条候选——多版本数据源天然存在，只是 UI 没暴露。

本期目标：
- 提供一个"选版本窗口"，把当前曲目所有可获得的候选列出来
- 用户选定后，本次及以后该曲目都用所选版本（持久化）
- 主 AppBar/Overlay 的歌词显示按"覆盖项优先"规则生效

不在本期范围：
- 不做"按曲目次次重选"的菜单提示
- 不做 Netease 的多候选列表（其搜索接口返回数组，可单条按 duration 选，能力等同于单结果；本期仅作单候选源）
- 不暴露配置 UI 启用/禁用库（启用状态由启动时探活决定）
- 不迁移旧 `display-settings.json`、不动 `appsettings.json`

## 当前相关代码（事实基线）

- `src/ABLyrics.App/Services/Lyrics/LyricsService.cs:30-67` — `FetchLyricsAsync` 兜底链：LRCLIB → Netease → null
- `src/ABLyrics.App/Services/Lyrics/LrcLibClient.cs:20-47` — 现有 `GetAsync` 走 `/api/get`（精确单条）
- `src/ABLyrics.App/Services/Lyrics/LocalLyricsProvider.cs:59-81` — `FindFile` 单点匹配（FirstOrDefault）
- `src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs:18-40` — `Load(LyricsResult?)` 内部 `LrcParser.Parse`
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs:280-285` — `SetSourceAsync` 切歌词源会 reload；`_lyricsActiveSource` 仅内存
- `src/ABLyrics.App/Configuration/PlaybackSettings.cs` — `ActiveSource` 指**播放来源**（Spotify/...）不是歌词源
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs` / `OverlayWindow.xaml.cs` — 右键菜单项注册点
- `docs/AGENT_CONTEXT.md` — 配置/存储约定（`%LOCALAPPDATA%` 用于配置、`%APPDATA%` 用于内容文件）

## 设计

### 1. 概念模型

**统一候选 `LyricsCandidate`**：本地文件与在线源都映射为同一形态，让 UI 不关心"本地还是网络"。

```csharp
public sealed class LyricsCandidate
{
    public required string Source { get; init; }       // "Local" / "LRCLIB" / "Netease"
    public required string Label { get; init; }        // UI 显示标签
    public string? SyncedLyrics { get; init; }
    public string? PlainLyrics { get; init; }
    public int DurationMs { get; init; }
    public required CandidateOrigin Origin { get; init; }
    public bool IsAvailable { get; init; } = true;    // false → 圆点红、不可选
}

public abstract record CandidateOrigin
{
    public sealed record Local(string FilePath) : CandidateOrigin;
    public sealed record Lrclib(int LrclibId) : CandidateOrigin;
    public sealed record Netease(long NeteaseSongId) : CandidateOrigin;
}
```

### 2. 候选聚合（单库模式）

**用户可配置的"库"列表**来自 `LyricsService.AvailableSources`，构造时按 `MusicU` 是否非空动态加入 "Netease"，永远包含 "LRCLIB" 与 "Local"。**默认启用全部当前能跑的库**——不在 UI 暴露启用/禁用项；启动时通过探活过滤。

**`LyricsSearchService.SearchAsync(track, library)`** 行为（不截断候选）：
- `library == "Local"`：调 `LocalLyricsSearchProvider.SearchAsync(track)` 返回所有宽松匹配 `.lrc` 文件（Artist ∩ Name 子串同时含，大小写不敏感）+ 现有 `FindFile` 命中项；按"文件名短度升序"排序
- `library == "LRCLIB"`：调 `LrcLibClient.SearchAsync(track)` 走 `/api/search?track_name=&artist_name=&album_name=`，按 `|duration - track.DurationMs|` 升序取全部（≤20）
- `library == "Netease"`：调现有 `FetchFromNetEaseAsync` 的搜索部分（不取 lyric），取首条作为单候选；若 `MusicU` 空则返回空数组

不去重——本地和 LRCLIB 同歌词都出现让用户决定。

### 3. 启动时探活

**`LyricsSearchService.ProbeAsync()`** 返回 `Dictionary<string, bool>`：
- `LRCLIB`：`HttpClient` 调 `https://lrclib.net/api/search?q=test`，2xx 即 OK
- `Local`：`Directory.EnumerateFiles(_libraryPath, "*.lrc").Any()` 不抛即 OK（验证目录可读）
- `Netease`：`MusicU` 非空 + `SearchNew("test")` 不抛即 OK

**失败处理**：探活失败的库从 `AvailableSources` 里移除，托盘/右键菜单不可见；UI 候选下拉框也不显示。

### 4. 选版本窗口 `LyricsCandidatePickerWindow`

#### 4.1 窗口属性
- 单例 + Show/Hide（避免多次开窗）
- 关闭（X 或 [关闭]）→ `Hide()` 而非 `Close()`
- 进程退出时彻底 `Close()`
- 用户切歌时（`_loadedTrackId` 变）→ 自动 `Hide()`，对比失去意义

#### 4.2 布局（左侧候选列 + 右侧对比区）

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ LyricsCandidatePickerWindow                                          [X]    │
├─────────────────────────────────────────────────────────────────────────────┤
│ 库: [LRCLIB ▼]   当前曲目：周杰伦 — 晴天 (七里香)                          │
├──────────────────────┬──────────────────────────────────────────────────────┤
│ 候选列表（左侧）      │ 歌词对比区                                            │
│ ┌──────────────────┐ │ ┌────────────┐ ┌────────────┐ ┌────────────┐         │
│ │ ● LRCLIB         │ │ │  Prev line │ │  Prev line │ │  Prev line │         │
│ │   Album: 七里香  │ │ │ ▶Curr line │ │ ▶Curr line │ │ ▶Curr line │         │
│ │   4:29 · #1283   │ │ │  Next line │ │  Next line │ │  Next line │         │
│ │   ◉ 当前覆盖 ✕  │ │ └────────────┘ └────────────┘ └────────────┘         │
│ ├──────────────────┤ │                                                      │
│ │ ● Local          │ │  验证状态:  ✓ LRCLIB 可用                            │
│ │   周杰伦 - ...   │ │  [刷新]    [确认]    [关闭]                          │
│ │   ◯              │ │                                                      │
│ ├──────────────────┤ │                                                      │
│ │ ● Local          │ │                                                      │
│ │   翻唱-晴天.lrc  │ │                                                      │
│ │   ◯              │ │                                                      │
│ └──────────────────┘ │                                                      │
│   共 4 个候选，已选 0/3                                                      │
└──────────────────────┴──────────────────────────────────────────────────────┘
```

#### 4.3 库下拉框
- 内容 = `LyricsService.AvailableSources`（探活结果过滤后）
- 切换下拉框 → 重新 `SearchAsync(track, newLib)`，刷新候选列表（**不**触发探活、不影响圆点）
- 默认选中当前主歌词源（`_lyricsActiveSource`）——对齐用户预期

#### 4.4 候选列表交互
- 每行：圆点（绿/红）+ 来源（LRCLIB/Local/Netease）+ Label + RadioButton
- 圆点红的候选不可选（`IsEnabled=false`）：对应"探活失败的库下候选"——本期内库下拉框只列出可用库，故此场景实际不会发生，但作为防御性约束保留
- 默认全不勾选（RadioButton 行为）
- 单选语义：选中新候选 → 旧的自动取消
- 选中候选 → 在对比区显示该列 + 对比区列也可点击切换 RadioButton

#### 4.5 当前覆盖项的视觉
- 已有 override 的候选行 Label 末尾显示"当前覆盖"标记 + `✕` 清除按钮
- 圆点旁额外一个高亮（边框/底色），RadioButton 默认选中该候选
- `✕` 点击 → 移除 override → 该候选恢复普通显示

#### 4.6 歌词对比区
- 0 个选中时：显示占位"勾选候选以对比"
- 1-3 个选中：均匀并排显示
- 每列独立 `LyricsSyncEngine` 实例，订阅 `coordinator.ProgressMsChanged`
- 高亮样式沿用 `LyricsStyleApplier` 已有的 CurrentLine 字号/颜色规则

#### 4.7 按钮
- **[刷新]**：仅触发"对当前选中库重新搜索"——不重新探活
- **[确认]**：disabled 当无选中；启用时点击 → 把"当前选中的候选"应用到主 AppBar
- **[关闭]**：`Hide()`

#### 4.8 验证状态显示区
- 显示在按钮左侧
- 默认空（不打扰用户）
- 选中"不可用库"时：触发 `ProbeAsync(library)` → 显示"正在验证 XXX…" → 成功显示"✓ XXX 可用"并自动搜索该库 / 失败显示"✗ XXX 不可用：原因"

### 5. 覆盖项持久化

#### 5.1 存储位置
`%LOCALAPPDATA%/ABLyrics/lyrics-overrides.json`（与 `display-settings.json` 同目录、同约定——配置类数据）。

#### 5.2 键
`(Artist, Album, Name)` 三段规范化字符串，格式 `"{artist}||{album}||{name}"`：
- `string.Trim()` + 内部 `CollapseWhitespace()` + 小写化
- Album 允许空（→ `{artist}||||{name}`）

#### 5.3 持久化格式（JSON）
```json
{
  "version": 1,
  "overrides": {
    "周杰伦||七里香||晴天": {
      "kind": "Local",
      "filePath": "C:\\Users\\b\\AppData\\Roaming\\ABLyrics\\lyrics\\周杰伦 - 七里香 - 晴天.lrc"
    },
    "taylor swift||1989||22": {
      "kind": "Lrclib",
      "lrclibId": 1283
    }
  }
}
```

#### 5.4 语义
- **选中即写盘**：覆盖项永远写入 JSON，下次同曲播放生效
- **同键二次选中**：覆写旧值
- **清除入口**：仅通过覆盖项 Label 旁的 `✕` 按钮（见 §4.5）；不提供额外的"清除所有覆盖"或托盘菜单入口
- **同 Artist/Album/Name 多次出现**：三段键归一、不会重复

### 6. `PlaybackCoordinator` 集成

#### 6.1 新增事件
```csharp
public event Action<long>? ProgressMsChanged;
```
在 `UpdateLyricFrame(progressMs)` 内**额外**触发一次。窗口订阅它来驱动对比区引擎。

#### 6.2 新增字段
```csharp
private LyricsCandidate? _overrideCandidate;
```

#### 6.3 新增方法
```csharp
public void OpenCandidatePicker();   // 弹出窗口（无 TrackId 时显示空态）
public async Task ApplyCandidateAsync(LyricsCandidate candidate);
```
- `ApplyCandidateAsync`：解析 `candidate.Origin` → 调 `ILyricsService.FetchCandidateAsync(track, origin)` 直接喂主 `_syncEngine`（不走 LRCLIB 兜底）；同时**写盘**到 `LyricsOverrideStore`
- 加载完成后触发 `LoadingFlash`、已有 `PropertyChanged` 通知 AppBar/Overlay 刷新

#### 6.4 优先级规则（修改 `LoadTrackAsync`）
```
if (_overrideCandidate is { } candidate)
{
    var lyrics = await _lyricsService.FetchCandidateAsync(track, candidate.Origin);
    // 成功 → _syncEngine.Load + 状态文本；失败 → 移除该 override + 走兜底链
    if (lyrics is not null) { ... return; }
    else { _lyricsOverrideStore.Remove(TrackKey.From(track)); _overrideCandidate = null; }
}
// 兜底链不变：LRCLIB → Netease → Local
```

切歌时**不**清 `_overrideCandidate`——它按 trackKey 持久生效。

### 7. `LyricsSyncEngine` 扩展

新增重载避免重复 Parse：
```csharp
public void LoadParsed(LyricsData? data, string[] plainLines);
```
对比区的多个引擎实例只在候选变更时 Load 一次，后续 `GetFrame` 是纯查表。

### 8. 触发入口

- 托盘菜单："选择歌词版本…"
- AppBar 右键："选择歌词版本…"
- Overlay 右键："选择歌词版本…"

### 9. UI 线程模型

- 所有 UI 渲染：UI 线程（Dispatcher）
- `SearchAsync` / `ProbeAsync` / `ApplyCandidateAsync`：后台线程完成 → `Dispatcher.Invoke` 切回 UI 线程更新
- `LyricsSyncEngine` 纯字段读写、同步调用栈，**线程安全**，窗口从事件回调调用足够

## 测试

### `tests/ABLyrics.App.Tests/LyricsOverrideStoreTests.cs`

- `Load_FileMissing_ReturnsEmpty`
- `Load_CorruptJson_ReturnsEmpty`（+ DevExceptionReporter 触发）
- `SaveAndLoad_RoundTrips_LocalOrigin`
- `SaveAndLoad_RoundTrips_LrclibOrigin`
- `Save_ExistingKey_Replaces`
- `Remove_ExistingKey_Deletes`
- `Remove_NonExistingKey_NoOp`

### `tests/ABLyrics.App.Tests/TrackKeyTests.cs`

- `Normalize_TrimsAndCollapsesWhitespace`
- `Normalize_IsCaseInsensitive`
- `Normalize_EmptyAlbum_KeepsSeparator`（→ `artist||||name`）
- `SameTracks_ProduceSameKey`

### `tests/ABLyrics.App.Tests/LocalLyricsSearchProviderTests.cs`

- `SearchAsync_NoFiles_ReturnsEmpty`
- `SearchAsync_FindsArtistNameIntersection`
- `SearchAsync_IncludesExistingFindFileHits`
- `SearchAsync_OrdersByFileNameLength`

### `tests/ABLyrics.App.Tests/LrcLibClientSearchTests.cs`（用 `HttpMessageHandler` 替身）

- `SearchAsync_ParsesArrayResponse`
- `SearchAsync_5xx_ReturnsEmpty`
- `SearchAsync_NetworkTimeout_ReturnsEmpty`

### `tests/ABLyrics.App.Tests/LyricsSearchServiceTests.cs`

- `SearchAsync_LocalLibrary_CallsLocalProvider`
- `SearchAsync_LrclibLibrary_CallsLrcLibClient`
- `SearchAsync_UnknownLibrary_ReturnsEmpty`
- `ProbeAsync_LrclibReachable_True` / `_False`
- `ProbeAsync_LocalReachable_True` / `_False`
- `ProbeAsync_NeteaseNoMusicU_False`

### `tests/ABLyrics.App.Tests/PlaybackCoordinatorOverrideTests.cs`

- `LoadTrackAsync_HasOverride_AppliesOverrideBeforeLrclib`
- `LoadTrackAsync_NoOverride_FallsBackAsBefore`
- `LoadTrackAsync_OverrideStaleFile_RemovesOverrideAndFallsBack`
- `ApplyCandidateAsync_PersistsOverride`
- `ApplyCandidateAsync_FreshTrack_UsesOverrideImmediately`

### `tests/ABLyrics.App.Tests/LyricsSyncEngineTests.cs`（已有则扩展）

- `LoadParsed_EquivalentToLoadRaw`

## 代码组织（最终文件清单）

**新增**：
- `src/ABLyrics.App/Models/LyricsCandidate.cs`（含 `CandidateOrigin`）
- `src/ABLyrics.App/Services/Lyrics/ILyricsSearchService.cs`
- `src/ABLyrics.App/Services/Lyrics/LyricsSearchService.cs`
- `src/ABLyrics.App/Services/Lyrics/LocalLyricsSearchProvider.cs`
- `src/ABLyrics.App/Configuration/LyricsOverrideStore.cs`
- `src/ABLyrics.App/Configuration/TrackKey.cs`
- `src/ABLyrics.App/Views/LyricsCandidatePickerWindow.xaml(.cs)`
- `src/ABLyrics.App/Views/CandidateColumnView.xaml(.cs)`
- `tests/ABLyrics.App.Tests/LyricsOverrideStoreTests.cs`
- `tests/ABLyrics.App.Tests/TrackKeyTests.cs`
- `tests/ABLyrics.App.Tests/LocalLyricsSearchProviderTests.cs`
- `tests/ABLyrics.App.Tests/LrcLibClientSearchTests.cs`
- `tests/ABLyrics.App.Tests/LyricsSearchServiceTests.cs`
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorOverrideTests.cs`

**修改**：
- `src/ABLyrics.App/Services/Lyrics/LrcLibClient.cs`（加 `SearchAsync`，扩展现有 client）
- `src/ABLyrics.App/Services/Lyrics/LocalLyricsProvider.cs`（保持不变；新搜索由 `LocalLyricsSearchProvider` 承担）
- `src/ABLyrics.App/Services/Lyrics/LyricsService.cs`（加 `FetchCandidateAsync`、加 `IsAvailableAsync`）
- `src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs`（加 `LoadParsed` 重载）
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs`（`ProgressMsChanged` 事件、`_overrideCandidate` 字段、`OpenCandidatePicker`、`ApplyCandidateAsync`、`LoadTrackAsync` 优先级插入）
- `src/ABLyrics.App/App.xaml.cs`（`OnStartup` 加探活 + 注册 `ILyricsSearchService`）
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs`（右键菜单加"选择歌词版本…"）
- `src/ABLyrics.App/Views/OverlayWindow.xaml.cs`（右键菜单加"选择歌词版本…"）
- `src/ABLyrics.App/App.xaml.cs`（托盘菜单加"选择歌词版本…"）
- `docs/AGENT_CONTEXT.md`（更新：覆盖项存储路径、优先级规则、选版本窗口入口、新增服务）

## 验收清单

1. `dotnet test ABLyrics.sln` 全绿（含新增约 25 条 + 现有全部）
2. `dotnet build ABLyrics.sln` 零警告（项目当前基线零警告）
3. 手动场景：
   - 启动软件、播放周杰伦《晴天》
   - 托盘右键"选择歌词版本…" → 窗口弹出、LRCLIB 库默认、列出 LRCLIB 多候选 + Local 候选
   - 切到 Local 库 → 列表刷新为本地 .lrc 文件
   - 选中一个 LRCLIB 候选 → 对比区显示该列 + 跟随播放进度高亮
   - 切回主窗口看 AppBar → 仍是默认歌词（未确认）
   - 点 [确认] → AppBar 切到选中版本的歌词
   - 重启软件、再次播放《晴天》→ AppBar 直接显示选中版本
4. 覆盖项场景：
   - 编辑 `lyrics-overrides.json` 把文件路径改错 → 启动软件播放该曲 → 自动回退到 LRCLIB 且 override 被移除
5. 探活场景：
   - 断网启动 → LRCLIB 圆点红、不可选；Local 圆点绿、可选
6. 库下拉框场景：
   - 没配 MUSIC_U → 下拉框只有 LRCLIB + Local
7. 切歌场景：选版本窗口打开状态下切换曲目 → 窗口自动 Hide
8. 跨源场景：托盘切到非 Spotify 播放源、播放新曲 → 选版本窗口对该曲目正常工作