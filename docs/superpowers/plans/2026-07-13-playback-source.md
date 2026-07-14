# Playback Source Abstraction & Settings Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Spotify sign-in out of the tray context menu into a new "Playback Source" page inside the Settings panel, and introduce a pluggable `IPlaybackSource` abstraction so future sources (e.g. device-local playback) can be added behind the same interface.

**Architecture:** Introduce `IPlaybackSource` + `PlaybackSourceRegistry`; wrap existing `ISpotifyAuthService` + `ISpotifyPlaybackService` into `SpotifyPlaybackSource`; refactor `PlaybackCoordinator` to consume `IPlaybackSource`; add a sixth `NavigationView` page in `StyleSettingsWindow`; persist the active source id in `appsettings.json.Playback.ActiveSource` plus `%LOCALAPPDATA%/ABLyrics/playback-state.json`. No DI container, no reflection-based loading, no changes to lyrics providers.

**Tech Stack:** .NET 8, WPF, WPF-UI (`lepoco/wpfui` 3.1.1), xUnit.

**Reference spec:** `docs/superpowers/specs/2026-07-13-playback-source-design.md`

---

## Task 1: Add `IPlaybackSource` interface

**Files:**
- Create: `src/ABLyrics.App/Services/Playback/IPlaybackSource.cs`

- [ ] **Step 1: Create the interface file**

Write `src/ABLyrics.App/Services/Playback/IPlaybackSource.cs`:

```csharp
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Playback;

public interface IPlaybackSource
{
    string Id { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    void Disconnect();
    Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default);
    event Action<PlaybackState?>? SnapshotChanged;
}
```

- [ ] **Step 2: Build to confirm compile**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: `Build succeeded`, no new warnings.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/Services/Playback/IPlaybackSource.cs
git commit -m "feat(playback): add IPlaybackSource interface"
```

---

## Task 2: Add `PlaybackSettings` config class

**Files:**
- Create: `src/ABLyrics.App/Configuration/PlaybackSettings.cs`

- [ ] **Step 1: Create the settings class**

Write `src/ABLyrics.App/Configuration/PlaybackSettings.cs`:

```csharp
namespace ABLyrics.App.Configuration;

public sealed class PlaybackSettings
{
    public string ActiveSource { get; set; } = "Spotify";
}
```

- [ ] **Step 2: Wire into `AppSettings`**

Modify `src/ABLyrics.App/Configuration/AppSettings.cs`:

```csharp
namespace ABLyrics.App.Configuration;

public sealed class AppSettings
{
    public SpotifySettings Spotify { get; init; } = new();
    public NetEaseSettings NetEase { get; init; } = new();
    public LyricsSettings Lyrics { get; init; } = new();
    public UiSettings Ui { get; init; } = new();
    public PlaybackSettings Playback { get; init; } = new();
}
```

- [ ] **Step 3: Build**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Configuration/PlaybackSettings.cs src/ABLyrics.App/Configuration/AppSettings.cs
git commit -m "feat(config): add PlaybackSettings with default ActiveSource"
```

---

## Task 3: Add `PlaybackStateStore`

**Files:**
- Create: `src/ABLyrics.App/Configuration/PlaybackStateStore.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ABLyrics.App.Tests/PlaybackStateStoreTests.cs`:

```csharp
using System.IO;
using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackStateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public PlaybackStateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "playback-state.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultSpotify()
    {
        var settings = new PlaybackStateStore(_path).Load();
        Assert.Equal("Spotify", settings.ActiveSource);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsActiveSource()
    {
        var store = new PlaybackStateStore(_path);
        store.Save(new PlaybackSettings { ActiveSource = "Spotify" });
        var loaded = store.Load();
        Assert.Equal("Spotify", loaded.ActiveSource);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefault()
    {
        File.WriteAllText(_path, "{ not json");
        var loaded = new PlaybackStateStore(_path).Load();
        Assert.Equal("Spotify", loaded.ActiveSource);
    }
}
```

- [ ] **Step 2: Run test to confirm it fails**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackStateStoreTests"
```

Expected: build error referencing `PlaybackStateStore`.

- [ ] **Step 3: Implement `PlaybackStateStore`**

Write `src/ABLyrics.App/Configuration/PlaybackStateStore.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

