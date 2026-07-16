# AppBar 歌词左右双列布局（下一句预览）

**日期**：2026-07-16
**范围**：AppBar（桌面底部 AppBar 歌词栏）的"显示行数"替换为"单列 / 双列"布局，副元素从已播的上一句改为未播的下一句预览。
**不在范围**：Overlay（叠层歌词窗口）、候选选择器窗口、字号/颜色/间距等其他样式设置。

---

## 1. 用户可见行为

### 1.1 AppBar 两种模式

| 模式 | 是否默认 | 主列（当前歌词） | 副列（下一句） | 主列对齐 |
|------|----------|------------------|----------------|----------|
| 单列 | 否 | ✅ 显示 | ❌ 隐藏 | 居中（与当前版本一致） |
| 双列 | ✅ | ✅ 显示 | ✅ 显示 | 左侧固定 |

### 1.2 双列模式视觉示意

```
┌─────────────────────────────────────────────────────────────┐
│ [曲名 · 歌手]  当前歌词           下一句        [源] [🔄] │
└─────────────────────────────────────────────────────────────┘
                  ↑左对齐       ↑右对齐 ↑24px 间距
```

- 主列（当前歌词）：左侧固定，自然宽度。
- 副列（下一句预告）：右侧，自然宽度。
- 主列与副列之间：硬编码 24px 间距。
- **副列视觉层次：完全复用现有"副级元素"样式规则** —— 与原"副行"一致：更小字号（主字号 × 0.82）+ 较低不透明度（沿用现有 `ForegroundOpacity`）。直接调用现有的副级元素样式入口，不为副列单独设计任何视觉参数。
- 高度 56px 不变。

### 1.3 单列模式行为

与当前版本完全一致：副元素隐藏，主列居中。提供向后兼容的安全回退。

---

## 2. 设置项

在 **设置 → 外观** tab，原"显示行数：1 行 / 2 行"两个 RadioButton 替换为：

> **副歌词显示**（Label 文案占位待最终命名）
> ○ 单列（不显示下一句）
> ● 双列（显示下一句预告）[默认]

默认选中"双列"。

> **Label 文案说明**：本次使用 "副歌词显示" 作为占位文案。如后续希望更直白表达，可在迭代中优化为 "下一句预览" 开关。"单列 / 双列" 选项内部文案已包含括号解释，保证用户能一眼理解含义。

---

## 3. 数据源

| 元素 | 数据源 | 备注 |
|------|--------|------|
| 主列（PrimaryLineText） | `PlaybackCoordinator.CurrentLine` | 不变 |
| 副列（SecondaryLineText） | `PlaybackCoordinator.NextLine` | **从 `PreviousLine` 改为 `NextLine`** |

`PlaybackCoordinator` 本身不需要改动 —— `NextLine` 属性已存在，由 `LyricsSyncEngine.GetFrame(...)` 返回的 `frame.NextLine` 填充。

---

## 4. 持久化与向后兼容

- `DisplayStyleSettings`：
  - 删除 `LineCount`（int）。
  - 新增 `LayoutMode`（enum：`Single` | `TwoColumn`，默认 `TwoColumn`）。
- `LyricsStyleApplier` 三处 `LineCount` 引用统一改为基于 `LayoutMode == TwoColumn` 派生：
  - `ApplyAppBar`：控制两个互斥子容器的可见性（详见第 6 节）。
  - `ApplyPreview`：设置面板"预览"卡片里的副行可见性（用户实际无感，仅保持原预览形态）。
  - `ApplyOverlay`：Overlay 窗口高度公式 `BarHeight + (TwoColumn ? FontSize * 1.4 : 0) + 28` + 副行可见性 —— 保持 Overlay 现有行为不变。
- 旧 `display-settings.json` 文件没有 `LayoutMode` 字段 → 反序列化使用默认值 `TwoColumn`。
- 旧文件中的 `LineCount` 字段被静默忽略，不抛错。
- 旧"1 行 / 2 行" RadioButton 替换后，旧的 LineCount 选择被丢弃。

---

## 5. 不在范围（YAGNI）

- Overlay 用户可见行为改造（仍是上下两行的"已播上一句"语义，本次仅底层字段统一，不改变用户感知）。
- 候选选择器窗口改造。
- 主副列间距/字号/颜色等独立设置（保留硬编码默认值）。
- 新增"全局行为"开关（本次仅作用于 AppBar）。

---

## 6. AppBar 布局结构（实现细节）

`AppBarWindow.xaml` 的 `LyricsPanel`（当前是 `StackPanel`）改造为：

```
LyricsPanel（Grid 根，用于挂载 ApplyAppBar 入口）
├─ SingleModeStack（StackPanel，居中的 PrimaryLineText，单列模式可见）
└─ TwoColumnGrid（Grid，两列，双列模式可见）
    ├─ Column 0：PrimaryLineText（HorizontalAlignment=Left，自然宽度）
    ├─ Column 1：固定 24px 间隔列
    └─ Column 2：SecondaryLineText（HorizontalAlignment=Right，自然宽度）
```

`LyricsStyleApplier.ApplyAppBar` 在 `LayoutMode == TwoColumn` 时：
- `SingleModeStack.Visibility = Collapsed`
- `TwoColumnGrid.Visibility = Visible`
- `PrimaryLineText.HorizontalAlignment = Left`
- `SecondaryLineText.HorizontalAlignment = Right`，绑定仍为 `NextLine`

否则（`Single`）：
- `SingleModeStack.Visibility = Visible`
- `TwoColumnGrid.Visibility = Collapsed`
- `PrimaryLineText.HorizontalAlignment = Center`

副元素仍复用现有副级元素样式（字号 × 0.82 + `ForegroundOpacity`），不单独设计任何视觉参数。

---

## 6. 设计取舍说明

**为什么是"双列"而不是"上下两行保留 + 新增左右两列"**：用户明确"完全舍弃原来的两行展示"，且新的两列布局在 56px 高度下信息密度更高（一行内同时呈现当前 + 下一句），更符合 AppBar 紧凑空间的利用。

**为什么副元素改为"下一句"而非"上一句"**：播放预告（提前看到下一句）比回顾已播内容更符合用户对"听歌看词"的预期 —— 让用户能跟上节奏、提前预知情绪变化。

**为什么默认双列**：旧版本默认是 1 行（隐藏副行）。但既然本次改动的核心价值就是把副元素换为"下一句预览"作为默认体验的一部分，默认双列可以让用户一升级就感受到新功能，而不是埋在开关里。
