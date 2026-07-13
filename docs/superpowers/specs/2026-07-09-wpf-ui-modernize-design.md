# SpotLyrics WPF 现代化（Wpf.Ui）

**日期：** 2026-07-09
**状态：** 已批准
**关联 Issue：** 无
**分支：** `feature/wpf-ui-modernize`
**Worktree：** `E:\MyProjects\SpotLyrics\.worktrees\wpf-ui-modernize`

## 目标

用 WPF + [Wpf.Ui](https://github.com/lepoco/wpfui)（MIT，活跃维护）替换三个窗口的样式/控件，让 SpotLyrics 拥有 Fluent 设计风格——Mica 背景、现代控件、清晰排版。不迁移到 WinUI 3。

**用户原始诉求：** "现在的文字、右键菜单、设置页面都太丑了。"

**非目标：**

- 不迁移到 WinUI 3 / Windows App SDK
- 不改 Spotify / Lyrics / 配置 / 业务服务层（这些与 UI 框架解耦）
- 不改 AppBar P/Invoke 实现
- 不重做托盘菜单（v1 维持 WinForms `ContextMenuStrip` 现状；后续 spec 再处理）
- 不引入主题切换（亮/暗）、动画过渡、Snackbar 错误提示、图标库
- 不重构 `LyricsStyleApplier` 内部结构

## 架构

### 新增 NuGet 依赖

```xml
<PackageReference Include="Wpf.Ui" Version="3.0.x" />
```

具体版本在实现时确认（取 Wpf.Ui 当前稳定版，要求支持 .NET 8 + WPF）。

### 新增 XAML 命名空间

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

仅在需要使用 Wpf.Ui 控件的窗口添加。

### 不变项

- `AppBarController` + `AppBarNative`：Win32 P/Invoke 继续工作
- `LyricsStyleApplier` / `LetterSpacingHelper` / `SystemFontCatalog`：public API 不变
- `PlaybackCoordinator` / `Services/Spotify/*` / `Services/Lyrics/*`：零改动
- `DisplaySettingsService` / `Configuration/*`：零改动
- 托盘图标 (`NotifyIcon`) + `ContextMenuStrip`：维持 WinForms 形态

## 组件改造

### 1. `StyleSettingsWindow`（大改）

**目标：** 这是用户主要痛点。改为 Fluent Window + NavigationView + Card 布局。

**改动：**

- `<Window>` → `<ui:FluentWindow>`（带标准 chrome 标题栏，符合"普通设置窗口"心智）
- 加 `WindowBackdrop="Mica"` 开 Mica 背景
- 现有 `<Grid>` 内容改为：
  - 左侧 `<ui:NavigationView>` 导航项（外观 / 歌词源 / 关于）
  - 右侧 `<ui:NavigationView.Content>` 内放各页面（用 `ui:Card` 包裹设置组）
- 控件替换：
  - `CheckBox` → `<ui:ToggleSwitch>`（如果有启用/禁用类设置）
  - 手动数字 `TextBox` → `<ui:NumberBox>`（字号、字距、框高度等）
  - `TextBox` → `<ui:TextBox>`（Fluent 风格）
  - `Button` → `<ui:Button>`（圆角、Fluent 风格）
  - `ColorPicker`/`ComboBox` → 视情况用 `ui:ColorPicker` / `ui:ComboBox` 或保留默认
- 字体：使用 `ui:TextBlock.FontTypography="BodyStrong"` 等预设（如果 Wpf.Ui 提供），否则手工设置 `FontSize`/`FontFamily`/`LineHeight`

**风险：**

- `FluentWindow` 的 chrome 与现有窗口尺寸/位置保存逻辑需协调（`WindowStartupLocation` 等）
- `NavigationView` 切换内容时，现有设置保存/读取逻辑（`DisplaySettingsStore`）不能丢

### 2. `AppBarWindow`（小改）

**目标：** 任务栏矮条加 Mica 背景，更新字体为 Fluent 默认。

**改动：**

- **不用** `FluentWindow`（会加标准 chrome 按钮，破坏 AppBar 体验）—— 继续用 `<Window WindowStyle="None" AllowsTransparency="True">`
- 加 `ui:WindowBackdrop="Mica"` 附加属性开 Mica
- 现有 `TextBlock` 替换为 `ui:TextBlock`（如果行为兼容），或保持 `TextBlock` 但用 Fluent 字体配置
- `LetterSpacingHelper` 验证在 `ui:TextBlock` 上仍生效；失效则改用其他字距方案

**风险：**

- Mica 与 `AllowsTransparency="True"` 是否冲突？需在 Windows 11 22H2+ 验证。Windows 10 系统需降级为纯色背景（用附加属性 conditional trigger 或运行时检测 OS 版本）
- AppBar 位置计算（`AppBarController`）依赖 `Window` 实际尺寸，Mica chrome 变化不影响 HWND 位置，但需视觉确认

### 3. `OverlayWindow`（小改）

**目标：** 悬浮歌词加 Mica 背景。

**改动：**

- 保持 `<Window WindowStyle="None" AllowsTransparency="True">`（需要圆角 + 自定义拖拽）
- 加 `ui:WindowBackdrop="Mica"`
- 圆角 + 半透明与 Mica 叠加效果需视觉调优（可能需要调低 `Window.Opacity` 让 Mica 透出）

**风险：**

- 同 Mica + `AllowsTransparency` 兼容性
- 现有拖拽逻辑（`MouseLeftButtonDown` → `DragMove()`）需保留

### 4. 托盘右键菜单（v1 不动）

**v1 决定：** WinForms `ContextMenuStrip` 维持现状。视觉上仍像 WinForms 风格菜单。

**理由：**

- 强制改 `ToolStripProfessionalRenderer` 颜色只能"接近" Fluent，但细节（hover 动画、focus ring、间距）难做对
- 完整替换为 WPF ContextMenu 需要换托盘库（如 `H.NotifyIcon.Wpf`），扩大改动范围
- 用户的三个"丑点"是 3 个主窗口，托盘菜单排在后面

**后续 spec（如需要）：** 单独开 `feature/wpf-tray-menu` 专题处理。

## 数据流

不变。

- `DisplaySettingsService` 仍是 source of truth
- `LyricsStyleApplier` 调用 WPF DependencyProperty setter —— Wpf.Ui 控件继承自 WPF 控件，setter 名字不变，继续工作
- `PlaybackCoordinator` 事件流不变

唯一可能改动：`LyricsStyleApplier` 中如果有硬编码的 `System.Windows.Controls.TextBlock` 类型引用，改为 `TextBlock` 基类或检查 Wpf.Ui 的 `ui:TextBlock` 派生关系。

## 错误处理

不变。

- 各 Service 自管错误（已有）
- v1 不引入 `ui:Snackbar` 做错误提示（出错了照样托盘通知 + 日志）

## 测试

业务层单测：不受影响（不涉及）。

UI 层：本项目无自动化 UI 测试基础设施。**只做人工冒烟测试：**

1. 启动应用 → 验证 Mica 背景显示（Windows 11 22H2+）
2. 三个窗口均能正常显示
3. AppBar 仍能正确注册到任务栏、最大化窗口避让
4. Overlay 仍能拖拽
5. StyleSettings 中所有设置项能保存/读取
6. 切回 Windows 10 风格（如果有 OS 检测）—— 验证降级路径

## 实施顺序（TDD-friendly）

1. 加 Wpf.Ui 依赖到 `SpotLyrics.App.csproj`，build 通过
2. 改造 `StyleSettingsWindow`（最大改动，先做）
3. 改造 `AppBarWindow`
4. 改造 `OverlayWindow`
5. 在 Windows 11 + Windows 10 各做一次人工冒烟
6. 更新 `README.md` 中"样式设置"小节（如有需要），记录新的视觉行为

## 风险登记

| 风险 | 等级 | 缓解 |
|------|------|------|
| Mica + `AllowsTransparency` 冲突 | 中 | Windows 11 22H2+ 通常可用，10 降级为纯色；用附加属性 conditional trigger 或运行时 OS 版本检测 |
| `FluentWindow` 影响 AppBar 定位 | 高 | AppBar/Overlay **不用** `FluentWindow`，已规避 |
| Wpf.Ui 与项目现有 WPF 版本/SDK 冲突 | 低 | 选 Wpf.Ui 3.x（支持 .NET 8）；如冲突退到 2.x |
| `LetterSpacingHelper` 在 `ui:TextBlock` 失效 | 中 | 验证步骤放在 AppBar/Overlay 改造之前；失效时改用 `ui:FontTypography` 或文本宽度微调 |
| 字体引用变化导致文字宽度变化（影响布局） | 中 | 视觉冒烟必查；如 `TextBlock` 宽高绑定 `ActualWidth/ActualHeight` 需手动重新调 |
| 旧用户配置（`display-settings.json`）兼容 | 低 | `DisplaySettingsStore` schema 不变，自动兼容 |

## 范围边界回顾

✅ **本次做：** 三个窗口 XAML 改造（Settings 大改、AppBar/Overlay 小改）、Mica 背景、Fluent 字体配置、`ui:ToggleSwitch` / `ui:NumberBox` 替换

❌ **本次不做：** 托盘菜单 Fluent 化、主题切换（亮/暗）、动画/转场、Snackbar 错误提示、图标库（Segoe Fluent Icons）

## 参考

- [Wpf.Ui GitHub](https://github.com/lepoco/wpfui)
- [Wpf.Ui 文档（控件目录）](https://wpfui.lepo.co/)
- [Microsoft Learn — Mica 背景](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)
- 项目内相关：参见 `README.md` 现有"样式设置"小节
