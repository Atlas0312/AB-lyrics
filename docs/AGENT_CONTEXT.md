# ABLyrics Agent Context

项目约定、模块入口与常见任务指针。改代码前先来这里查一遍。

## 1. 项目身份

- 名称：ABLyrics（前身 SpotLyrics）
- 平台：Windows 桌面 / .NET 8 / WPF + WPF-UI 3.1.1
- 主入口：`src/ABLyrics.App/App.xaml.cs`（`App : Application`）
- 测试：`tests/ABLyrics.App.Tests/`（xUnit；`internal` 通过 `InternalsVisibleTo` 可见）

## 2. 构建与运行

```bash
dotnet build ABLyrics.sln
dotnet test  ABLyrics.sln
```

约定：

- 中文注释写业务逻辑；技术术语保留英文。
- `internal sealed class` 用于同程序集辅助；`public sealed class` 用于跨层服务。
- 异常在 DEBUG 走 `DevExceptionReporter.Show(ex, title)`；不弹窗。
- WPF 跨线程更新统一 `Dispatcher.Invoke` / `Dispatcher.BeginInvoke`。

## 3. 模块清单

| 层 | 路径 | 责任 |
|----|------|------|
| 播放来源抽象 | `Services/Playback/` | `IPlaybackSource` + 注册表 |
| 播放协调 | `Services/PlaybackCoordinator.cs` | 轮询、字幕帧、覆盖项优先级 |
| 歌词主服务 | `Services/Lyrics/LyricsService.cs` | 兜底链：LRCLIB → Netease → Local |
| 歌词搜索聚合 | `Services/Lyrics/LyricsSearchService.cs` | 多候选搜索 + 启动时探活 |
| 歌词本地搜索 | `Services/Lyrics/LocalLyricsSearchProvider.cs` | 本地库宽松匹配 |
| LRCLIB 客户端 | `Services/Lyrics/LrcLibClient.cs` | `/api/get` 精确 + `/api/search` 多候选 |
| 歌词同步引擎 | `Services/Lyrics/LyricsSyncEngine.cs` | `Load / LoadParsed / GetFrame` |
| 覆盖项持久化 | `Configuration/LyricsOverrideStore.cs` | trackKey → CandidateOrigin JSON |
| 曲目规范化键 | `Configuration/TrackKey.cs` | `{artist}||{album}||{name}` |
| 候选 UI 主窗口 | `Views/LyricsCandidatePickerWindow.xaml` | 单例 + Show/Hide |
| 候选对比列 | `Views/CandidateColumnView.xaml` | 单列：prev / curr / next 三行 |
| AppBar / Overlay | `Views/AppBarWindow.xaml(.cs)` / `Views/OverlayWindow.xaml(.cs)` | 主显示窗 |

## 4. 配置 / 存储路径

| 路径 | 内容 | 维护者 | 格式 |
|------|------|--------|------|
| `%LOCALAPPDATA%/ABLyrics/display-settings.json` | 外观样式（字体/颜色） | `DisplaySettingsStore` | JSON |
| `%LOCALAPPDATA%/ABLyrics/lyrics-behavior.json` | 歌词行为（缺失提示） | `LyricsBehaviorStore` | JSON |
| `%LOCALAPPDATA%/ABLyrics/lyrics-overrides.json` | 歌词版本覆盖项 | `LyricsOverrideStore` | JSON (WriteIndented, 含 version + overrides) |
| `%LOCALAPPDATA%/ABLyrics/playback-state.json` | 上次播放源 | `PlaybackStateStore` | JSON |
| `%APPDATA%/ABLyrics/lyrics/*.lrc` | 用户本地歌词库 | `LocalLyricsProvider` / `LocalLyricsSearchProvider` | LRC / 文本 |
| 应用根 `appsettings.json` | Spotify ClientId / Netease MusicU / LRCLIB UA | `ConfigurationLoader` | JSON |

## 5. 关键流程

### 5.1 启动

`App.OnStartup` → 加载 `appsettings.json` → 注册播放源 → 构造 `PlaybackCoordinator`（注入 `LyricsSearchService` + `LyricsOverrideStore`） → 创建托盘 + AppBar → 后台 `ProbeAsync` 探活。

### 5.2 轮询与字幕帧

`PlaybackCoordinator.PollAsync` 每 800 ms 取一次 `PlaybackState`；新曲目触发 `LoadTrackAsync`，否则只更新 `UpdateLyricFrame(progressMs)`，同时触发 `ProgressMsChanged` 事件（候选选择窗口用）。

