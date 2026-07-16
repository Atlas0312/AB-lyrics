# AppBar 双列布局（下一句预览）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 AppBar 上下两行（上一句 + 当前行）改为左右双列（当前行 + 下一句预告），通过"设置 → 外观 → 副歌词显示"切换，默认双列。

**架构：**
- 在 `DisplayStyleSettings` 用枚举 `LayoutMode`（`Single` / `TwoColumn`）替换 `LineCount`。
- `AppBarWindow.xaml` 把 `LyricsPanel`（StackPanel）改造为 Grid，内含两个互斥子容器：单列的居中 StackPanel + 双列的两列 Grid。
- 副列直接复用现有副级元素样式入口（不单独设计视觉参数），绑定从 `PreviousLine` 改为 `NextLine`。
- `LyricsStyleApplier` 三处 `LineCount` 引用统一改为 `LayoutMode == TwoColumn`，用户感知不变（Overlay/Preview 行为保持）。

**技术栈：** .NET 8 + WPF + WPF-UI (lepoco) + xUnit。

---

## 文件结构

**修改：**
- `src/ABLyrics.App/Configuration/DisplayStyleSettings.cs` — `LineCount` → `LayoutMode` 枚举
- `src/ABLyrics.App/Views/LyricsStyleApplier.cs` — 三处 `LineCount` 改为 `LayoutMode` 派生
- `src/ABLyrics.App/Views/AppBarWindow.xaml` — `LyricsPanel` 双容器结构 + `SecondaryLineText` 改绑 `NextLine`
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs` — `ApplyStyle` 透传新参数
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml` — "显示行数" 替换为"副歌词显示：单列 / 双列"
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs` — 绑定 RadioButton → `LayoutMode`

**测试修改：**
- `tests/ABLyrics.App.Tests/DisplayStyleSettingsTests.cs` — 新增 `LayoutMode` 默认值断言
- `tests/ABLyrics.App.Tests/DisplaySettingsStoreDefaultsTests.cs` — 旧 JSON 缺字段回退 `TwoColumn`

**新建：** 无（`LyricsLayoutMode` 枚举就放在 `DisplayStyleSettings.cs` 内联，避免创建一次性文件）。

**职责边界：**
- `DisplayStyleSettings` 仅做字段定义和默认值 + `Clone` 复制
- `LyricsStyleApplier` 是视觉应用层（XAML 元素 → 颜色/字号/可见性），不读 settings 之外的来源
- `AppBarWindow.xaml` 是结构层（XAML 容器），不参与视觉决策

---

## 任务 1：新增 `LyricsLayoutMode` 枚举与 `DisplayStyleSettings.LayoutMode`

**文件：**
- 修改：`src/ABLyrics.App/Configuration/DisplayStyleSettings.cs:1-45`
- 测试：`tests/ABLyrics.App.Tests/DisplayStyleSettingsTests.cs`

- [ ] **步骤 1：编写失败的测试**

在 `DisplayStyleSettingsTests.cs` 中追加：

```csharp
[Fact]
public void Defaults_LayoutMode_IsTwoColumn()
{
    var settings = new DisplayStyleSettings();
    Assert.Equal(LyricsLayoutMode.TwoColumn, settings.LayoutMode);
}