public sealed class PlaybackStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public PlaybackStateStore() : this(DefaultPath) { }

    public PlaybackStateStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "playback-state.json");

    public PlaybackSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new PlaybackSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<PlaybackSettings>(json, JsonOptions) ?? new PlaybackSettings();
        }
        catch
        {
            return new PlaybackSettings();
        }
    }

    public void Save(PlaybackSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
```

- [ ] **Step 4: Run test to confirm it passes**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackStateStoreTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ABLyrics.App/Configuration/PlaybackStateStore.cs tests/ABLyrics.App.Tests/PlaybackStateStoreTests.cs
git commit -m "feat(config): add PlaybackStateStore with round-trip tests"
```

---

## Task 4: Update `ConfigurationLoader` to read `Playback.ActiveSource`

**Files:**
- Modify: `src/ABLyrics.App/Configuration/ConfigurationLoader.cs`

- [ ] **Step 1: Add runtime override layering**

Replace the body of `Load` in `src/ABLyrics.App/Configuration/ConfigurationLoader.cs` with:

```csharp
public static AppSettings Load(string? baseDirectory = null)
{
    baseDirectory ??= AppContext.BaseDirectory;
    var path = Path.Combine(baseDirectory, "appsettings.json");

    var settings = File.Exists(path)
        ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
        : new AppSettings();

    // Environment override for Spotify client id.
    var envClientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
    if (!string.IsNullOrWhiteSpace(envClientId))
    {
        settings.Spotify.ClientId = envClientId;
    }

    // Layer playback-state.json on top of appsettings.json.
    try
    {
        var playback = new PlaybackStateStore().Load();
        if (!string.IsNullOrWhiteSpace(playback.ActiveSource))
        {
            settings.Playback.ActiveSource = playback.ActiveSource;
        }
    }
    catch
    {
        // Ignore — keep the appsettings.json value.
    }

    return settings;
}
```

- [ ] **Step 2: Update default `appsettings.json` to include the new section**

Modify `src/ABLyrics.App/appsettings.json`. Add the `Playback` block at the bottom (after `Ui`):

```json
  "Playback": {
    "ActiveSource": "Spotify"
  }
}
```

The full file becomes:

```json
{
  "Spotify": {
    "ClientId": "",
    "RedirectUri": "http://127.0.0.1:48721/callback",
    "Scopes": [
      "user-read-currently-playing",
      "user-read-playback-state"
    ]
  },
  "NetEase": {
    "MusicU": ""
  },
  "Lyrics": {
    "PrimaryProvider": "LRCLIB",
    "FallbackProvider": "Netease",
    "UserAgent": "AB-lyrics/0.1.0"
  },
  "Ui": {
    "DefaultMode": "AppBar",
    "AppBarHeight": 56
  },
  "Playback": {
    "ActiveSource": "Spotify"
  }
}
```

- [ ] **Step 3: Build and run existing tests**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug
```

Expected: build succeeds; existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Configuration/ConfigurationLoader.cs src/ABLyrics.App/appsettings.json
git commit -m "feat(config): overlay playback-state.json on appsettings.json"
```

---

## Task 5: Add `PlaybackSourceRegistry` with tests

**Files:**
- Create: `src/ABLyrics.App/Services/Playback/PlaybackSourceRegistry.cs`
- Create: `tests/ABLyrics.App.Tests/PlaybackSourceRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ABLyrics.App.Tests/PlaybackSourceRegistryTests.cs`:

```csharp
using ABLyrics.App.Models;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackSourceRegistryTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsAvailable { get; init; } = true;
        public bool IsConnected { get; private set; }
        public int ConnectCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public PlaybackState? NextSnapshot { get; set; }

        public FakeSource(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            return Task.FromResult(NextSnapshot);
        }

        public event Action<PlaybackState?>? SnapshotChanged;
    }

    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new PlaybackSourceRegistry();
        var fake = new FakeSource("Spotify", "Spotify");
        registry.Register(fake);

        Assert.Same(fake, registry.Get("Spotify"));
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var registry = new PlaybackSourceRegistry();
        Assert.Null(registry.Get("Local"));
    }

    [Fact]
    public void All_ReflectsRegistrations()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        registry.Register(new FakeSource("Local", "Local"));

        Assert.Equal(2, registry.All.Count);
        Assert.Contains(registry.All, s => s.Id == "Spotify");
        Assert.Contains(registry.All, s => s.Id == "Local");
    }

    [Fact]
    public void Clear_ResetsRegistrations()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        registry.Clear();

        Assert.Empty(registry.All);
    }
}
```

- [ ] **Step 2: Run test to confirm it fails**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackSourceRegistryTests"
```

Expected: build error referencing `PlaybackSourceRegistry`.

- [ ] **Step 3: Implement `PlaybackSourceRegistry`**

Write `src/ABLyrics.App/Services/Playback/PlaybackSourceRegistry.cs`:

```csharp
namespace ABLyrics.App.Services.Playback;

public sealed class PlaybackSourceRegistry
{
    private readonly List<IPlaybackSource> _sources = new();

    public IReadOnlyList<IPlaybackSource> All => _sources;

    public void Register(IPlaybackSource source)
    {
        if (_sources.Any(s => s.Id == source.Id))
        {
            throw new InvalidOperationException($"播放来源 {source.Id} 已被注册。");
        }
        _sources.Add(source);
    }

    public IPlaybackSource? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
    }

    public void Clear() => _sources.Clear();
}
```

- [ ] **Step 4: Run test to confirm it passes**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackSourceRegistryTests"
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/ABLyrics.App/Services/Playback/PlaybackSourceRegistry.cs tests/ABLyrics.App.Tests/PlaybackSourceRegistryTests.cs
git commit -m "feat(playback): add PlaybackSourceRegistry with tests"
```

---

## Task 6: Add `SpotifyPlaybackSource` implementation

**Files:**
- Create: `src/ABLyrics.App/Services/Playback/SpotifyPlaybackSource.cs`

- [ ] **Step 1: Create the implementation**

Write `src/ABLyrics.App/Services/Playback/SpotifyPlaybackSource.cs`:

