# 歌词同步偏移量设计

日期：2026-07-09
状态：已批准

## 需求

1. 限制插值漂移不超过轮询间隔
2. 样式设置界面加上同步偏移量滑块（用户可调）
3. 记录每次偏移量修改值，用于后续分析

## 方案

### 1. PlaybackCoordinator 偏移量 + 插值限制

```csharp
private int _syncOffsetMs = 150;

public int SyncOffsetMs
{
    get => _syncOffsetMs;
    set => SetField(ref _syncOffsetMs, Math.Clamp(value, 0, 500));
}

private long GetInterpolatedProgressMs()
{
    if (!_isPlaying)
        return Math.Max(0, _lastProgressMs - _syncOffsetMs);

    var elapsed = DateTimeOffset.UtcNow - _lastSampleAt;
    var clamped = Math.Min((long)elapsed.TotalMilliseconds, PollIntervalMs);
    return _lastProgressMs + clamped - _syncOffsetMs;
}
```

### 2. 设置持久化

`DisplaySettingsStore` 增加 `SyncOffsetMs` 字段，保存到 `appsettings.json`。启动时加载，调整时保存。

### 3. UI 滑块

`StyleSettingsWindow` 新增一行：`同步偏移: [===o========] 150ms (0~500)`。绑定到 `DisplaySettingsStore.SyncOffsetMs`。

### 4. 修改记录

`SyncOffsetRecorder` 类写入 `logs/sync-offset.csv`：

```csv
timestamp,offset_ms,source
2026-07-09T15:53:00+08:00,150,slider
2026-07-09T15:54:00+08:00,200,slider
```

### 变更文件

| 文件 | 变更 |
|------|------|
| `PlaybackCoordinator.cs` | `SyncOffsetMs` 属性 + `GetInterpolatedProgressMs` 插值限制 |
| `DisplaySettingsStore.cs` | 新增 `SyncOffsetMs` 字段 |
| `DisplaySettingsService.cs` | 加载/保存 `SyncOffsetMs` |
| `StyleSettingsWindow.xaml` | 同步偏移滑块 UI |
| `StyleSettingsWindow.xaml.cs` | 滑块绑定逻辑 |
| `SyncOffsetRecorder.cs` | 新建，记录每次调整 |
