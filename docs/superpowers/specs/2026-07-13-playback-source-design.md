---
title: 播放进度来源抽象与设置面板改造
date: 2026-07-13
status: proposed
---

# 播放进度来源抽象与设置面板改造

## 背景与目标

ABLyrics 当前仅支持从 Spotify Web API 获取“当前正在播放”信息。登录入口
（`登录 Spotify` / `退出登录`）位于系统托盘右键菜单，由 `App.xaml.cs` 的
`BuildTrayContextMenu` 创建（`App.xaml.cs:117-120, 142`），通过
`Coordinator.LoginAsync` / `Coordinator.Logout` 调用 `SpotifyAuthService`。

本期目标：

1. 将 Spotify 登录从托盘菜单移除，统一改为在设置面板内完成。
2. 把“获取当前播放进度”抽象为可插拔的“播放进度来源”接口（`IPlaybackSource`），
   Spotify 作为首个实现；后续“设备本地播放”等来源按相同接口接入。
3. 调整设置面板结构，新增独立“播放来源”页，承载来源选择与登录状态。
4. 将“活动播放来源”持久化到配置文件与运行时状态文件，App 启动时按它恢复。

明确不在本期实现：Spotify 之外的任何 `IPlaybackSource` 具体类（例如设备本地播放）。
抽象与发现机制按 `dont-scope` 原则预留即可。

## 当前相关代码（事实基线）

- `src/ABLyrics.App/App.xaml.cs:117-120` — 托盘菜单创建“登录 Spotify”。
- `src/ABLyrics.App/App.xaml.cs:180-208` — `OnLoginLogoutClick` 登录/退登逻辑。
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs:10-403` —
  `PlaybackCoordinator` 直接依赖 `ISpotifyAuthService` + `ISpotifyPlaybackService`，
  在 `PollAsync` 中持续轮询（`PlaybackCoordinator.cs:245-293`）。
- `src/ABLyrics.App/Services/Spotify/ISpotifyAuthService.cs` /
  `ISpotifyPlaybackService.cs` — 现有 Spotify 抽象。
- `src/ABLyrics.App/Services/Spotify/SpotifyAuthService.cs` —
  PKCE 授权 + token 刷新（`SpotifyAuthService.cs:31-209`）。
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml:21-44` — 现有
  `NavigationView` + 五个 `NavigationViewItem`。
- `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs:20-58` — 标签页纯逻辑路由。
- `src/ABLyrics.App/Configuration/AppSettings.cs` — 当前四个节点（`Spotify`、
  `NetEase`、`Lyrics`、`Ui`）。
- `src/ABLyrics.App/Configuration/DisplaySettingsStore.cs:6-41` —
  样式持久化参考实现（同风格写 `PlaybackStateStore`）。
- `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs:1-51` — 现有 xUnit 风格。

## 设计

### 1. 架构与新类型

新增 `src/ABLyrics.App/Services/Playback/` 目录：

- `IPlaybackSource`（接口，公共）
  - `string Id { get; }` — 稳定标识，写入 `Playback.ActiveSource`。
  - `string DisplayName { get; }` — UI 名称（如 "Spotify"）。
  - `bool IsAvailable { get; }` — 当前是否可选；Spotify 在无 `ClientId` 时为 `false`。
  - `Task ConnectAsync(CancellationToken)` — 建立授权/会话，幂等。
  - `void Disconnect()` — 撤销授权/停止。
  - `Task<PlaybackState?> GetSnapshotAsync(CancellationToken)` — 取当前播放快照。
  - `event Action<PlaybackState?>? SnapshotChanged` — 可选推送；Spotify 本期不实现。

- `SpotifyPlaybackSource`（公共，组合现有 `ISpotifyAuthService` + `ISpotifyPlaybackService`）
  - `Id = "Spotify"`、`DisplayName = "Spotify"`。
  - `IsAvailable = !string.IsNullOrWhiteSpace(_settings.ClientId)`。
  - `ConnectAsync` = `EnsureAuthenticatedAsync` → 未认证时 `LoginInteractiveAsync`。
  - `Disconnect` = `_authService.Logout`。
  - `GetSnapshotAsync` = `_playbackService.GetCurrentPlaybackAsync`。

- `PlaybackSourceRegistry`（公共，静态注册中心）
  - `IReadOnlyList<IPlaybackSource> All { get; }`
  - `IPlaybackSource? Get(string id)`
  - `void Register(IPlaybackSource source)` — 由 `App.OnStartup` 在 `Coordinator`
    创建前调用，匹配现有“手动 new”风格，不引入 DI 容器。

修改的类型：