```csharp
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Spotify;

namespace ABLyrics.App.Services.Playback;

public sealed class SpotifyPlaybackSource : IPlaybackSource
{
    private readonly ISpotifyAuthService _authService;
    private readonly ISpotifyPlaybackService _playbackService;
    private readonly SpotifySettings _settings;

    public SpotifyPlaybackSource(
        ISpotifyAuthService authService,
        ISpotifyPlaybackService playbackService,
        AppSettings settings)
    {
        _authService = authService;
        _playbackService = playbackService;
        _settings = settings.Spotify;
    }

    public string Id => "Spotify";
    public string DisplayName => "Spotify";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_settings.ClientId);

    public bool IsConnected => _authService.IsAuthenticated;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("请在 appsettings.json 中配置 Spotify ClientId。");
        }
        await _authService.EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Disconnect() => _authService.Logout();

    public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => _playbackService.GetCurrentPlaybackAsync(cancellationToken);

    public event Action<PlaybackState?>? SnapshotChanged
    {
        add { /* Spotify: no push channel yet */ }
        remove { /* no-op */ }
    }
}
```

- [ ] **Step 2: Build**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/Services/Playback/SpotifyPlaybackSource.cs
git commit -m "feat(playback): add SpotifyPlaybackSource adapter"
```

---

## Task 7: Update `StyleSettingsTabRouter` for the new tab

**Files:**
- Modify: `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs`
- Modify: `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`

- [ ] **Step 1: Add the new tag and mapping**

In `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs`:

- Add `public const string PlaybackSourceTag = "playback-source";` after `AboutTag`.
- Extend `KnownTags` to include `PlaybackSourceTag`.
- Add `PlaybackSourceTag => "PlaybackSourcePage",` to the `Resolve` switch.

Final file:

```csharp
// This Source Code Form is subject to the terms of the MIT License.
// Copyright (C) ABLyrics Contributors.

namespace ABLyrics.App.Views;

internal static class StyleSettingsTabRouter
{
    public const string AppearanceTag = "appearance";
    public const string LayoutTag = "layout";
    public const string ColorTag = "color";
    public const string SyncTag = "sync";
    public const string AboutTag = "about";
    public const string PlaybackSourceTag = "playback-source";

    public static readonly IReadOnlyCollection<string> KnownTags = new[]
    {
        AppearanceTag,
        LayoutTag,
        ColorTag,
        SyncTag,
        AboutTag,
        PlaybackSourceTag,
    };

    public static string? Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        return tag switch
        {
            AppearanceTag => "AppearancePage",
            LayoutTag => "LayoutPage",
            ColorTag => "ColorPage",
            SyncTag => "SyncPage",
            AboutTag => "AboutPage",
            PlaybackSourceTag => "PlaybackSourcePage",
            _ => null,
        };
    }
}
```

- [ ] **Step 2: Update existing tests and add new coverage**

Replace `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs` with:

```csharp
using ABLyrics.App.Views;
using Xunit;

namespace ABLyrics.App.Tests;