### 5.3 歌词拉取

`LyricsService.FetchLyricsAsync` 兜底顺序：

- **覆盖项（来自 `lyrics-overrides.json`）凌驾其上**
- LRCLIB → Netease → Local

切歌时（`_loadedTrackId` 变）→ `PlaybackCoordinator.LoadTrackAsync` 先查 `_overrides`（启动时一次性从 JSON 加载到内存）→ 若命中则 `ILyricsService.FetchCandidateAsync(track, origin)` 直接喂主 `_syncEngine`；失败则移除该项并回退到 LRCLIB 兜底。

### 5.4 候选选择窗口

入口：

- 托盘右键「选择歌词版本…」
- AppBar 右键（共享托盘菜单）
- Overlay 右键

行为：单例 + Show/Hide；切换库下拉框触发 `LyricsSearchService.SearchAsync` 刷新候选列表；用户勾选 + 确认 → `PlaybackCoordinator.ApplyCandidateAsync(candidate)` 写盘 + 应用。

## 6. 测试约定

- xUnit `[Fact]` + `Assert.Equal/Null/Same/Contains/Empty/True/NotEmpty`
- 文件命名：`<Subject>Tests.cs`，与被测代码同名同层
- 不引入 Moq / NSubstitute；用 `sealed class Fake...` 手写替身
- `PlaybackCoordinator` / `LyricsService` / `LocalLyricsProvider` 是 `internal` 测试可见（通过 `InternalsVisibleTo`）
- 反射调用私有方法仅在确有需要时（`LoadTrackAsync` / `PollAsync` 这类）

## 7. 当前 WIP

歌词版本选择器窗口（2026-07-14 起）：

- 新增 `LyricsCandidate` / `CandidateOrigin` / `TrackKey` / `LyricsOverrideStore` / `ILyricsSearchService` / `LyricsSearchService` / `LocalLyricsSearchProvider`
- `LrcLibClient` 加 `SearchAsync` + `ProbeAsync`；`LocalLyricsProvider` 暴露 `BuildFileName` 为 `internal static`
- `LyricsService` 加 `FetchCandidateAsync`；`LyricsSyncEngine` 加 `LoadParsed`
- `PlaybackCoordinator` 加覆盖项优先级 + `ProgressMsChanged`/`CandidatePickerRequested` 事件 + `ApplyCandidateAsync`/`OpenCandidatePicker`
- 新窗口 `Views/LyricsCandidatePickerWindow.xaml(.cs)` + `Views/CandidateColumnView.xaml(.cs)`
- 托盘/AppBar/Overlay 右键菜单各加 "选择歌词版本…" 项

## 8. 常见任务入口

| 任务 | 路径 | 备注 |
|------|------|------|
| 改歌词主服务兜底链 | `Services/Lyrics/LyricsService.cs` | `FetchLyricsAsync` 顺序：覆盖项 > LRCLIB > Netease > Local |
| 改本地歌词库路径 | `Configuration/AppSettings.cs` (`Lyrics.LocalPath`) | 默认 `%APPDATA%/ABLyrics/lyrics` |
| 改播放源 | `Services/Playback/IPlaybackSource.cs` + `PlaybackSourceRegistry` | 新源要 `Register` 进 `App.OnStartup` |
| 改外观样式 | `Services/DisplaySettingsService.cs` + `Views/LyricsStyleApplier.cs` | 持久化在 `display-settings.json` |
| 修改歌词候选聚合 | `Services/Lyrics/LyricsSearchService.cs` + `ILyricsSearchService` | 同步 `AvailableSources` 与 `ProbeAsync` 字典 |
| 加新覆盖项来源类型 | `Models/LyricsCandidate.cs` (`CandidateOrigin`) + `Configuration/LyricsOverrideStore.cs` 的 `SerializeOrigin`/`DeserializeOrigin` | 三处 switch 都得加 |

## 9. 风险与注意

- `PlaybackCoordinator.PollAsync` 在 UI 线程触发的事件必须 `Dispatcher.BeginInvoke` 后再访问 WPF 元素。
- `LrcLibClient` 是 `internal sealed`，测试通过 `internal LyricsSearchService(AppSettings, LrcLibClient)` 构造替身。
- WPF-UI 3.x：`FluentWindow` / `Card` / `NavigationView` / `ToggleSwitch` 均在 `http://schemas.lepo.co/wpfui/2022/xaml`。
- 不要修改 `LyricsSyncEngine.Load` 的语义；`LoadParsed` 是给"已解析"路径用的，调用方负责 Parse。