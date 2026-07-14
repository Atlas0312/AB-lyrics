---
title: Local 歌词源导入：缺失时弹窗选择 + 主动入口 + 打开歌词库文件夹 + 行为开关
date: 2026-07-14
status: proposed
---

# Local 歌词源导入：缺失时弹窗选择 + 主动入口 + 打开歌词库文件夹 + 行为开关

## 背景与目标

ABLyrics 通过 `LocalLyricsProvider` 从 `%APPDATA%/ABLyrics/lyrics`（或 `AppSettings.Lyrics.LocalPath`
配置的目录）读取 `<Artist> - <Name>.lrc` 歌词文件。本地库未命中当前曲目时，
`LyricsService.FetchFromSourceAsync(track, "Local")` 返回 `null`，`PlaybackCoordinator` 状态显示
"暂无歌词"——用户此时无法自助补齐本地歌词。

本期目标：

1. 切换到 Local 源且本地库未命中时，弹一次文件选择对话框让用户自行指定 `.lrc`/`.txt` 文件，
   选中后把文件复制进歌词库并按既有命名规则 `<Artist> - <Name>.lrc` 重命名（自动覆盖已有），
   完成后立即重新加载当前曲目以显示歌词。
2. 在 `AppBarWindow` 的歌词源菜单中，"Local" 下新增子项 **"导入歌词文件…"**，允许用户随时
   主动触发同样的导入流程（无论当前歌词源是否为 Local）。
3. 在 "Local" 子菜单中再新增 **"打开歌词库文件夹"**，调用 `explorer.exe` 直接打开歌词库目录，
   方便用户管理文件。
4. 被动弹窗仅对同一首曲目提示一次（按 session 内 TrackId 去重）。
5. **在主设置窗口新增 "歌词" 页**，提供开关 **"本地歌词缺失时弹出选择对话框"**（默认开启），
   让用户能完全关掉被动弹窗。关闭后**不再弹窗**，但去重集合**继续累计**——重新开启时已经
   询问过的曲目不会重新弹出，避免一次性弹窗轰炸。

## 当前相关代码（事实基线）

- `src/ABLyrics.App/Services/Lyrics/LocalLyricsProvider.cs:9-23` — `LibraryPath` 已是 `public`，
  目录不存在时构造函数会 `Directory.CreateDirectory(_libraryPath)`。
- `src/ABLyrics.App/Services/Lyrics/LocalLyricsProvider.cs:49-55` — `ImportAsync(sourcePath, track)`
  已存在并满足"复制 + 按 `{Artist} - {Name}.lrc` 重命名 + 覆盖"的需求，未被任何 UI 调用。
- `src/ABLyrics.App/Services/Lyrics/LyricsService.cs:69-84` — `FetchFromSourceAsync("Local")`
  走 `LocalLyricsProvider.GetAsync`。
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs:273-319` — `SetSourceAsync` 切换后调用
  `ReloadCurrentTrackAsync`；`ReloadCurrentTrackAsync` 是触发"缺失时弹窗"的关键节点。
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs:286-319` — `ReloadCurrentTrackAsync` 当前
  仅设置 `_lyricsActiveSource` 并刷新；找不到歌词时 `StatusText = "暂无歌词"`。
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs:438-452` — `ClearLyrics` / `ClearLyricLines`
  是切换曲目/切源时清空 UI 的统一入口。
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs:17, 26-32, 87-100` — `_sourceMenu`
  是 `Forms.ContextMenuStrip`；`OnSourceTagClick` 当前用平铺 `ToolStripMenuItem{Tag=source}` 构建。
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs:44-51` — `FakeLyricsService`
  可被复用，按曲目返回 null 或非 null。
- `docs/superpowers/specs/2026-07-13-playback-source-design.md` — 上期设计文档，参考其结构与
  代码组织风格。
- `src/ABLyrics.App/Configuration/DisplaySettingsStore.cs` — 现有设置持久化风格：
  `%LOCALAPPDATA%/ABLyrics/display-settings.json`、JSON 容错、`defaults.Clone()` 回填。
- `src/ABLyrics.App/Services/DisplaySettingsService.cs` — 现有设置服务风格：
  `Current` 内存对象 + `Update(settings)` 写盘 + `SettingsChanged` 事件。
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml:267-324` — 现有"播放来源"页的 `ui:Card`
  + `ui:ToggleSwitch` 风格尚未使用；本设计引入新的"歌词"页，沿用同样的 `ui:Card` 容器。