public class StyleSettingsTabRouterTests
{
    [Theory]
    [InlineData(StyleSettingsTabRouter.AppearanceTag, "AppearancePage")]
    [InlineData(StyleSettingsTabRouter.LayoutTag, "LayoutPage")]
    [InlineData(StyleSettingsTabRouter.ColorTag, "ColorPage")]
    [InlineData(StyleSettingsTabRouter.SyncTag, "SyncPage")]
    [InlineData(StyleSettingsTabRouter.AboutTag, "AboutPage")]
    [InlineData(StyleSettingsTabRouter.PlaybackSourceTag, "PlaybackSourcePage")]
    public void Resolve_KnownTag_ReturnsMatchingPageName(string tag, string expectedPage)
    {
        Assert.Equal(expectedPage, StyleSettingsTabRouter.Resolve(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Appearance")]
    [InlineData("APPEARANCE")]
    [InlineData("settings")]
    [InlineData("foo bar")]
    public void Resolve_UnknownOrEmptyTag_ReturnsNull(string? tag)
    {
        Assert.Null(StyleSettingsTabRouter.Resolve(tag));
    }

    [Fact]
    public void Resolve_DoesNotMutateInput()
    {
        const string original = StyleSettingsTabRouter.AppearanceTag;
        var copy = new string(original);
        StyleSettingsTabRouter.Resolve(copy);
        Assert.Equal(original, copy);
    }

    [Fact]
    public void KnownTags_ContainsExactlySixTags()
    {
        Assert.Equal(6, StyleSettingsTabRouter.KnownTags.Count);
        Assert.Contains(StyleSettingsTabRouter.AppearanceTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.LayoutTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.ColorTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.SyncTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.AboutTag, StyleSettingsTabRouter.KnownTags);
        Assert.Contains(StyleSettingsTabRouter.PlaybackSourceTag, StyleSettingsTabRouter.KnownTags);
    }
}
```

- [ ] **Step 3: Run tests to confirm pass**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~StyleSettingsTabRouterTests"
```

Expected: 14 passed.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Views/StyleSettingsTabRouter.cs tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs
git commit -m "feat(settings): route PlaybackSourcePage in StyleSettingsTabRouter"
```

---

## Task 8: Add the new `PlaybackSourcePage` to the settings window

**Files:**
- Modify: `src/ABLyrics.App/Views/StyleSettingsWindow.xaml`
- Modify: `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`

- [ ] **Step 1: Add the navigation entry**

In `src/ABLyrics.App/Views/StyleSettingsWindow.xaml`, add a new `NavigationViewItem` inside `NavigationView.MenuItems` (right after the `About` item):

```xml
<ui:NavigationViewItem Content="播放来源" TargetPageTag="playback-source" Click="OnMenuItemClick" />
```

- [ ] **Step 2: Add the new ScrollViewer**

In the same file, add a new ScrollViewer (place it before the existing `AboutPage` block so the visible order matches the navigation order):

```xml
<!-- 播放来源页 -->
<ScrollViewer x:Name="PlaybackSourcePage" Visibility="Collapsed"
              VerticalScrollBarVisibility="Auto" Padding="24">
    <StackPanel>
        <TextBlock Text="播放进度来源" FontSize="20" FontWeight="SemiBold" Margin="0,0,0,16" />
        <ui:Card Padding="20" Margin="0,0,0,16">
            <StackPanel>
                <TextBlock Text="活动来源" FontWeight="SemiBold" Margin="0,0,0,8" />
                <TextBlock x:Name="ActiveSourceNameText" Text="(未选择)" Margin="0,0,0,4" />
                <TextBlock x:Name="ActiveSourceStatusText" Opacity="0.7" Margin="0,0,0,12" />
                <StackPanel Orientation="Horizontal">
                    <ui:Button x:Name="ConnectSourceButton" Content="连接"
                               Width="84" Margin="0,0,8,0" Click="OnConnectSourceClick" />
                    <ui:Button x:Name="DisconnectSourceButton" Content="断开"
                               Width="84" Click="OnDisconnectSourceClick" />
                </StackPanel>
            </StackPanel>
        </ui:Card>

        <ui:Card x:Name="SpotifyDetailCard" Padding="20" Margin="0,0,0,16" Visibility="Collapsed">
            <StackPanel>
                <TextBlock Text="Spotify" FontWeight="SemiBold" Margin="0,0,0,8" />
                <TextBlock x:Name="SpotifyStatusDetailText" Opacity="0.7" Margin="0,0,0,12" />
                <TextBlock x:Name="SpotifyUnavailableHint"
                           TextWrapping="Wrap" FontSize="12" Opacity="0.7"
                           Visibility="Collapsed"
                           Text="请在 appsettings.json 中配置 Spotify.ClientId，然后重启应用。" />
            </StackPanel>
        </ui:Card>

        <ui:Card Padding="20">
            <StackPanel>
                <TextBlock Text="可用来源" FontWeight="SemiBold" Margin="0,0,0,8" />
                <ItemsControl x:Name="AvailableSourcesList">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Padding="8" Margin="0,0,0,8" CornerRadius="4"
                                    Background="{DynamicResource ControlFillColorDefaultBrush}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <StackPanel Grid.Column="0">
                                        <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" />
                                        <TextBlock Text="{Binding StatusText}" Opacity="0.7" FontSize="12" />
                                    </StackPanel>
                                    <ui:Button Grid.Column="1" Content="使用此来源"
                                                Click="OnUseSourceClick"
                                                Tag="{Binding Id}" />
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </ui:Card>
    </StackPanel>
</ScrollViewer>
```

- [ ] **Step 3: Wire routing and behaviour in code-behind**

In `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`:

1. Add the using:

```csharp
using ABLyrics.App.Services.Playback;
```

2. Replace the `OnMenuItemClick` method body so it also toggles `PlaybackSourcePage`:

```csharp
private void OnMenuItemClick(object sender, RoutedEventArgs e)
{
    if (sender is not NavigationViewItem { TargetPageTag: var tag } || string.IsNullOrEmpty(tag))
    {
        return;
    }

    if (StyleSettingsTabRouter.Resolve(tag) is not { } pageName)
    {
        return;
    }

    AppearancePage.Visibility = Visibility.Collapsed;
    LayoutPage.Visibility = Visibility.Collapsed;
    ColorPage.Visibility = Visibility.Collapsed;
    SyncPage.Visibility = Visibility.Collapsed;
    AboutPage.Visibility = Visibility.Collapsed;
    PlaybackSourcePage.Visibility = Visibility.Collapsed;

    switch (pageName)
    {
        case nameof(AppearancePage): AppearancePage.Visibility = Visibility.Visible; break;
        case nameof(LayoutPage): LayoutPage.Visibility = Visibility.Visible; break;
        case nameof(ColorPage): ColorPage.Visibility = Visibility.Visible; break;
        case nameof(SyncPage): SyncPage.Visibility = Visibility.Visible; break;
        case nameof(AboutPage): AboutPage.Visibility = Visibility.Visible; break;
        case nameof(PlaybackSourcePage):
            PlaybackSourcePage.Visibility = Visibility.Visible;
            RefreshPlaybackSourcePanel();
            break;
    }
}
```

3. Add helper fields and methods to the class:

```csharp
private PlaybackSourceRegistry? _sourceRegistry;
private PlaybackCoordinator? _coordinatorRef;

private void AttachPlaybackSourceRegistry(PlaybackSourceRegistry registry)
{
    _sourceRegistry = registry;
}

private void SetCoordinatorReference(PlaybackCoordinator coordinator)
{
    _coordinatorRef = coordinator;
}

private void RefreshPlaybackSourcePanel()
{
    if (_sourceRegistry is null || _coordinatorRef is null) return;

    var registry = _sourceRegistry;
    var coordinator = _coordinatorRef;

    var active = coordinator.ActiveSource;
    ActiveSourceNameText.Text = active?.DisplayName ?? "(未选择)";
    ActiveSourceStatusText.Text = active is null
        ? "尚未选择播放来源"
        : active.IsConnected ? "已连接" : (active.IsAvailable ? "未连接" : "不可用：缺少配置");

    ConnectSourceButton.IsEnabled = active is { IsAvailable: true, IsConnected: false };
    DisconnectSourceButton.IsEnabled = active is { IsConnected: true };

    SpotifyDetailCard.Visibility = active is SpotifyPlaybackSource ? Visibility.Visible : Visibility.Collapsed;
    if (active is SpotifyPlaybackSource spotify)
    {
        SpotifyStatusDetailText.Text = spotify.IsConnected
            ? "Spotify 已连接，可开始播放。"
            : (spotify.IsAvailable ? "Spotify 未登录。" : "Spotify 不可用：未配置 ClientId。");
        SpotifyUnavailableHint.Visibility = spotify.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
    }

    var items = registry.All.Select(s => new
    {
        s.Id,
        s.DisplayName,
        StatusText = !s.IsAvailable ? "不可用：缺少配置"
            : s.IsConnected ? "已连接"
            : (s.Id == coordinator.ActiveSource?.Id ? "当前未连接" : "未连接"),
    }).ToList();
    AvailableSourcesList.ItemsSource = items;
}

private async void OnConnectSourceClick(object sender, RoutedEventArgs e)
{
    if (_coordinatorRef?.ActiveSource is null) return;
    try
    {
        await _coordinatorRef.ActiveSource.ConnectAsync();
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "连接播放来源失败");
    }
    finally
    {
        RefreshPlaybackSourcePanel();
    }
}

private void OnDisconnectSourceClick(object sender, RoutedEventArgs e)
{
    if (_coordinatorRef?.ActiveSource is null) return;
    try
    {
        _coordinatorRef.ActiveSource.Disconnect();
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "断开播放来源失败");
    }
    finally
    {
        RefreshPlaybackSourcePanel();
    }
}

private async void OnUseSourceClick(object sender, RoutedEventArgs e)
{
    if (sender is not Wpf.Ui.Controls.Button { Tag: string id }) return;
    if (_coordinatorRef is null) return;
    try
    {
        await _coordinatorRef.SetActiveSourceAsync(id);
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "切换播放来源失败");
    }
    finally
    {
        RefreshPlaybackSourcePanel();
    }
}
```

4. In the constructor (after `LoadFrom(_displaySettings.Current);`), add:

```csharp
_sourceRegistry = App.GetPlaybackSourceRegistry();
SetCoordinatorReference(_coordinator);
_coordinator.SourceStateChanged += (_, _) => Dispatcher.BeginInvoke(RefreshPlaybackSourcePanel);
RefreshPlaybackSourcePanel();
```

- [ ] **Step 4: Build**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: build succeeds (warnings about `App.GetPlaybackSourceRegistry` are expected — it will be added in Task 9).

- [ ] **Step 5: Commit**

```bash
git add src/ABLyrics.App/Views/StyleSettingsWindow.xaml src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs
git commit -m "feat(settings): add PlaybackSourcePage with source list and controls"
```

---

## Task 9: Refactor `PlaybackCoordinator` to consume `IPlaybackSource`

**Files:**
- Modify: `src/ABLyrics.App/Services/PlaybackCoordinator.cs`

- [ ] **Step 1: Replace constructor and dependencies**

In `src/ABLyrics.App/Services/PlaybackCoordinator.cs`:

1. Add the using:

```csharp
using ABLyrics.App.Services.Playback;
```

2. Replace the existing `_authService` / `_playbackService` field block with:

```csharp
private readonly PlaybackSourceRegistry _registry;
private readonly ILyricsService _lyricsService;
private IPlaybackSource? _activeSource;
```

3. Replace the constructor:

```csharp
public PlaybackCoordinator(
    PlaybackSourceRegistry registry,
    string initialSourceId,
    ILyricsService lyricsService,
    DisplaySettingsService? displaySettings = null)
{
    _registry = registry;
    _lyricsService = lyricsService;
    _activeSource = _registry.Get(initialSourceId);
    _syncOffsetMs = Math.Clamp(displaySettings?.Current.SyncOffsetMs ?? 150, 0, 500);

    _timer = new System.Timers.Timer(PollIntervalMs);
    _timer.Elapsed += async (_, _) => await PollAsync().ConfigureAwait(false);
    _timer.AutoReset = true;
}
```

4. Replace `IsAuthenticated` and add source-facing members:

```csharp
public bool IsAuthenticated => _activeSource?.IsConnected ?? false;
public IPlaybackSource? ActiveSource => _activeSource;
public bool IsSourceConnected => _activeSource?.IsConnected ?? false;
public event Action? SourceStateChanged;
```

5. Replace `TryRestoreSessionAsync`:

```csharp
public async Task<bool> TryRestoreSessionAsync()
{
    if (_activeSource is null) return false;

    try
    {
        await _activeSource.ConnectAsync().ConfigureAwait(false);
        StatusText = $"已连接 {_activeSource.DisplayName}";
        SourceStateChanged?.Invoke();
        return true;
    }
    catch
    {
        StatusText = $"请先连接 {_activeSource.DisplayName}";
        return false;
    }
}
```

6. Replace `LoginAsync` / `Logout` with thin shells that operate on `_activeSource`:

```csharp
public Task LoginAsync()
{
    if (_activeSource is null)
    {
        throw new InvalidOperationException("尚未选择播放来源。");
    }
    return _activeSource.ConnectAsync();
}