- `Configuration/AppSettings.cs` — 新增 `PlaybackSettings` 属性。
- `Configuration/ConfigurationLoader.cs` — 读 `Playback.ActiveSource`，缺失时回填 `"Spotify"`。
- `Configuration/PlaybackSettings.cs`（新）— `string ActiveSource = "Spotify"`。
- `Configuration/PlaybackStateStore.cs`（新）— 持久化
  `%LOCALAPPDATA%/ABLyrics/playback-state.json`，参考 `DisplaySettingsStore` 容错风格。
- `Services/PlaybackCoordinator.cs` —
  - 构造函数改为接收 `PlaybackSourceRegistry` + 初始 `ActiveSourceId`，
    不再直接持有 `ISpotifyAuthService` / `ISpotifyPlaybackService`。
  - 公开 `IPlaybackSource? ActiveSource`、`bool IsSourceConnected`、
    `event Action? SourceStateChanged`。
  - 增 `Task SetActiveSourceAsync(string id, bool restoreOnly = false)`。
  - 保留 `LoginAsync` / `Logout` 仅为 `internal` 薄壳，规避一次性改动所有调用方。
- `App.xaml.cs` — 删除“登录/退出登录”菜单项；将“样式设置…”改名为“设置…”；
  `OnLoginLogoutClick` 移除；`UpdateMenuStates` / `UpdateTooltip` 改为订阅
  `SourceStateChanged`；`TryRestoreSessionAsync` 委托给 `SetActiveSourceAsync(..., restoreOnly: true)`。
- `Views/StyleSettingsWindow.xaml` — 新增 `ScrollViewer x:Name="PlaybackSourcePage"`；
  左侧导航新增 `<ui:NavigationViewItem Content="播放来源" TargetPageTag="playback-source" Click="OnMenuItemClick" />`。
- `Views/StyleSettingsWindow.xaml.cs` — `OnMenuItemClick` 路由 `PlaybackSourcePage.Visibility`。
- `Views/StyleSettingsTabRouter.cs` — 新增 `PlaybackSourceTag = "playback-source"`、
  `Resolve` 映射到 `"PlaybackSourcePage"`、`KnownTags` 同步为 6 项。

### 2. 状态、设置面板新页与持久化

- `StatusText` 通用文案（替换现有硬编码 "Spotify…"）：
  - 未配置来源 → `"未配置播放来源"`。
  - 已选来源但未连接 → `"请先连接 {DisplayName}"`。
  - 已连接但未在播放 → `"未在播放"`。
  - 错误 → 来源抛出的 `ex.Message`。
- 设置面板“播放来源”页（`PlaybackSourcePage`）：
  - 顶部卡：当前活动来源 + 状态 + “连接 / 断开”按钮。
  - Spotify 卡（仅当 `ActiveSource is SpotifyPlaybackSource`）：
    - 状态摘要 / “重新连接” / “退出登录”。
    - `!IsAvailable` 时显示 “请先在 appsettings.json 中配置 Spotify.ClientId”，按钮置灰。
  - 可用来源列表（ListView + 卡片风格）：
    - 名称 / 状态 / 是否活动 / “使用此来源” 操作。
- 持久化：
  - `appsettings.json` 新增 `Playback: { "ActiveSource": "Spotify" }`。
  - `%LOCALAPPDATA%/ABLyrics/playback-state.json` 仅存 `ActiveSource`，
    启动时优先于 `appsettings.json`。
  - `SetActiveSourceAsync` 成功后立即写 `playback-state.json`。
- 错误处理：所有面板动作 try/catch，失败时 `DevExceptionReporter.Show` 并在卡片下方展示简短错误；
  `ConnectAsync` 失败时 `StatusText` 反映原因，不弹窗（与现有非入侵风格一致）。

### 3. 数据流与生命周期

- `App.OnStartup`：
  1. `ConfigurationLoader.Load()` → 读 `Playback.ActiveSource`。
  2. `PlaybackStateStore.Load()` 覆盖。
  3. 实例化 `SpotifyAuthService` / `SpotifyPlaybackService` / `LyricsService`。
  4. `PlaybackSourceRegistry.Register(new SpotifyPlaybackSource(...))`。
  5. `new PlaybackCoordinator(registry, activeId, lyricsService, displaySettings)`。
  6. `CreateTrayIcon`（不创建登录项）。
  7. `_ = TryRestoreSessionAsync()` → `Coordinator.SetActiveSourceAsync(activeId, restoreOnly: true)`：
     - 不可用 → `Stop()` + `StatusText = "来源不可用：{DisplayName}"`。
     - 已认证 → `ConnectAsync`（仅 refresh token）+ `Start()`。
     - 未认证 → `Stop()` + `StatusText = "请先连接 {DisplayName}"`。
