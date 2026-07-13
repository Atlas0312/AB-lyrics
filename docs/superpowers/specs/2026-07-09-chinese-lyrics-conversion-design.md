# 简繁中文歌词转换设计

日期：2026-07-09
状态：已批准

## 需求

所有歌词源（LRCLIB、Netease、Local）返回的繁体中文歌词自动转为简体中文输出。

## 方案

依赖 .NET 内置的 `Microsoft.VisualBasic.Strings.StrConv` API 做繁→简转换，在 `LyricsService.Normalize()` 中统一处理。

### 实现

`LyricsService.cs` 修改 `Normalize()`：

```csharp
using Microsoft.VisualBasic;

private static string? Normalize(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return Strings.StrConv(value.Trim(), VbStrConv.TraditionalChinese, 0x0804);
}
```

无需新增包引用 — `Microsoft.VisualBasic` 在 .NET Windows 项目中可通过 `<UseWindowsForms>true</UseWindowsForms>` 或直接引用 `Microsoft.VisualBasic.dll` 获得。本项目已有 WinForms 互操作，应可直接使用。

### 成功标准

- 歌词含有传统汉字（如"為"、"裡"）时自动转为简体（"为"、"里"）
- 已经是简体的歌词保持不变
- 非中文文本不受影响
- 所有源（LRCLIB、Netease、Local）统一处理

### 文件变更

| 文件 | 变更 |
|------|------|
| `LyricsService.cs` | `Normalize()` 中添加 `StrConv` 转换 |
