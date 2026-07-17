# 曲名居中：预读静默时长（取代墙钟 3 秒）

**日期**：2026-07-17  
**范围**：播放中曲名/歌手区域的居中时机——由「歌词空档墙钟满 3 秒再居中」改为「预读下一句非空歌词，长静默空档一开始即居中」。  
**不在范围**：AppBar/Overlay 的视觉样式、歌词获取、同步偏移、纯文本无时间轴歌词的新居中策略、用「两句时间戳间隔」推断间奏（已否决）。

---

## 1. 背景与目标

### 1.1 现状

`TrackInfoLayoutState` 在 `shouldShowLyrics == false` 后用 `DateTimeOffset.UtcNow` 计时，满 `CenterAfterEmptyMs`（3000）才 `ShouldCenter = true`。长间奏开始后用户要多等约 3 秒才看到曲名居中。

### 1.2 目标

- 进入歌词空档时，根据时间轴预读「距下一句非空歌词还有多久」。
- 若该静默将持续 ≥ 3 秒（或已无下一句），**立刻居中**，不再等墙钟。
- 短空档（&lt; 3 秒）仍保持左对齐，避免换气/短间隙抖动。
- 首句歌词出现前居中、有非空歌词时左对齐——行为不变。

### 1.3 静默定义（已确认）

**仅「无有效歌词内容」算静默**：当前帧无活动歌词，或当前行文本为空白。  
**不以**相邻两句非空时间戳的间隔当作静默（一句可唱超过 3 秒）。

触发时机：**B1**——一旦进入空档且预读满足长静默条件，立刻居中（不提前几百毫秒）。

---

## 2. 用户可见行为

| 场景 | 曲名区域 |
|------|----------|
| 首句非空歌词出现前 | 居中 |
| 正在显示非空歌词 | 左对齐 |
| 空档，且距下一句非空歌词 &lt; 3000 ms | 左对齐 |
| 空档，且距下一句非空歌词 ≥ 3000 ms | **立刻居中** |
| 空档，且之后再无非空歌词（含曲末） | **立刻居中** |
| 纯文本 / 无同步时间轴 | 与现网一致（无可靠空档预读则不引入新策略） |

长空档内保持居中直到再次出现非空歌词，避免间奏末尾因「剩余 &lt; 3s」反复切换对齐。

---

## 3. 架构

```
LyricsSyncEngine.GetFrame(progressMs)
        │
        ├─ CurrentLine / IsActive / …（现有）
        └─ MsUntilNextNonEmptyLine（新增预读）
                │
PlaybackCoordinator.UpdateLyricFrame
                │
TrackInfoLayoutState.Update(shouldShowLyrics, msUntilNextNonEmpty)
                │
        ShouldCenterTrackInfo → AppBar / Overlay 布局
```

### 3.1 `LyricsSyncEngine`

在同步歌词路径上，为当前 `progressMs` 计算：

- 找到「下一句」：在时间轴上 `StartTime > progressMs`（或严格晚于当前行）且 `Text` 非空白的最近一行。
- 返回 `msUntilNextNonEmpty = next.StartTime - progressMs`。
- 若无下一句非空行：返回表示「无穷长」的约定值（实现可用 `long.MaxValue` 或可空 + 文档约定；Coordinator/Layout 侧按 ≥ 3000 处理）。

空行（`Text` 空白）本身不打断「下一句非空」的查找，但当前停在空行上时 `shouldShowLyrics` 为 false。

纯文本路径：可不提供有意义预读（例如返回 0 或与「有歌词」一致），Layout 不因预读误居中。

### 3.2 `TrackInfoLayoutState`

- 删除基于 `_lyricsEmptySince` / `DateTimeOffset` 的墙钟计时。
- `Update(bool shouldShowLyrics, long msUntilNextNonEmpty)`（或等价参数）：
  - 有非空歌词 → 记录已出现过首句，左对齐，并清除「长静默锁定」状态。
  - 尚未出现过首句 → 居中。
  - 空档：若 `msUntilNextNonEmpty >= CenterAfterEmptyMs` **或**已处于本段长静默锁定 → 居中；否则左对齐。
- 长静默锁定：一旦因预读判定为长静默而居中，在本段空档内保持居中，直到 `shouldShowLyrics` 再次为 true。
- 阈值常量保留 `CenterAfterEmptyMs = 3000`，语义改为「预读静默时长阈值」，不再表示墙钟等待。

### 3.3 `PlaybackCoordinator`

`UpdateLyricFrame` 从 `GetFrame` 取出预读字段，传入 `_trackInfoLayout.Update(...)`；`ShouldCenterTrackInfo` 同步逻辑不变。

---

## 4. 错误与边界

| 情况 | 处理 |
|------|------|
| Seek 进入长空档中段 | 按当前 `progressMs` 预读；若剩余 ≥ 3000 或已锁定则居中 |
| Seek 到长空档末尾（剩余 &lt; 3000）且本段尚未锁定 | 左对齐（短剩余不居中） |
| 切歌 / `Reset` | 重置为首句前居中，清除锁定 |
| 歌词未加载 / 清空 | 居中（与现 `ClearLyricLines` 一致） |

---

## 5. 测试

- `TrackInfoLayoutState`：短空档不居中；长空档立刻居中；长空档末段锁定仍居中；有歌词后解除锁定并左对齐；`Reset` 恢复。
- `LyricsSyncEngine`：空行后预读到下一句非空的 ms；连续空行跳过；无下一句返回无穷约定；纯文本不破坏现有帧语义。
- 可选：Coordinator 级冒烟（mock 引擎）——非必须，单元覆盖上述两者即可。

---

## 6. 明确不做什么

- 不根据两句非空歌词的 StartTime 差推断间奏。
- 不在空档开始前提前居中（非 B2）。
- 不改 UI 样式与布局控件本身，只改 `ShouldCenter` 判定时机。
- 不为纯文本歌词发明虚拟时间轴静默。