## 设计

### 1. 架构与新类型

新增 4 个类型，修改若干既有类型；不引入 DI 容器、不修改 `ILyricsService` 接口。

**新增类型**：

- **`Configuration/LyricsBehaviorSettings.cs`**（新）
  ```csharp
  public sealed class LyricsBehaviorSettings
  {
      public bool PromptForLocalLyricsOnMissing { get; set; } = true;
      public LyricsBehaviorSettings Clone() => new()
      {
          PromptForLocalLyricsOnMissing = PromptForLocalLyricsOnMissing,
      };
  }
  ```
  沿用 `DisplayStyleSettings.Clone()` 的浅拷贝风格；字段未来增加时再扩展。

- **`Configuration/LyricsBehaviorStore.cs`**（新，参考 `DisplaySettingsStore`）
  - 文件路径：`%LOCALAPPDATA%/ABLyrics/lyrics-behavior.json`（独立于 `display-settings.json`，
    与"歌词行为"语义域一致）。
  - `Load(LyricsBehaviorSettings defaults)`：文件不存在或解析失败时回退到 `defaults.Clone()`。
  - `Save(LyricsBehaviorSettings settings)`：写入 JSON（`WriteIndented = true`）。
  - **不引入环境变量覆盖**——这是纯用户偏好，配置层一次定型。

- **`Services/LyricsBehaviorService.cs`**（新，参考 `DisplaySettingsService`）
  ```csharp
  public sealed class LyricsBehaviorService
  {
      public event EventHandler<LyricsBehaviorSettings>? SettingsChanged;
      public LyricsBehaviorSettings Current { get; private set; }

      public LyricsBehaviorService(LyricsBehaviorSettings defaults)
      {
          Current = LyricsBehaviorStore.Load(defaults);
      }

      public void Update(LyricsBehaviorSettings settings)
      {
          Current = settings.Clone();
          LyricsBehaviorStore.Save(Current);
          SettingsChanged?.Invoke(this, Current);
      }
  }
  ```

- **`Views/LyricsSettingsPage.cs`**（不存在，仅作设计占位说明）—— "歌词"页是
  `StyleSettingsWindow.xaml` 中的 `ScrollViewer x:Name="LyricsPage"`，与现有
  `PlaybackSourcePage` 对齐（见 §2 的"UI 细节"）。不需要单独文件。

**修改的类型**：

- **`Services/PlaybackCoordinator.cs`**
  - 构造函数新增参数 `LyricsBehaviorService lyricsBehavior`，存为 `_lyricsBehavior` 字段；
    订阅 `_lyricsBehavior.SettingsChanged` 以在运行期同步开关状态。
  - 新增 `public event Action<TrackInfo>? LocalLyricsMissing;`。
  - 新增 `private readonly HashSet<string> _localPromptedTrackIds = new();` 与
    `private readonly object _localPromptLock = new();`。
  - 在 `ReloadCurrentTrackAsync` 中：当 `_lyricsActiveSource == "Local"` 且
    `await _lyricsService.FetchFromSourceAsync(currentTrack, "Local")` 返回 `null` 时，
    在 `lock (_localPromptLock)` 内 `_localPromptedTrackIds.Add(currentTrack.Id)` 返回
    `true` 才 `LocalLyricsMissing?.Invoke(currentTrack)`，
    **且必须** `_lyricsBehavior.Current.PromptForLocalLyricsOnMissing == true`。
    去重集合在开关关闭期间**继续累计**——避免重新开启后突然弹出大量对话框。
  - `ClearLyrics` 中清空 `_localPromptedTrackIds`（切换曲目/切源时允许再次提示）。
  - `SetSourceAsync` 切到非 Local 源时**不清空**集合——避免"切到 LRCLIB → 切回 Local"时
    同一首曲目又被弹一次。

