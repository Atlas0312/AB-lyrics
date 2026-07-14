# ABLyrics

Windows 桌面歌词小组件，为 Spotify 提供**任务栏矮条（AppBar）** 与**悬浮歌词（Overlay）** 实时同步展示。

## 界面展示

### 任务栏矮条（AppBar）

```
┌─────────────────────────────────────────────────────────┐
│  曲名 · 歌手            当前歌词行...         🔄 LRCLIB ▼ │
└─────────────────────────────────────────────────────────┘
```

- 注册为 Windows 系统 AppBar，停靠于屏幕底部
- 最大化窗口自动避让
- 无歌词时居中显示歌曲信息，有歌词时切换到左曲名 + 中歌词布局

### 悬浮歌词（Overlay）

```
┌──────────────────────────────────────────────────────────┐
│          曲名 · 歌手           🔄 LRCLIB ▼              │
│                   当前歌词行...                           │
└──────────────────────────────────────────────────────────┘
```

- 圆角悬浮窗口，支持拖拽
- 可放置于任意位置

## 技术栈

- **.NET 8 + WPF**（WinExe）
- **Windows Forms**（系统托盘图标）
- **Spotify Web API**（Authorization Code + PKCE）
- **[Lyricify.Lyrics.Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)**（歌词解析）
- **DPAPI 加密**（本地 Token 存储）

## 歌词来源