- 设置面板交互：登录/退登/切换都直接面向 `Coordinator.ActiveSource` 或 `SetActiveSourceAsync`。
- 运行期轮询（`PollAsync`）：
  - 前置：`ActiveSource is not null && IsSourceConnected`，否则 `Stop()`。
  - 取快照 → 走现有 `LoadTrackAsync` 流程。
- 切源（`SetActiveSourceAsync`）：
  1. 同 Id 且已连接 → 直接返回。
  2. 停旧源 `_timer.Stop()` + `ClearLyrics`。
  3. 解析新源；空 → `StatusText = "未知播放来源"` + `ActiveSource = null`。
  4. `ConnectAsync`；失败 → 状态文案，不切换。
  5. `_timer.Start()` + `SourceStateChanged` + 写 `playback-state.json`。
- 生命周期：`OnExit` 沿用现有 `Coordinator.Dispose`；`Registry` 不持有资源。
- **行为变化点**：从设置面板退出登录时，AppBar / Overlay 不再自动关闭（与原托盘退登不同）；
  仅清空歌词显示。该行为变化已在设计 3/4 节登记为确认项。

### 4. 测试、错误边界与代码组织

单元测试（`tests/ABLyrics.App.Tests/`，沿用现有 xUnit 风格）：

- `PlaybackSourceTabRouterTests.cs` — 覆盖 `"playback-source" → "PlaybackSourcePage"`；
  验证 `KnownTags` 从 5 → 6。
- `PlaybackSourceRegistryTests.cs` — 注册 / 解析 / 未知 Id 返回 null。
- `PlaybackCoordinatorActiveSourceTests.cs` — 在不引入 WPF 的前提下：
  - 不可用源时 `IsRunning == false` 且 `StatusText` 提示。
  - 可用且 `ConnectAsync` 成功后 `GetSnapshotAsync` 被调用。
  - 通过 fake `IPlaybackSource` 与 fake `ILyricsService` 验证切源行为。

错误边界：

- `IPlaybackSource.ConnectAsync` 抛 → `Coordinator` 捕获 → `StatusText` 反映；`ActiveSource`
  保持上一个有效值。
- `GetSnapshotAsync` 抛 → 沿用 `StatusText = ex.Message`。
- token 失效（`RefreshAsync` 内 `Logout`）→ `IsSourceConnected` 变 false → `Stop()`。
- `ConfigurationLoader` 读取失败时回退到默认 `ActiveSource = "Spotify"`，App 不应崩溃。

代码组织（最终文件清单）：

- `Services/Playback/IPlaybackSource.cs`
- `Services/Playback/SpotifyPlaybackSource.cs`
- `Services/Playback/PlaybackSourceRegistry.cs`
- `Services/PlaybackCoordinator.cs`（修改）
- `Configuration/PlaybackSettings.cs`
- `Configuration/PlaybackStateStore.cs`
- `Configuration/AppSettings.cs`（修改）
- `Configuration/ConfigurationLoader.cs`（修改）
- `App.xaml.cs`（修改）
- `Views/StyleSettingsWindow.xaml`（修改）
- `Views/StyleSettingsWindow.xaml.cs`（修改）
- `Views/StyleSettingsTabRouter.cs`（修改）
- `tests/ABLyrics.App.Tests/PlaybackSourceTabRouterTests.cs`
- `tests/ABLyrics.App.Tests/PlaybackSourceRegistryTests.cs`
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs`
- `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`（更新 `KnownTags.Count` 测试）
- `README.md`（更新“使用步骤”段，把“登录 Spotify”改为“设置 → 播放来源 → 连接 Spotify”）

## 验收清单

1. 托盘菜单不再包含“登录 Spotify / 退出登录”项；“样式设置…”已重命名为“设置…”。
2. 设置面板新增“播放来源”页，可在此完成 Spotify 登录、退登、查看状态。
3. 状态文案（`StatusText`、托盘 Tooltip）不再硬编码 “Spotify”，统一为来源无关。
4. 未登录 / 来源不可用时 `PlaybackCoordinator.IsRunning == false`。
5. `appsettings.json.Playback.ActiveSource` 写入并由 `playback-state.json` 恢复。
6. `StyleSettingsTabRouter.KnownTags` 为 6 项；新增 `playback-source` 单元测试通过。
7. `dotnet build` 与 `dotnet test` 全部通过；现有歌词来源（LRCLIB / Netease / Local）流程不变。
8. 退出登录时不再自动关闭 AppBar / Overlay（已登记行为变化）。