- **`Views/StyleSettingsWindow.xaml` + `.xaml.cs`**
  - 新增 `LyricsPage` ScrollViewer（详见 §4）。
  - 左侧导航新增 `<ui:NavigationViewItem Content="歌词" TargetPageTag="lyrics" Click="OnMenuItemClick" />`。
  - `OnMenuItemClick` 增加 `LyricsPage.Visibility` 切换。
  - 新增 `LoadFrom(LyricsBehaviorSettings)` / `ReadLyricsBehaviorForm()`：
    - 读取唯一的 `ui:ToggleSwitch`（见 §4）→ `PromptForLocalLyricsOnMissing`。
  - 在构造函数 / `WireEvents` 中：订阅 `ToggleSwitch.Toggled` → `ApplyLyricsBehaviorSettings()`
    → `_lyricsBehavior.Update(...)`。

- **`Views/StyleSettingsTabRouter.cs`**
  - 新增 `LyricsTag = "lyrics"`，`KnownTags` 加一项（变 7），`Resolve` 映射到 `"LyricsPage"`。

- **`Views/AppBarWindow.xaml.cs`**
  - 构造函数：`_coordinator.LocalLyricsMissing += OnLocalLyricsMissing;`
    （事件触发时通过 `Dispatcher.BeginInvoke` 调度到 UI 线程）。
  - 新增 `private readonly LocalLyricsProvider _localProvider;` 字段，
    通过 `App.GetLocalLyricsProvider()` 获取（详见 `App.xaml.cs` 修改）。
  - 改造 `OnSourceTagClick` 构建的菜单结构：
    - `LRCLIB`、`Netease` 沿用现有平铺项。
    - `Local` 顶级项，`Tag = "Local"`，点击仍走现有 `ItemClicked` 路径切源；
      其 `DropDownItems` 追加两个子项：
      - **导入歌词文件…**：`Enabled = _coordinator.GetCurrentTrackId() is not null`；无曲目置灰。
        点击调用 `PromptForLocalLyricsAsync(currentTrack)`。
      - **打开歌词库文件夹**：始终启用。点击调用 `OpenLocalLyricsLibrary()`。
  - 新增 `private void OnLocalLyricsMissing(TrackInfo track)`：
    - 在事件触发线程（非 UI 线程）通过 `Dispatcher.BeginInvoke(() => PromptForLocalLyricsAsync(track))`
      调度到 UI 线程后再弹窗，避免后台线程触碰 WPF 控件。
  - 新增 `private async Task PromptForLocalLyricsAsync(TrackInfo track)`：
    1. 构造 `Microsoft.Win32.OpenFileDialog`（详见 §2）。
    2. `ShowDialog() == true` → `await _localProvider.ImportAsync(path, track)`。
    3. 若当前 `_coordinator.ActiveSource != "Local"`：先 `await _coordinator.SetSourceAsync("Local")`
       （这会自动重载）；否则直接 `await _coordinator.ForceReloadAsync()`。
    4. 用户取消：no-op。
    5. 任何异常：`DevExceptionReporter.Show(ex, "导入本地歌词失败")`，不重载。
  - 新增 `private void OpenLocalLyricsLibrary()`：
    `Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_localProvider.LibraryPath}\"")
    { UseShellExecute = true })`；异常走 `DevExceptionReporter`。

- **`App.xaml.cs`**
  - 新增 `public static LyricsBehaviorService LyricsBehavior { get; private set; } = null!;`
    初始化于 `OnStartup`。
  - 新增 `public static LocalLyricsProvider GetLocalLyricsProvider()`：
    返回新建 `LocalLyricsProvider(Settings)`，与现有静态访问器风格一致
    （`GetPlaybackSourceRegistry` / `GetTrayContextMenu`）。
  - `PlaybackCoordinator` 构造函数新增第 4 个参数 `LyricsBehavior`。
  - `ShowAppBar()` 创建 `AppBarWindow` 时无需改动注入（已通过 `App.GetLocalLyricsProvider()`
    自取）。

- **`Services/Lyrics/LocalLyricsProvider.cs`**
  - **无功能改动**。`ImportAsync` 已满足本期需求；`LibraryPath` 已 public。