| 来源 | 说明 | 是否需要配置 |
|------|------|-------------|
| **LRCLIB** | 主源，通过 [lrclib.net](https://lrclib.net) API 获取 | 无需配置 |
| **Netease** | 网易云音乐 Fallback，中文歌更全 | 需提供 `MUSIC_U` Cookie |
| **Local** | 本地 `.lrc` 文件 | 可选，默认路径 `%APPDATA%/ABLyrics/lyrics/` |

### 歌词源切换

点击窗口上的来源标签（如 `LRCLIB ▼`），弹出菜单即可实时切换歌词源。

- **Local 源**的特殊行为：点击后若检测到本地无匹配文件，会自动弹出文件选择对话框，选中 `.lrc` 文件后自动拷贝到歌词库中。
- **Local 子菜单**：`导入歌词文件…`（随时手动导入，无曲目时置灰）、`打开歌词库文件夹`（用资源管理器打开 `%APPDATA%/ABLyrics/lyrics`，方便管理文件）。
- **本地弹窗开关**：`设置… → 歌词`，关闭"缺失时弹出选择对话框"后切到 Local 不再自动弹窗（仍可手动从 AppBar 菜单导入）。
- **刷新按钮** `🔄`：强制用当前源重新加载当前曲目的歌词。

## 配置

编辑 `src/ABLyrics.App/appsettings.json`：

```json
{
  "Spotify": {
    "ClientId": "your_client_id",
    "RedirectUri": "http://127.0.0.1:48721/callback",
    "Scopes": ["user-read-currently-playing", "user-read-playback-state"]
  },
  "NetEase": {
    "MusicU": ""
  },
  "Lyrics": {
    "PrimaryProvider": "LRCLIB",
    "FallbackProvider": "Netease",
    "UserAgent": "AB-lyrics/0.1.0",
    "LocalPath": ""
  },
  "Ui": {
    "DefaultMode": "AppBar",
    "AppBarHeight": 56
  }
}
```

| 字段 | 说明 |
|------|------|
| `Spotify.ClientId` | Spotify Developer Dashboard 的 Client ID |
| `Spotify.RedirectUri` | 本地 OAuth 回调地址 |
| `Spotify.Scopes` | 请求的 Spotify API 权限范围 |
| `NetEase.MusicU` | 网易云 `MUSIC_U` Cookie（中文歌 Fallback 需要） |
| `Lyrics.LocalPath` | 本地歌词库路径（空则使用默认 `%APPDATA%/ABLyrics/lyrics/`） |
| `Lyrics.UserAgent` | 请求 LRCLIB API 的 User-Agent |
| `Ui.DefaultMode` | 启动默认模式（`AppBar` / `Overlay`） |
| `Ui.AppBarHeight` | 默认矮条高度（px） |

也可以通过环境变量 `SPOTIFY_CLIENT_ID` 覆盖 Client ID。

## 使用步骤

1. 在 [Spotify Dashboard](https://developer.spotify.com/dashboard) 创建应用
2. 添加 Redirect URI：`http://127.0.0.1:48721/callback`
3. 将 Client ID 填入 `appsettings.json`
4. 运行应用 → 系统托盘出现图标 → 右键 **设置… → 播放来源 → 连接 Spotify**
5. 在 Spotify 客户端播放音乐即可看到同步歌词
6. 右键托盘图标可管理窗口显示、切换来源

### 样式设置

托盘右键菜单 → **样式设置…**，支持：

| 设置项 | 范围 | 默认值 |
|--------|------|--------|
| 字体 | 系统任意已安装字体 | Microsoft YaHei UI |
| 歌词字号 | 12–36 px | 18 px |
| 歌曲信息字号 | 8–24 px | 12 px |
| 字距 | -2–20 px | 0 px |
| 框高度 | 28–120 px | 56 px |
| 底色 | RGB Hex | `#101010` |
| 背景透明度 | 0–100% | 80% |
| 四周边距 | 左/上/右/下 分别设定 | 16/4/16/4 |
| 显示行数 | 1 行 / 2 行 | 1 行 |

歌词与歌曲信息（曲名 / 歌手）使用独立的字号设置，互不影响。预览区会同时显示中文与英文歌词示例，便于切换字体时验证混合语言渲染效果。

所有样式自动持久化至 `%LOCALAPPDATA%/ABLyrics/display-settings.json`。

## 项目结构

```
ABLyrics/
├── src/
│   └── ABLyrics.App/              # 主应用（WPF）
│       ├── App.xaml[.cs]            # 应用入口、托盘图标、窗口管理
│       ├── appsettings.json         # 应用配置
│       ├── Configuration/           # 配置加载与持久化
│       │   ├── AppSettings.cs       # 配置模型
│       │   ├── ConfigurationLoader.cs
│       │   ├── DisplaySettingsStore.cs
│       │   └── DisplayStyleSettings.cs
│       ├── Models/                  # 数据模型
│       │   ├── LyricsResult.cs
│       │   ├── PlaybackState.cs
│       │   └── TrackInfo.cs
│       ├── Native/                  # Win32 P/Invoke
│       │   ├── AppBarController.cs  # AppBar 注册与位置管理
│       │   └── AppBarNative.cs      # Shell32 / User32 DllImport
│       ├── Services/
│       │   ├── Lyrics/              # 歌词获取与同步
│       │   │   ├── ILyricsService.cs
│       │   │   ├── LyricsService.cs         # 多源路由
│       │   │   ├── LyricsSyncEngine.cs      # 歌词时间轴引擎
│       │   │   ├── LrcLibClient.cs          # LRCLIB API 客户端
│       │   │   └── LocalLyricsProvider.cs   # 本地 .lrc 文件管理
│       │   ├── Spotify/             # Spotify API
│       │   │   ├── ISpotifyAuthService.cs
│       │   │   ├── ISpotifyPlaybackService.cs
│       │   │   ├── SpotifyApiClient.cs      # API 调用（含 429 退避）
│       │   │   ├── SpotifyAuthService.cs    # PKCE 授权 + Token 刷新
│       │   │   ├── SpotifyPkce.cs           # PKCE 工具
│       │   │   ├── SpotifyPlaybackService.cs # 当前播放状态
│       │   │   └── SpotifyTokenStore.cs     # DPAPI 加密存储
│       │   ├── DisplaySettingsService.cs
│       │   ├── PlaybackCoordinator.cs       # 核心协调器
│       │   └── TrackInfoLayoutState.cs
│       └── Views/                   # WPF 窗口
│           ├── AppBarWindow.xaml[.cs]
│           ├── OverlayWindow.xaml[.cs]
│           ├── StyleSettingsWindow.xaml[.cs]
│           ├── LetterSpacingHelper.cs       # 字距附加属性
│           ├── LyricsHostLifecycle.cs        # 窗口生命周期
│           ├── LyricsLayoutController.cs     # 布局切换
│           ├── LyricsStyleApplier.cs         # 样式应用
│           └── SystemFontCatalog.cs          # 系统字体枚举
├── tools/
│   └── LrcProbe/                   # 歌词探测辅助工具
└── docs/superpowers/               # 设计文档
```

## 开发

```bash
cd E:\MyProjects\ABLyrics
dotnet build
dotnet run --project src/ABLyrics.App
```

项目要求 .NET 8 SDK，WPF 仅在 Windows 上支持。

## 许可

本项目代码 MIT。[Lyricify.Lyrics.Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper) 遵循 Apache-2.0。