public void Logout()
{
    Stop();
    _activeSource?.Disconnect();
    ResetDisplay("已断开当前播放来源");
    SourceStateChanged?.Invoke();
}
```

7. Add `SetActiveSourceAsync`:

```csharp
public async Task SetActiveSourceAsync(string id, bool restoreOnly = false)
{
    if (_activeSource is { } current && string.Equals(current.Id, id, StringComparison.Ordinal) && current.IsConnected)
    {
        return;
    }

    Stop();
    ClearLyrics();

    var next = _registry.Get(id);
    if (next is null)
    {
        _activeSource = null;
        StatusText = "未知播放来源";
        SourceStateChanged?.Invoke();
        return;
    }

    _activeSource = next;
    if (!next.IsAvailable)
    {
        StatusText = $"来源不可用：{next.DisplayName}";
        SourceStateChanged?.Invoke();
        return;
    }

    try
    {
        await next.ConnectAsync().ConfigureAwait(false);
        StatusText = $"已连接 {next.DisplayName}";
        Start();
    }
    catch (Exception ex)
    {
        StatusText = $"连接 {next.DisplayName} 失败：{ex.Message}";
    }

    SourceStateChanged?.Invoke();

    if (!restoreOnly)
    {
        try
        {
            new PlaybackStateStore().Save(new PlaybackSettings { ActiveSource = next.Id });
        }
        catch
        {
            // Persist failure is non-fatal.
        }
    }
}
```

8. Replace `PollAsync`:

```csharp
private async Task PollAsync()
{
    try
    {
        if (_activeSource is null || !_activeSource.IsConnected)
        {
            Stop();
            StatusText = _activeSource is null
                ? "未配置播放来源"
                : $"请先连接 {_activeSource.DisplayName}";
            return;
        }

        var playback = await _activeSource.GetSnapshotAsync().ConfigureAwait(false);
        if (playback is null)
        {
            var wasPlaying = _isPlaying;
            _isPlaying = false;
            if (wasPlaying != _isPlaying) IsPlayingChanged?.Invoke(_isPlaying);
            ClearLyrics();
            TrackTitle = string.Empty;
            ArtistName = string.Empty;
            StatusText = "未在播放";
            return;
        }

        TrackTitle = playback.Track.Name;
        ArtistName = playback.Track.Artist;
        var wasPlaying2 = _isPlaying;
        _isPlaying = playback.IsPlaying;
        if (wasPlaying2 != _isPlaying) IsPlayingChanged?.Invoke(_isPlaying);
        _lastProgressMs = playback.ProgressMs;
        _lastSampleAt = DateTimeOffset.UtcNow;

        if (_loadedTrackId != playback.Track.Id)
        {
            await LoadTrackAsync(playback.Track).ConfigureAwait(false);
        }

        UpdateLyricFrame(GetInterpolatedProgressMs());
    }
    catch (Exception ex)
    {
        StatusText = ex.Message;
    }
}
```

9. Replace `Start` so it stays stopped when no source is connected:

```csharp
public void Start()
{
    if (_activeSource is null || !_activeSource.IsConnected)
    {
        StatusText = _activeSource is null
            ? "未配置播放来源"
            : $"请先连接 {_activeSource.DisplayName}";
        return;
    }

    if (!_timer.Enabled)
    {
        _timer.Start();
        StatusText = "正在监听播放…";
    }
}
```

10. Update `Dispose` to remove the obsolete `_authService`/`_playbackService` casts:

```csharp
public void Dispose()
{
    _timer.Stop();
    _timer.Dispose();
}
```

- [ ] **Step 2: Build**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: build fails on `App.xaml.cs` because the old `new PlaybackCoordinator(auth, playback, ...)` call signature is gone — that is fixed in Task 10.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/Services/PlaybackCoordinator.cs
git commit -m "refactor(playback): coordinator consumes IPlaybackSource"
```