### 2. UI 细节

**文件选择对话框**（`Microsoft.Win32.OpenFileDialog`）：

| 属性 | 值 |
|---|---|
| `Title` | `$"为 {track.Artist} - {track.Name} 选择本地歌词"` |
| `Filter` | `"LRC 歌词 (*.lrc)\|*.lrc\|文本歌词 (*.txt)\|*.txt\|所有文件 (*.*)\|*.*"` |
| `FilterIndex` | `1` |
| `InitialDirectory` | `Environment.GetFolderPath(SpecialFolder.MyDocuments)` |
| `CheckFileExists` | `true` |
| `Multiselect` | `false` |

不记忆上次路径（YAGNI）。

**菜单结构**（运行时 `Forms.ContextMenuStrip`）：

```
LRCLIB              (选中时打勾，点击切源)
Netease             (选中时打勾，点击切源)
Local               (选中时打勾，点击切源)
  ├─ 导入歌词文件…  (无曲目时置灰)
  └─ 打开歌词库文件夹
```

**主设置窗口 "歌词" 页**（新增 `ScrollViewer x:Name="LyricsPage"`，位于
`StyleSettingsWindow.xaml` 中与其它 6 个页并列）：

```xml
<ui:Card Padding="20">
    <StackPanel>
        <TextBlock Text="本地歌词" FontWeight="SemiBold" Margin="0,0,0,8" />
        <DockPanel>
            <ui:ToggleSwitch x:Name="PromptLocalMissingSwitch"
                             DockPanel.Dock="Right"
                             Content="缺失时弹出选择对话框" />
        </DockPanel>
        <TextBlock TextWrapping="Wrap" Opacity="0.7" FontSize="12" Margin="0,8,0,0"
                   Text="切换到本地歌词源时，若在歌词库找不到当前曲目，会弹出文件选择框。关闭后不再提示，需要时可使用顶部 AppBar 菜单「Local ▸ 导入歌词文件…」。" />
    </StackPanel>
</ui:Card>
```

`PromptLocalMissingSwitch.IsOn` ↔ `LyricsBehaviorSettings.PromptForLocalLyricsOnMissing`。
切换时立即 `Update(...)` 写盘 + 触发 `SettingsChanged`（`PlaybackCoordinator` 订阅，
下一次 `ReloadCurrentTrackAsync` 即生效）。

**状态文案**：
- 弹窗前：`_coordinator.StatusText = "等待选择本地歌词…"`（仅作过渡文案，重载完成后会被覆盖）。
- 导入成功：依赖 `ForceReloadAsync` / `SetSourceAsync` 的既有逻辑显示歌词行与正常 `LyricsSource`。
- 用户取消：状态不变（沿用现有"暂无歌词"）。
- 打开文件夹失败：`DevExceptionReporter.Show`，不打扰 UI。

### 3. 数据流

**被动触发（自动弹窗）**：

```
PollAsync → 切换曲目 / 切源
  └── PlaybackCoordinator.ReloadCurrentTrackAsync
        ├── _lyricsActiveSource = "Local"
        ├── lyrics = _lyricsService.FetchFromSourceAsync(track, "Local")
        │              └── LocalLyricsProvider.GetAsync
        │                      └── Directory.EnumerateFiles → 未命中 → null
        ├── if lyrics is null
        │     && _lyricsBehavior.Current.PromptForLocalLyricsOnMissing  ← 新增
        │     && _localPromptedTrackIds.Add(track.Id):  ← 仍累计
        │       LocalLyricsMissing?.Invoke(track)
        │       └── AppBarWindow.OnLocalLyricsMissing  (后台线程)
        │             └── Dispatcher.BeginInvoke(() => PromptForLocalLyricsAsync(track))
        │                   ├── OpenFileDialog.ShowDialog()
        │                   ├── if OK:
        │                   │     await _localProvider.ImportAsync(path, track)
        │                   │     await _coordinator.ForceReloadAsync()
        │                   └── if Cancel: no-op
        └── StatusText = "暂无歌词" (若用户取消且未导入)
```

**主动触发（菜单"导入歌词文件…"）**：

