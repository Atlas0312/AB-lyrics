# SpotLyrics 三功能设计规格

日期：2026-07-09
状态：已批准

## 1. AppBar 右键菜单

### 需求
在 AppBar 窗口任意区域右键点击时，弹出与系统托盘图标相同的右键菜单。

### 实现

- 将 `App.xaml.cs` 中的 `BuildContextMenu()` 提取为公共静态方法 `App.BuildTrayContextMenu()`，返回 `Forms.ContextMenuStrip`
- `AppBarWindow` 订阅 `PreviewMouseRightButtonDown` 事件
- 在事件处理中弹出该共享菜单
- 注意：托盘菜单中部分项（如登录/退出）通过 `_loginMenuItem` 字段引用；提取时需将菜单构建纯逻辑分离，供托盘和 AppBar 共用

### 文件变更

| 文件 | 变更 |
|------|------|
| `App.xaml.cs` | 提取 `BuildTrayContextMenu()`，字段引用适配 |
| `AppBarWindow.xaml.cs` | 新增 `PreviewMouseRightButtonDown` 事件处理器 |

---

## 2. 双击歌曲信息打开 Spotify

### 需求
双击 TrackInfoPanel（曲名 · 歌手区域）时，通过 `spotify:track:{id}` URI 打开 Spotify 桌面客户端到当前歌曲。

### 实现

- 在 AppBarWindow 和 OverlayWindow 的 TrackInfoPanel 上注册 `MouseDoubleClick`
- 检查 `_coordinator` 是否有当前曲目 ID（`_loadedTrackId`），无则不触发
- 调用 `Process.Start("spotify:track:" + trackId)` 打开桌面客户端

### 文件变更

| 文件 | 变更 |
|------|------|
| `AppBarWindow.xaml` | TrackInfoPanel 加 `MouseLeftButtonDown` / `MouseDoubleClick` |
| `AppBarWindow.xaml.cs` | 新增双击事件处理器 |
| `OverlayWindow.xaml` | TrackInfoPanel 加双击处理 |
| `OverlayWindow.xaml.cs` | 新增双击事件处理器 |

---

## 3. 加载闪烁 + 状态文本

### 需求

任何歌词加载场景（歌曲切换、开始播放、用户手动选源）都触发视觉反馈：

- **有网络请求时**：窗口边框/背景轻微闪烁，状态文本显示当前尝试的源
- **有结果时**：直接展示歌词，StatusText 清空
- **无结果 + Fallback 时**：继续闪烁并切换源名称文本
- **最终无结果**：显示"无歌词"
- 仅网络源（LRCLIB、Netease）闪烁；Local 源瞬时读取不闪烁

### 状态转换

```
触发加载（歌曲切换 / 开始播放 / 选源）
  ↓
StatusText = "正在尝试 {source}…"
  ↓ (闪烁)
有结果 → StatusText = "" (清空) → 歌词展示 → 闪烁结束
  ↓ (闪烁)
无结果 → Fallback → StatusText = "正在尝试 {source}…" → 重复
  ↓ (闪烁)
最终无结果 → StatusText = "无歌词" → 闪烁结束
```

### 关键设计

#### LoadingFlash 事件

在 `PlaybackCoordinator` 中新增 `event Action? LoadingFlash`，在每次即将发送网络请求前触发。`LyricsHostLifecycle` 订阅该事件，执行闪烁动画。

#### 闪烁动画

- 将 ChromeBorder 的 BorderBrush 设为高亮色（`#7AD7FF`，Cyan），Opacity 从 1.0 → 0.0
- 使用 `DispatcherTimer` 在 150ms 内线性渐隐
- 动画结束后恢复 BorderBrush 为原值（`Transparent` 或 `#33FFFFFF`）

#### StatusText 更新

| 事件 | StatusText |
|------|-----------|
| 开始加载 | `"正在尝试 {source}…"`（source 是当前被尝试的源名） |
| 有歌词结果 | `""` 清空 |
| 最终无结果 | `"无歌词"` |

相关变更：`ReloadCurrentTrackAsync()` 中将 `StatusText` 的前置设置与 `LyricsSource` 的解耦，确保每次请求前先设好文本。

### 文件变更

| 文件 | 变更 |
|------|------|
| `PlaybackCoordinator.cs` | 新增 `LoadingFlash` 事件 + 加载前设置 StatusText 逻辑 |
| `LyricsHostLifecycle.cs` | 订阅 `LoadingFlash`，驱动 150ms 渐隐动画，统一窗口 |
| `LyricsStyleApplier.cs` | 新增 `ApplyFlash(Border)` 辅助方法 |
| `AppBarWindow.xaml.cs` | 传递 `ChromeBorder` 引用给 Lifecycle |
| `OverlayWindow.xaml.cs` | 传递 `ChromeBorder` 引用给 Lifecycle |

---

## 非本次修复：关闭 AppBar 导致退出的问题

已识别但不在本次范围内。初步根因排查方向：AppBar 的 AppBarController `OnClosed` → `Dispose` 事件流导致 `Application.Shutdown()` 被意外触发。

---

## 实现顺序

1. **功能 1**（右键菜单）— 最独立，不涉及其它功能
2. **功能 2**（双击打开 Spotify）— 独立，仅在 UI 层添加事件
3. **功能 3**（加载闪烁 + 状态文本）— 横跨 Coordinator → Lifecycle → UI 三层