---

## Task 10: Wire registry + new coordinator in `App.xaml.cs`

**Files:**
- Modify: `src/ABLyrics.App/App.xaml.cs`

- [ ] **Step 1: Add registry + remove tray login items**

In `src/ABLyrics.App/App.xaml.cs`:

1. Add the using:

```csharp
using ABLyrics.App.Services.Playback;
using ABLyrics.App.Configuration;
```

2. Replace the tray context menu builder. Remove the `_loginMenuItem` field and its wiring, rename the styles menu, and ensure the build method returns the new menu:

Replace the existing `BuildTrayContextMenu` with:

```csharp
private Forms.ContextMenuStrip BuildTrayContextMenu()
{
    var menu = new Forms.ContextMenuStrip();

    menu.Items.Add(new Forms.ToolStripMenuItem("ABLyrics") { Enabled = false });
    menu.Items.Add(new Forms.ToolStripSeparator());

    _overlayToggle = new Forms.ToolStripMenuItem("悬浮歌词");
    _overlayToggle.Click += (_, _) => ToggleOverlay();
    menu.Items.Add(_overlayToggle);
    menu.Items.Add(new Forms.ToolStripSeparator());

    var settingsItem = new Forms.ToolStripMenuItem("设置…");
    settingsItem.Click += (_, _) => OnStyleSettingsClick();
    menu.Items.Add(settingsItem);
    menu.Items.Add(new Forms.ToolStripSeparator());

    var exitItem = new Forms.ToolStripMenuItem("退出");
    exitItem.Click += (_, _) => Shutdown();
    menu.Items.Add(exitItem);

    return menu;
}
```