```
AppBarWindow.OnSourceTagClick → 用户点击 "Local ▸ 导入歌词文件…"
  └── PromptForLocalLyricsAsync(currentTrack)
        ├── OpenFileDialog.ShowDialog()
        ├── if OK:
        │     await _localProvider.ImportAsync(path, currentTrack)
        │     if _coordinator.ActiveSource != "Local":
        │           await _coordinator.SetSourceAsync("Local")  // 内含 ReloadCurrentTrackAsync
        │     else:
        │           await _coordinator.ForceReloadAsync()
        └── if Cancel: no-op
```

**打开歌词库文件夹**：

```
AppBarWindow.OnSourceTagClick → 用户点击 "Local ▸ 打开歌词库文件夹"
  └── Process.Start(new ProcessStartInfo("explorer.exe",
        $"\"{_localProvider.LibraryPath}\"") { UseShellExecute = true })
```

### 4. 生命周期与线程

- `OpenFileDialog.ShowDialog()` 在 UI 线程同步阻塞调用方，但 `AppBarWindow.OnLocalLyricsMissing`
  处于 `PollAsync` 的后台线程上下文，必须先 `Dispatcher.BeginInvoke` 调度。
- `_localPromptedTrackIds` 只在 `PlaybackCoordinator` 内部访问：调用方可能是后台线程
  (`PollAsync`) 与 UI 线程（`SetSourceAsync` 在用户点击时由 UI 线程触发）。用
  `lock (_localPromptLock)` 保护 `Add` / `Clear`；`ClearLyrics` 同样加锁。
- `_localProvider` 仅在 UI 线程被 `AppBarWindow` 使用，无并发问题。

### 5. 测试

新增 `tests/ABLyrics.App.Tests/LocalLyricsProviderTests.cs`（基于 `Path.Combine(Path.GetTempPath(),
Path.GetRandomFileName())` 的临时目录，每个测试结束清理）：

- `ImportAsync_CopiesFileWithExpectedName`
- `ImportAsync_OverwritesExisting`
- `ImportAsync_SanitizesInvalidChars`
- `GetAsync_ReturnsNullWhenMissing`
- `GetAsync_ReadsContentWhenPresent`

新增 `tests/ABLyrics.App.Tests/PlaybackCoordinatorLocalMissingPromptTests.cs`（复用现有的
`FakeLyricsService` 模式，`FetchFromSourceAsync` 按曲目返回 null 或结果）：

- `LocalSource_MissingLyrics_RaisesEventOnce`
- `LocalSource_MissingLyrics_SameTrackReloadedTwice_RaisesOnlyOnce`
- `LocalSource_MissingLyrics_DifferentTracks_RaisesForEach`
- `LocalSource_ClearLyrics_AllowsRetrigger`
- `NonLocalSource_MissingLyrics_DoesNotRaise`
- `LocalSource_PresentLyrics_DoesNotRaise`
- `LocalSource_MissingLyrics_BehaviorOff_DoesNotRaise` ← 新增
- `LocalSource_MissingLyrics_BehaviorOff_StillTracksInDedupSet` ← 新增：
  - 先关闭开关 → 弹窗被抑制，但 `_localPromptedTrackIds` 仍累计；
  - 然后开启开关 → 同一曲目**不再**触发弹窗。
- `LocalSource_MissingLyrics_BehaviorOnAfterOff_RaisesForNewTracksOnly` ← 新增：
  - 关→开转换后，**新曲目**仍能弹窗。

新增 `tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs`（参考 `PlaybackStateStoreTests`
的临时文件风格）：

- `Load_FileMissing_ReturnsDefaults`
- `Save_Then_Load_RoundTrip`
- `Load_CorruptJson_ReturnsDefaults`

更新 `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`：`KnownTags.Count == 7`，
并断言 `Resolve("lyrics") == "LyricsPage"`。

不修改现有 `PlaybackCoordinatorActiveSourceTests.cs`（通过构造函数新增的"必填"参数无法兼容；
改为在 `BuildCoordinator` helper 内增加一行 `new LyricsBehaviorService(new LyricsBehaviorSettings())` 即可，
改动量极小但需要更新测试文件）。如果选择兼容旧测试，则把 `LyricsBehaviorService` 改成可空
（`= null`），内部 null-check 时回退到默认行为——权衡后**建议直接更新现有测试**，避免引入
二义性"null 意味着默认"路径。