[Fact]
public void Clone_CopiesLayoutMode()
{
    var original = new DisplayStyleSettings { LayoutMode = LyricsLayoutMode.Single };
    var clone = original.Clone();
    Assert.Equal(LyricsLayoutMode.Single, clone.LayoutMode);
    clone.LayoutMode = LyricsLayoutMode.TwoColumn;
    Assert.Equal(LyricsLayoutMode.Single, original.LayoutMode);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行（在仓库根）：
```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~DisplayStyleSettingsTests"
```

预期：编译失败（`LyricsLayoutMode` 不存在）。如果只是断言失败也 OK —— 关键是测试代码先到位。

- [ ] **步骤 3：编写实现代码**

替换 `src/ABLyrics.App/Configuration/DisplayStyleSettings.cs` 为：

```csharp
namespace ABLyrics.App.Configuration;

public enum LyricsLayoutMode
{
    Single,
    TwoColumn,
}

public sealed class DisplayStyleSettings
{
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 18;
    public double SongInfoFontSize { get; set; } = 12;
    public double LetterSpacing { get; set; } = 0;
    public int BarHeight { get; set; } = 56;
    public string BackgroundColor { get; set; } = "#101010";
    public double BackgroundOpacity { get; set; } = 0.8;
    public double PaddingLeft { get; set; } = 16;
    public double PaddingTop { get; set; } = 4;
    public double PaddingRight { get; set; } = 16;
    public double PaddingBottom { get; set; } = 4;
    public LyricsLayoutMode LayoutMode { get; set; } = LyricsLayoutMode.TwoColumn;
    public int SyncOffsetMs { get; set; } = 150;

    public string ForegroundColor { get; set; } = "#FFFFFF";
    public double ForegroundOpacity { get; set; } = 0.78;
    public string OverlayBaseColor { get; set; } = "#000000";

    public DisplayStyleSettings Clone()
    {
        return new DisplayStyleSettings
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            SongInfoFontSize = SongInfoFontSize,
            LetterSpacing = LetterSpacing,
            BarHeight = BarHeight,
            BackgroundColor = BackgroundColor,
            BackgroundOpacity = BackgroundOpacity,
            PaddingLeft = PaddingLeft,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            LayoutMode = LayoutMode,
            SyncOffsetMs = SyncOffsetMs,
            ForegroundColor = ForegroundColor,
            ForegroundOpacity = ForegroundOpacity,
            OverlayBaseColor = OverlayBaseColor,
        };
    }
}
```

注意：删除 `public int LineCount`，新增 `LayoutMode`。`Clone` 同步替换。

- [ ] **步骤 4：运行测试验证通过**

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~DisplayStyleSettingsTests"
```

预期：全部 PASS（其它测试可能因为 `LineCount` 删除而失败，那是任务 2/3 的事，本任务范围内 PASS 即可）。

- [ ] **步骤 5：Commit**

```bash
git add src/ABLyrics.App/Configuration/DisplayStyleSettings.cs tests/ABLyrics.App.Tests/DisplayStyleSettingsTests.cs
git commit -m "feat(style): DisplayStyleSettings 用 LayoutMode 枚举替换 LineCount"
```

---

## 任务 2：`LyricsStyleApplier` 三处 `LineCount` 引用改用 `LayoutMode == TwoColumn`

**文件：**
- 修改：`src/ABLyrics.App/Views/LyricsStyleApplier.cs:43, 66, 94, 106`

- [ ] **步骤 1：替换 `ApplyAppBar` 末尾**

`src/ABLyrics.App/Views/LyricsStyleApplier.cs:43`：
```csharp
// 原：
secondaryLine.Visibility = style.LineCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
// 改：
secondaryLine.Visibility = style.LayoutMode == LyricsLayoutMode.TwoColumn ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **步骤 2：替换 `ApplyPreview` 末尾**

`src/ABLyrics.App/Views/LyricsStyleApplier.cs:66`：同步骤 1 的替换。

- [ ] **步骤 3：替换 `ApplyOverlay` 高度公式与可见性**

`src/ABLyrics.App/Views/LyricsStyleApplier.cs:94`：
```csharp
// 原：
var overlayHeight = style.BarHeight + (style.LineCount >= 2 ? style.FontSize * 1.4 : 0) + 28;
// 改：
var overlayHeight = style.BarHeight + (style.LayoutMode == LyricsLayoutMode.TwoColumn ? style.FontSize * 1.4 : 0) + 28;
```

`src/ABLyrics.App/Views/LyricsStyleApplier.cs:106`：同步骤 1 的替换。

- [ ] **步骤 4：编译验证**

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj
```

预期：编译通过。`LyricsStyleApplier` 没有自己的测试，但调用方测试覆盖了下一句/上一句切换逻辑（任务 4 起）。

- [ ] **步骤 5：Commit**

```bash
git add src/ABLyrics.App/Views/LyricsStyleApplier.cs
git commit -m "refactor(style): LyricsStyleApplier 改用 LayoutMode 派生可见性"
```

---

## 任务 3：旧 `display-settings.json` 兼容测试

**文件：**
- 修改：`tests/ABLyrics.App.Tests/DisplaySettingsStoreDefaultsTests.cs`

- [ ] **步骤 1：新增测试**

在 `DisplaySettingsStoreDefaultsTests` 中追加：

```csharp
[Fact]
public void Load_WhenLayoutModeMissingInJson_FallsBackToTwoColumnDefault()
{
    // Older config files serialized before LayoutMode existed.
    File.WriteAllText(_path, """
        {
          "FontFamily": "Segoe UI",
          "FontSize": 20
        }
        """);

    var defaults = new DisplayStyleSettings();

    var loaded = DisplaySettingsStore.Load(_path, defaults);

    Assert.Equal(LyricsLayoutMode.TwoColumn, loaded.LayoutMode);
}

[Fact]
public void Load_WhenLegacyLineCountFieldPresent_IsIgnoredAndLayoutModeKeepsDefault()
{
    File.WriteAllText(_path, """
        {
          "LineCount": 2
        }
        """);

    var defaults = new DisplayStyleSettings();

    var loaded = DisplaySettingsStore.Load(_path, defaults);

    Assert.Equal(LyricsLayoutMode.TwoColumn, loaded.LayoutMode);
}
```

- [ ] **步骤 2：运行测试验证通过**

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~DisplaySettingsStoreDefaultsTests"
```

预期：全部 PASS（包括旧的两个测试）。`JsonSerializer.Deserialize` 默认会忽略未知字段，所以旧 `LineCount` 字段被丢弃，符合规格第 4 节"旧字段被静默忽略"承诺。

- [ ] **步骤 3：Commit**

```bash
git add tests/ABLyrics.App.Tests/DisplaySettingsStoreDefaultsTests.cs
git commit -m "test(style): 旧 display-settings.json 缺 LayoutMode 回退 TwoColumn"
```

---

## 任务 4：AppBar XAML 双列容器结构

**文件：**
- 修改：`src/ABLyrics.App/Views/AppBarWindow.xaml:39-53`

- [ ] **步骤 1：替换 `LyricsPanel` 内容**

`src/ABLyrics.App/Views/AppBarWindow.xaml:39-53`：

```xml
<!-- 中间：歌词（单列/双列互斥子容器） -->
<Grid x:Name="LyricsPanel"
      Grid.Column="1"
      VerticalAlignment="Center"
      HorizontalAlignment="Stretch">
    <StackPanel x:Name="SingleModeStack"
                VerticalAlignment="Center"
                HorizontalAlignment="Center"
                Visibility="Collapsed">
        <TextBlock x:Name="PrimaryLineTextSingle"
                   Text="{Binding CurrentLine}"
                   TextTrimming="CharacterEllipsis"
                   TextAlignment="Center" />
    </StackPanel>
    <Grid x:Name="TwoColumnGrid"
          Visibility="Visible">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto" />
            <ColumnDefinition Width="24" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <TextBlock x:Name="PrimaryLineText"
                   Grid.Column="0"
                   Text="{Binding CurrentLine}"
                   TextTrimming="CharacterEllipsis"
                   VerticalAlignment="Center"
                   HorizontalAlignment="Left" />
        <TextBlock x:Name="SecondaryLineText"
                   Grid.Column="2"
                   Text="{Binding NextLine}"
                   TextTrimming="CharacterEllipsis"
                   VerticalAlignment="Center"
                   HorizontalAlignment="Right" />
    </Grid>
</Grid>
```

**关键点：**
- `SecondaryLineText` 绑定从 `{Binding PreviousLine}` 改为 `{Binding NextLine}`（满足规格第 3 节）。
- `PrimaryLineText` 移到 `Grid.Column="0"`、`HorizontalAlignment="Left"`（满足规格第 1.2 节主列左对齐）。
- 新增 `PrimaryLineTextSingle` 作为单列模式下的居中容器（与旧行为一致），旧 `PrimaryLineText` 改为双列模式下的左对齐版本。`LyricsStyleApplier.ApplyAppBar` 通过 Visibility 切换互斥容器。
- `TwoColumnGrid` 的中间列宽度固定 24（满足规格第 1.2 节硬编码间距）。
- `TwoColumnGrid` 第 3 列 `*` 让副列自然占据剩余宽度（满足规格第 1.2 节"两列都自适应"）。

- [ ] **步骤 2：编译验证**

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj
```

预期：编译通过。注意：现在 `PrimaryLineText` 被 `TwoColumnGrid` 引用，`LyricsStyleApplier.ApplyAppBar` 接收的 `primaryLine` 仍然是 `PrimaryLineText`（不再是旧 StackPanel 里的）。需要任务 5 调整 `ApplyAppBar` 行为以适应新结构。

- [ ] **步骤 3：手动验证 AppBar 启动**

```bash
dotnet run --project src/ABLyrics.App/ABLyrics.App.csproj
```

预期：双列模式默认生效，播放歌曲后能看到 `当前行 ← → 下一句` 布局。如果还没接 `LyricsStyleApplier` 的切换逻辑，默认会看到 `TwoColumnGrid` 可见（已写死），单列/双列切换暂时不会响应 —— 这由任务 5 接入。

- [ ] **步骤 4：Commit**

```bash
git add src/ABLyrics.App/Views/AppBarWindow.xaml
git commit -m "feat(appbar): 双列容器结构 + 副列改绑 NextLine"
```

---

## 任务 5：`LyricsStyleApplier.ApplyAppBar` 适配双列容器 + 设置窗口绑定

**文件：**
- 修改：`src/ABLyrics.App/Views/LyricsStyleApplier.cs:13-44`
- 修改：`src/ABLyrics.App/Views/StyleSettingsWindow.xaml:143-152`
- 修改：`src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs:246-249, 274-275, 324, 393`

- [ ] **步骤 1：扩展 `ApplyAppBar` 签名**

`src/ABLyrics.App/Views/LyricsStyleApplier.cs:13-44` 改为：

```csharp
public static void ApplyAppBar(
    Window window,
    Border chrome,
    TextBlock trackTitle,
    TextBlock artistName,
    TextBlock primaryLine,
    TextBlock secondaryLine,
    TextBlock sourceTag,
    FrameworkElement singleModeStack,
    FrameworkElement twoColumnGrid,
    DisplayStyleSettings style)
{
    var fontFamily = CreateFontFamily(style.FontFamily);
    var background = CreateBackgroundBrush(style);

    window.Height = style.BarHeight;
    chrome.Background = background;
    chrome.Padding = new Thickness(
        style.PaddingLeft,
        style.PaddingTop,
        style.PaddingRight,
        style.PaddingBottom);

    var fg = style.ForegroundColor;
    var secondaryAlpha = style.ForegroundOpacity;

    ApplyTextStyle(trackTitle, fontFamily, style.SongInfoFontSize, FontWeights.SemiBold, fg, PrimaryLineAlpha, style.LetterSpacing);
    ApplyTextStyle(artistName, fontFamily, style.SongInfoFontSize, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
    ApplyTextStyle(primaryLine, fontFamily, style.FontSize, FontWeights.Normal, fg, PrimaryLineAlpha, style.LetterSpacing);
    ApplyTextStyle(secondaryLine, fontFamily, style.FontSize * 0.82, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);
    ApplyTextStyle(sourceTag, fontFamily, style.FontSize * 0.72, FontWeights.Normal, fg, secondaryAlpha, style.LetterSpacing);

    var isTwoColumn = style.LayoutMode == LyricsLayoutMode.TwoColumn;
    singleModeStack.Visibility = isTwoColumn ? Visibility.Collapsed : Visibility.Visible;
    twoColumnGrid.Visibility = isTwoColumn ? Visibility.Visible : Visibility.Collapsed;
    secondaryLine.Visibility = isTwoColumn ? Visibility.Visible : Visibility.Collapsed;
}
```

新增两个 `FrameworkElement` 参数接收两个互斥子容器；末尾三行根据 `LayoutMode` 切换可见性。

- [ ] **步骤 2：更新 `AppBarWindow.ApplyStyle` 调用**

`src/ABLyrics.App/Views/AppBarWindow.xaml.cs:66-78` 改为：

```csharp
private void ApplyStyle(DisplayStyleSettings style)
{
    LyricsStyleApplier.ApplyAppBar(
        this,
        ChromeBorder,
        TrackTitleText,
        ArtistNameText,
        PrimaryLineText,
        SecondaryLineText,
        SourceTagText,
        SingleModeStack,
        TwoColumnGrid,
        style);
    _appBarController?.UpdateHeight(style.BarHeight);
}
```

- [ ] **步骤 3：替换设置窗口 RadioButton 组**

`src/ABLyrics.App/Views/StyleSettingsWindow.xaml:143-152`：

```xml
<!-- 原 -->
<TextBlock Text="显示行数" Margin="0,0,0,12" />
<StackPanel Orientation="Horizontal">
    <RadioButton x:Name="OneLineRadio"
                 Content="1 行"
                 Margin="0,0,24,0"
                 GroupName="LineCount" />
    <RadioButton x:Name="TwoLineRadio"
                 Content="2 行"
                 GroupName="LineCount" />
</StackPanel>

<!-- 改 -->
<TextBlock Text="副歌词显示" Margin="0,0,0,12" />
<StackPanel Orientation="Horizontal">
    <RadioButton x:Name="SingleColumnRadio"
                 Content="单列（不显示下一句）"
                 Margin="0,0,24,0"
                 GroupName="LayoutMode" />
    <RadioButton x:Name="TwoColumnRadio"
                 Content="双列（显示下一句预告）"
                 GroupName="LayoutMode" />
</StackPanel>
```

- [ ] **步骤 4：替换设置窗口代码后置绑定**

`src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`：

替换 `OneLineRadio` / `TwoLineRadio` 所有出现为 `SingleColumnRadio` / `TwoColumnRadio`：

- 行 246-249（WireEvents）：
```csharp
SingleColumnRadio.Checked += (_, _) => ApplyPreviewIfReady();
SingleColumnRadio.LostFocus += OnControlLostFocus;
TwoColumnRadio.Checked += (_, _) => ApplyPreviewIfReady();
TwoColumnRadio.LostFocus += OnControlLostFocus;
```

- 行 274-275（LoadFrom）：
```csharp
SingleColumnRadio.IsChecked = style.LayoutMode == LyricsLayoutMode.Single;
TwoColumnRadio.IsChecked = style.LayoutMode == LyricsLayoutMode.TwoColumn;
```

- 行 324（ReadFromForm）：
```csharp
LayoutMode = TwoColumnRadio.IsChecked == true ? LyricsLayoutMode.TwoColumn : LyricsLayoutMode.Single,
```

- 行 393（ApplyPreview 内的预览文案）：
```csharp
PreviewSecondary.Text = style.LayoutMode == LyricsLayoutMode.TwoColumn ? "下一句歌词预览" : string.Empty;
```

**注意**：行 393 的预览文案从"上一行歌词预览"改为"下一句歌词预览"，与 AppBar 行为一致。

- [ ] **步骤 5：编译验证**

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj
```

预期：编译通过。

- [ ] **步骤 6：手动验证**

```bash
dotnet run --project src/ABLyrics.App/ABLyrics.App.csproj
```

验证：
- 默认看到双列：当前歌词左对齐 + 24px 间距 + 下一句右对齐。
- 打开"设置 → 外观 → 副歌词显示"，切换"单列"，AppBar 副列消失，主列回到居中。
- 切回"双列"，副列重新显示。

- [ ] **步骤 7：运行所有测试**

```bash
dotnet test
```

预期：所有测试 PASS（包括任务 1/3 的新测试，以及现有 `LyricsSearchServiceTests`/`PlaybackCoordinatorOverrideTests` 等不受影响）。

- [ ] **步骤 8：Commit**

```bash
git add src/ABLyrics.App/Views/LyricsStyleApplier.cs src/ABLyrics.App/Views/AppBarWindow.xaml.cs src/ABLyrics.App/Views/StyleSettingsWindow.xaml src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs
git commit -m "feat(appbar): 接入 LayoutMode 双列切换 + 设置面板"
```

---

## 自检

### 1. 规格覆盖度

| 规格章节 | 对应任务 |
|----------|----------|
| 1.1 模式表 | 任务 4（XAML 结构）+ 任务 5（ApplyAppBar 切换） |
| 1.2 双列视觉示意 | 任务 4（XAML 容器 + 间距 + 对齐） |
| 1.3 单列行为（与原版一致） | 任务 5（SingleModeStack + 居中） |
| 2. 设置项 | 任务 5 步骤 3-4（RadioButton + 绑定） |
| 3. 数据源 NextLine | 任务 4 步骤 1（绑定改为 `{Binding NextLine}`） |
| 4. 删除 LineCount + 新增 LayoutMode | 任务 1 |
| 4. 三处 LineCount 派生 | 任务 2 |
| 4. 旧 JSON 兼容 | 任务 3 |
| 6. 容器结构 + 24px 间距 + 副元素复用副级样式 | 任务 4（间距）+ 任务 5（复用 `ApplyTextStyle` 调用样式） |

无遗漏。

### 2. 占位符扫描

无"TODO"、"待定"、"后续实现"。所有步骤都包含完整代码或命令。

### 3. 类型一致性

- `LyricsLayoutMode` 枚举：定义在任务 1，所有引用一致（`== LyricsLayoutMode.TwoColumn` / `== LyricsLayoutMode.Single`）。
- `DisplayStyleSettings.LayoutMode`：属性名一致。
- `LyricsStyleApplier.ApplyAppBar` 签名：参数顺序与 `AppBarWindow.ApplyStyle` 调用点对应。
- `SingleModeStack` / `TwoColumnGrid`：XAML 命名与代码后置引用一致。
- `SingleColumnRadio` / `TwoColumnRadio`：XAML 命名与代码后置引用一致。

无冲突。