3. Update `OnStartup` to build the registry and pass it to `PlaybackCoordinator`:

Replace the relevant block (the lines between `Settings = ConfigurationLoader.Load();` and `CreateTrayIcon();`) with:

```csharp
Settings = ConfigurationLoader.Load();

var displayDefaults = new DisplayStyleSettings
{
    BarHeight = Settings.Ui.AppBarHeight,
};
DisplaySettings = new DisplaySettingsService(displayDefaults);

var authService = new SpotifyAuthService(Settings);
var playbackService = new SpotifyPlaybackService(authService);
var lyricsService = new LyricsService(Settings);

_sourceRegistry = new PlaybackSourceRegistry();
_sourceRegistry.Register(new SpotifyPlaybackSource(authService, playbackService, Settings));

Coordinator = new PlaybackCoordinator(
    _sourceRegistry,
    Settings.Playback.ActiveSource,
    lyricsService,
    DisplaySettings);
Coordinator.IsPlayingChanged += OnIsPlayingChanged;
Coordinator.SourceStateChanged += (_, _) => Dispatcher.BeginInvoke(OnCoordinatorSourceStateChanged);

CreateTrayIcon();
```

4. Add the field and helper accessor above `OnStartup`:

```csharp
private PlaybackSourceRegistry? _sourceRegistry;

public static PlaybackSourceRegistry GetPlaybackSourceRegistry()
{
    var app = (App)Current;
    return app._sourceRegistry ?? throw new InvalidOperationException("Registry 尚未初始化。");
}

private void OnCoordinatorSourceStateChanged()
{
    UpdateTooltip();
    UpdateMenuStates();
}
```

5. Replace `TryRestoreSessionAsync`:

```csharp
private async Task TryRestoreSessionAsync()
{
    try
    {
        if (await Coordinator.TryRestoreSessionAsync())
        {
            Coordinator.Start();
        }
    }
    catch (Exception ex)
    {
        if (DevExceptionReporter.IsEnabled)
        {
            DevExceptionReporter.Show(ex, "播放来源会话恢复失败");
        }
    }
    finally
    {
        UpdateMenuStates();
        UpdateTooltip();
    }
}
```

6. Delete the obsolete `OnLoginLogoutClick` method entirely.

7. Update `UpdateMenuStates` to remove the Spotify login item state:

```csharp
private void UpdateMenuStates()
{
    _overlayToggle!.Checked = _overlayWindow is not null;
}
```

8. Update `UpdateTooltip` to use the new generic status text:

```csharp
private void UpdateTooltip()
{
    if (_trayIcon is null) return;

    if (Coordinator.ActiveSource is null)
    {
        _trayIcon.Text = "ABLyrics\n未配置播放来源";
        return;
    }

    if (!Coordinator.IsSourceConnected)
    {
        _trayIcon.Text = $"ABLyrics\n请先连接 {Coordinator.ActiveSource.DisplayName}";
        return;
    }

    var title = Coordinator.TrackTitle;
    var artist = Coordinator.ArtistName;
    var line = Coordinator.CurrentLine;
    var source = Coordinator.LyricsSource;

    if (string.IsNullOrWhiteSpace(title))
    {
        _trayIcon.Text = "ABLyrics\n未在播放";
        return;
    }

    var tip = $"ABLyrics\n🎵 {title} - {artist}";
    if (!string.IsNullOrWhiteSpace(line)) tip += $"\n歌词：{line}";
    if (!string.IsNullOrWhiteSpace(source)) tip += $" | 来源：{source}";

    _trayIcon.Text = tip.Length > 128 ? tip[..125] + "…" : tip;
}
```

- [ ] **Step 2: Build**

Run:

```bash
dotnet build src/ABLyrics.App/ABLyrics.App.csproj -c Debug
```

Expected: build succeeds.