### 6. 错误边界

- `_localProvider.ImportAsync` 抛（源文件被占用、磁盘满）→ `DevExceptionReporter.Show`，
  不重载歌词。
- `Process.Start("explorer.exe", ...)` 抛 → `DevExceptionReporter.Show`。
- 用户取消对话框 → 无副作用；`_localPromptedTrackIds` 已记录，本次 session 内不再弹。
- 切换曲目时 `ClearLyrics` 清空去重集合，行为对用户透明（换歌后再提示是合理的）。
- `LyricsBehaviorStore.Load` 失败（文件损坏）→ 回退到默认值（开关 = true），不崩溃。
- 设置窗口关闭未点"确定" → 切回原开关值；`Update` 只在"应用"或"确定"路径调用。
  这与现有 `ApplySettings` 行为一致——开关的 `Toggled` 事件绑到 `_isLoading` 守卫后的
  `ApplyLyricsBehaviorSettings`，仅在用户主动切换时写盘。

### 7. 代码组织（最终文件清单）

修改：

- `src/ABLyrics.App/Services/PlaybackCoordinator.cs`（新增可选参数 + 事件 + 去重）
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml`（新增 LyricsPage）
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`（路由 + 加载/保存）
- `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs`（LyricsTag）
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs`（菜单 + 弹窗 + 打开文件夹）
- `src/ABLyrics.App/App.xaml.cs`（`LyricsBehavior` 静态访问器 + `GetLocalLyricsProvider` +
  `PlaybackCoordinator` 构造参数）
- `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`（`KnownTags.Count == 7` + `Resolve("lyrics")`）
- `README.md`（"使用步骤"段：说明 Local 缺失弹窗、菜单入口、设置开关）

新增：

- `src/ABLyrics.App/Configuration/LyricsBehaviorSettings.cs`
- `src/ABLyrics.App/Configuration/LyricsBehaviorStore.cs`
- `src/ABLyrics.App/Services/LyricsBehaviorService.cs`
- `tests/ABLyrics.App.Tests/LocalLyricsProviderTests.cs`
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorLocalMissingPromptTests.cs`
- `tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs`
- `docs/superpowers/specs/2026-07-14-local-lyrics-import-design.md`（本文档）

## 验收清单

1. 切换到 Local 源、本地库未命中当前曲目 → 自动弹一次文件选择框（同一首曲目本 session 内仅一次）。
2. 选择 `.lrc` 或 `.txt` → 文件被复制到 `%APPDATA%/ABLyrics/lyrics/{Artist} - {Name}.lrc`（覆盖已有）
   → 当前曲目歌词自动显示。
3. 用户取消对话框 → 状态保持"暂无歌词"，无副作用。
4. 同一曲目重复触发（手动刷新、重新进入）→ 不再弹窗；切换到其他曲目后再次未命中则重新弹。
5. 在 Local 子菜单点击"导入歌词文件…" → 同样行为；当前非 Local 时导入完成自动切到 Local 并显示。
6. 在 Local 子菜单点击"打开歌词库文件夹" → 资源管理器打开歌词库目录。
7. "导入歌词文件…" 在无曲目时置灰，"打开歌词库文件夹" 始终可用。
8. 主设置窗口左侧导航新增"歌词"页，含开关 **"本地歌词缺失时弹出选择对话框"**，默认开启。
9. 关闭开关 → 不再自动弹窗；去重集合继续累计；切换曲目后行为不变。
10. 重新开启开关 → 已记录曲目不再弹出（避免"突然弹一堆"），新曲目仍能弹窗。
11. 设置立即写盘到 `%LOCALAPPDATA%/ABLyrics/lyrics-behavior.json`，重启后保留。
12. `StyleSettingsTabRouter.KnownTags.Count == 7`；`Resolve("lyrics") == "LyricsPage"`。
13. `dotnet build` 与 `dotnet test` 全部通过；现有流程不破坏。