- [ ] **Step 3: Run all tests**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/App.xaml.cs
git commit -m "feat(app): wire PlaybackSourceRegistry and remove tray login"
```

---

## Task 11: Add coordinator active-source tests

**Files:**
- Create: `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs`:

```csharp
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackCoordinatorActiveSourceTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsAvailable { get; set; } = true;
        public bool IsConnected { get; private set; }
        public bool ConnectShouldThrow { get; set; }
        public int ConnectCalls { get; private set; }
        public PlaybackState? NextSnapshot { get; set; }

        public FakeSource(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            if (ConnectShouldThrow) throw new InvalidOperationException("boom");
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;

        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NextSnapshot);

        public event Action<PlaybackState?>? SnapshotChanged;
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB" };
        public Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
            => Task.FromResult<LyricsResult?>(null);
        public Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
            => Task.FromResult<LyricsResult?>(null);
    }

    private static PlaybackCoordinator BuildCoordinator(PlaybackSourceRegistry registry, string initialId)
    {
        var coordinator = new PlaybackCoordinator(
            registry,
            initialId,
            new FakeLyricsService(),
            new DisplaySettingsService(new DisplayStyleSettings()));
        return coordinator;
    }

    [Fact]
    public void TryRestore_WhenSourceUnavailable_StaysStopped()
    {
        var registry = new PlaybackSourceRegistry();
        var source = new FakeSource("Spotify", "Spotify") { IsAvailable = false };
        registry.Register(source);

        var coordinator = BuildCoordinator(registry, "Spotify");

        var restored = coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        Assert.False(restored);
        Assert.False(coordinator.IsRunning);
        Assert.Equal(0, source.ConnectCalls);
    }

    [Fact]
    public void TryRestore_WhenSourceAvailableAndConnected_MarksRestored()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        var coordinator = BuildCoordinator(registry, "Spotify");

        var restored = coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        Assert.True(restored);
        Assert.True(coordinator.IsSourceConnected);
    }

    [Fact]
    public void SetActiveSource_UnknownId_LeavesActiveSourceNull()
    {
        var registry = new PlaybackSourceRegistry();
        registry.Register(new FakeSource("Spotify", "Spotify"));
        var coordinator = BuildCoordinator(registry, "Spotify");

        coordinator.SetActiveSourceAsync("Local").GetAwaiter().GetResult();

        Assert.Null(coordinator.ActiveSource);
    }

    [Fact]
    public void SetActiveSource_ConnectThrows_KeepsPreviousActiveSource()
    {
        var registry = new PlaybackSourceRegistry();
        var first = new FakeSource("Spotify", "Spotify");
        var second = new FakeSource("Local", "Local") { ConnectShouldThrow = true };
        registry.Register(first);
        registry.Register(second);
        var coordinator = BuildCoordinator(registry, "Spotify");
        coordinator.TryRestoreSessionAsync().GetAwaiter().GetResult();

        coordinator.SetActiveSourceAsync("Local").GetAwaiter().GetResult();

        Assert.Same(first, coordinator.ActiveSource);
    }
}
```

- [ ] **Step 2: Run test to confirm it fails (compile)**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackCoordinatorActiveSourceTests"
```

Expected: build error referencing `ILyricsService` or the new constructor signature. (The tests should pass after Task 9 already landed the new coordinator, but the `ILyricsService` fake exposes a contract — proceed only if compile succeeds. If `ILyricsService` differs, adjust the fake method bodies to match the interface in `src/ABLyrics.App/Services/Lyrics/ILyricsService.cs`.)

- [ ] **Step 3: Run test to confirm it passes**

Run:

```bash
dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj -c Debug --filter "FullyQualifiedName~PlaybackCoordinatorActiveSourceTests"
```

Expected: 4 passed.

- [ ] **Step 4: Commit**

```bash
git add tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs
git commit -m "test(playback): cover coordinator active-source transitions"
```

---

## Task 12: Update README usage steps

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Replace the login instruction paragraph**

In `README.md`, replace lines 96–101 (the bullet list under `## 使用步骤`) with:

```markdown
## 使用步骤

1. 在 [Spotify Dashboard](https://developer.spotify.com/dashboard) 创建应用
2. 添加 Redirect URI：`http://127.0.0.1:48721/callback`
3. 将 Client ID 填入 `appsettings.json`
4. 运行应用 → 系统托盘出现图标 → 右键 **设置… → 播放来源 → 连接 Spotify**
5. 在 Spotify 客户端播放音乐即可看到同步歌词
6. 右键托盘图标可管理窗口显示、切换来源
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: update README usage steps for new playback source page"
```

---

## Task 13: Final verification

**Files:** none

- [ ] **Step 1: Full build**

Run:

```bash
dotnet build ABLyrics.sln -c Debug
```

Expected: `Build succeeded`, zero errors.

- [ ] **Step 2: Full test run**

Run:

```bash
dotnet test ABLyrics.sln -c Debug
```

Expected: all tests pass.

- [ ] **Step 3: Manual smoke check (document only)**

Open `src/ABLyrics.App/appsettings.json`, leave `Spotify.ClientId` empty, and confirm:

- Tray context menu no longer contains "登录 Spotify".
- Settings window opens via "设置…" and exposes a "播放来源" page.
- `Playback.ActiveSource` is present in the JSON.

Note: a full interactive OAuth flow is **not** required at this stage; verify only structural changes.

- [ ] **Step 4: Final commit (only if Step 1 surfaced fixes)**

If any fix was applied during Step 1, commit with:

```bash
git add -A
git commit -m "chore: final fixes from verification"
```

Otherwise, no commit is needed.