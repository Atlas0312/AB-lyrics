# Local Lyrics Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users fill missing entries in the local lyrics library via an automatic file picker when switching to the Local source, a manual entry on the AppBar source menu, and an "open library folder" shortcut — with an opt-out toggle in the StyleSettingsWindow.

**Architecture:** Extend `PlaybackCoordinator` with a `LocalLyricsMissing` event and a per-track dedup set guarded by a `LyricsBehaviorService` opt-out switch. Add a new "歌词" page to `StyleSettingsWindow` bound to `LyricsBehaviorService`. Modify `AppBarWindow` to show `Local ▸ 导入歌词文件…` and `Local ▸ 打开歌词库文件夹` submenu items and subscribe to the event. Reuse the existing `LocalLyricsProvider.ImportAsync` (already in the codebase) and `LibraryPath` properties — no changes to `LocalLyricsProvider` itself.

**Tech Stack:** .NET 8, WPF, WPF-UI (lepoco) 3.1.1, xUnit, Microsoft.Win32.OpenFileDialog (built-in).

---

## File Map

**New files (production):**
- `src/ABLyrics.App/Configuration/LyricsBehaviorSettings.cs` — settings POCO with `Clone()`.
- `src/ABLyrics.App/Configuration/LyricsBehaviorStore.cs` — JSON store at `%LOCALAPPDATA%/ABLyrics/lyrics-behavior.json`.
- `src/ABLyrics.App/Services/LyricsBehaviorService.cs` — `Current` + `Update()` + `SettingsChanged` event.

**New files (tests):**
- `tests/ABLyrics.App.Tests/LocalLyricsProviderTests.cs`
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorLocalMissingPromptTests.cs`
- `tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs`

**Modified:**
- `src/ABLyrics.App/Services/PlaybackCoordinator.cs` — new constructor param, event, dedup set, behavior switch.
- `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs` — add `LyricsTag`.
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml` — add `LyricsPage` ScrollViewer + nav item.
- `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs` — load/save lyrics behavior, route the new page.
- `src/ABLyrics.App/Views/AppBarWindow.xaml.cs` — hierarchical `Local` submenu + file picker + open folder.
- `src/ABLyrics.App/App.xaml.cs` — initialize `LyricsBehavior`, expose `GetLocalLyricsProvider`, pass service into coordinator.
- `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs` — assert `KnownTags.Count == 7` and `Resolve("lyrics") == "LyricsPage"`.
- `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs` — pass `LyricsBehavior` into helper.
- `README.md` — update usage steps section.

**Project conventions:**
- `dotnet build` and `dotnet test` from the repository root.
- Tests use xUnit; internals are visible to the test project via `InternalsVisibleTo`.
- `LyricsBehaviorService` mirrors `DisplaySettingsService` exactly.
- All commits use the project's `feat(...):` / `docs:` / `refactor(...):` style.

---

## Task 1: Add LyricsBehaviorSettings POCO

**Files:**
- Create: `src/ABLyrics.App/Configuration/LyricsBehaviorSettings.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace ABLyrics.App.Configuration;

public sealed class LyricsBehaviorSettings
{
    public bool PromptForLocalLyricsOnMissing { get; set; } = true;

    public LyricsBehaviorSettings Clone()
    {
        return new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = PromptForLocalLyricsOnMissing,
        };
    }
}
```

- [ ] **Step 2: Build to confirm compilation**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/Configuration/LyricsBehaviorSettings.cs
git commit -m "feat(lyrics): add LyricsBehaviorSettings POCO"
```

---

## Task 2: Add LyricsBehaviorStore with round-trip + corruption tests

**Files:**
- Create: `src/ABLyrics.App/Configuration/LyricsBehaviorStore.cs`
- Create: `tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs`:

```csharp
using System.IO;
using ABLyrics.App.Configuration;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsBehaviorStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public LyricsBehaviorStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "lyrics-behavior.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var defaults = new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false };
        var loaded = new LyricsBehaviorStore(_path).Load(defaults);
        Assert.True(loaded.PromptForLocalLyricsOnMissing);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsFalse()
    {
        var store = new LyricsBehaviorStore(_path);
        store.Save(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false });
        var loaded = store.Load(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });
        Assert.False(loaded.PromptForLocalLyricsOnMissing);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaults()
    {
        File.WriteAllText(_path, "{ not json");
        var defaults = new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = false };
        var loaded = new LyricsBehaviorStore(_path).Load(defaults);
        Assert.True(loaded.PromptForLocalLyricsOnMissing);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~LyricsBehaviorStoreTests" -nologo -v minimal`
Expected: Build fails because `LyricsBehaviorStore` does not exist.

- [ ] **Step 3: Implement `LyricsBehaviorStore`**

Create `src/ABLyrics.App/Configuration/LyricsBehaviorStore.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

public sealed class LyricsBehaviorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public LyricsBehaviorStore() : this(DefaultPath) { }

    public LyricsBehaviorStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "lyrics-behavior.json");

    public LyricsBehaviorSettings Load(LyricsBehaviorSettings defaults)
    {
        try
        {
            if (!File.Exists(_path)) return defaults.Clone();
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<LyricsBehaviorSettings>(json, JsonOptions);
            return loaded ?? defaults.Clone();
        }
        catch
        {
            return defaults.Clone();
        }
    }

    public void Save(LyricsBehaviorSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~LyricsBehaviorStoreTests" -nologo -v minimal`
Expected: 3 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/ABLyrics.App/Configuration/LyricsBehaviorStore.cs tests/ABLyrics.App.Tests/LyricsBehaviorStoreTests.cs
git commit -m "feat(lyrics): add LyricsBehaviorStore with round-trip tests"
```

---

## Task 3: Add LyricsBehaviorService

**Files:**
- Create: `src/ABLyrics.App/Services/LyricsBehaviorService.cs`

- [ ] **Step 1: Create the file**

```csharp
using ABLyrics.App.Configuration;

namespace ABLyrics.App.Services;

public sealed class LyricsBehaviorService
{
    public event EventHandler<LyricsBehaviorSettings>? SettingsChanged;

    public LyricsBehaviorSettings Current { get; private set; }

    public LyricsBehaviorService(LyricsBehaviorSettings defaults)
    {
        Current = new LyricsBehaviorStore().Load(defaults);
    }

    public LyricsBehaviorService(LyricsBehaviorStore store, LyricsBehaviorSettings defaults)
    {
        Current = store.Load(defaults);
    }

    public void Update(LyricsBehaviorSettings settings)
    {
        Current = settings.Clone();
        new LyricsBehaviorStore().Save(Current);
        SettingsChanged?.Invoke(this, Current);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/Services/LyricsBehaviorService.cs
git commit -m "feat(lyrics): add LyricsBehaviorService"
```

---

## Task 4: Add LocalLyricsProvider tests (regression coverage)

**Files:**
- Create: `tests/ABLyrics.App.Tests/LocalLyricsProviderTests.cs`

`LocalLyricsProvider` is `internal`; `InternalsVisibleTo` already exposes it.

- [ ] **Step 1: Write tests**

```csharp
using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using Xunit;

namespace ABLyrics.App.Tests;

public class LocalLyricsProviderTests : IDisposable
{
    private readonly string _dir;

    public LocalLyricsProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private LocalLyricsProvider NewProvider()
    {
        var settings = new AppSettings();
        settings.Lyrics.LocalPath = _dir;
        return new LocalLyricsProvider(settings);
    }

    private static TrackInfo Track(string id = "t1") => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
    };

    [Fact]
    public void LibraryPath_UsesConfiguredDirectory()
    {
        var provider = NewProvider();
        Assert.Equal(_dir, provider.LibraryPath);
    }

    [Fact]
    public void GetAsync_ReturnsNullWhenMissing()
    {
        var provider = NewProvider();
        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.Null(result);
    }

    [Fact]
    public void ImportAsync_CopiesFileWithExpectedName()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "[00:01.00] hello");

        provider.ImportAsync(source, Track()).GetAwaiter().GetResult();

        var dest = Path.Combine(_dir, "Artist - Song.lrc");
        Assert.True(File.Exists(dest));
        Assert.Equal("[00:01.00] hello", File.ReadAllText(dest));
    }

    [Fact]
    public void ImportAsync_OverwritesExisting()
    {
        var provider = NewProvider();
        var dest = Path.Combine(_dir, "Artist - Song.lrc");
        File.WriteAllText(dest, "old content");
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "new content");

        provider.ImportAsync(source, Track()).GetAwaiter().GetResult();

        Assert.Equal("new content", File.ReadAllText(dest));
    }

    [Fact]
    public void ImportAsync_SanitizesInvalidChars()
    {
        var provider = NewProvider();
        var source = Path.Combine(_dir, "source.lrc");
        File.WriteAllText(source, "x");

        provider.ImportAsync(source, new TrackInfo
        {
            Id = "t2",
            Name = "Bad/Name:1",
            Artist = "A|B",
        }).GetAwaiter().GetResult();

        var expected = Path.Combine(_dir, "A_B - Bad_Name_1.lrc");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void GetAsync_ReadsContentWhenPresent()
    {
        var provider = NewProvider();
        File.WriteAllText(Path.Combine(_dir, "Artist - Song.lrc"), "lyrics body");

        var result = provider.GetAsync(Track()).GetAwaiter().GetResult();
        Assert.NotNull(result);
        Assert.Equal("Local", result!.Source);
        Assert.Equal("lyrics body", result.SyncedLyrics);
        Assert.Equal("lyrics body", result.PlainLyrics);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~LocalLyricsProviderTests" -nologo -v minimal`
Expected: 6 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add tests/ABLyrics.App.Tests/LocalLyricsProviderTests.cs
git commit -m "test(lyrics): cover LocalLyricsProvider import and lookup"
```

---

## Task 5: Extend PlaybackCoordinator with event + dedup + behavior gate

**Files:**
- Modify: `src/ABLyrics.App/Services/PlaybackCoordinator.cs`
- Modify: `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs`

- [ ] **Step 1: Write the failing tests for the prompt event**

Create `tests/ABLyrics.App.Tests/PlaybackCoordinatorLocalMissingPromptTests.cs`:

```csharp
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;
using ABLyrics.App.Services.Playback;
using Xunit;

namespace ABLyrics.App.Tests;

public class PlaybackCoordinatorLocalMissingPromptTests
{
    private sealed class FakeSource : IPlaybackSource
    {
        public string Id { get; init; } = "Spotify";
        public string DisplayName { get; init; } = "Spotify";
        public bool IsAvailable { get; init; } = true;
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }
        public void Disconnect() => IsConnected = false;
        public Task<PlaybackState?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<PlaybackState?>(null);
        public event Action<PlaybackState?>? SnapshotChanged;
    }

    private sealed class FakeLyricsService : ILyricsService
    {
        public Dictionary<string, LyricsResult?> Responses { get; } = new();
        public IReadOnlyList<string> AvailableSources => new[] { "LRCLIB", "Local" };
        public int CallCount { get; private set; }

        public Task<LyricsResult?> FetchLyricsAsync(TrackInfo track, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<LyricsResult?>(null);
        }

        public Task<LyricsResult?> FetchFromSourceAsync(TrackInfo track, string source, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Responses.TryGetValue($"{source}:{track.Id}", out var v) ? v : null);
        }
    }

    private static TrackInfo Track(string id) => new()
    {
        Id = id,
        Name = "Song",
        Artist = "Artist",
        DurationMs = 1000,
    };

    private static PlaybackCoordinator Build(
        FakeLyricsService lyrics,
        PlaybackSourceRegistry registry,
        LyricsBehaviorService behavior)
    {
        registry.Register(new FakeSource { Id = "Spotify", DisplayName = "Spotify" });
        return new PlaybackCoordinator(
            registry,
            "Spotify",
            lyrics,
            new DisplaySettingsService(new DisplayStyleSettings()),
            behavior);
    }

    [Fact]
    public async Task LocalSource_MissingLyrics_RaisesEventOnce()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<TrackInfo>();
        coordinator.LocalLyricsMissing += t => raised.Add(t);

        await coordinator.SetSourceAsync("Local");
        var track = Track("t1");
        coordinator.InjectLoadedTrack(track); // helper added in step 3
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Single(raised);
        Assert.Equal("t1", raised[0].Id);
    }

    [Fact]
    public async Task LocalSource_SameTrackReloadedTwice_RaisesOnlyOnce()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task LocalSource_DifferentTracks_RaisesForEach()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        coordinator.InjectLoadedTrack(Track("t2"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(new[] { "t1", "t2" }, raised);
    }

    [Fact]
    public async Task NonLocalSource_MissingLyrics_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_PresentLyrics_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        lyrics.Responses["Local:t1"] = new LyricsResult { Source = "Local", PlainLyrics = "x" };
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOff_DoesNotRaise()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = false,
        });
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOff_StillTracksInDedupSet()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = false,
        });
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();

        // Now turn the prompt back on. Same track should NOT raise again.
        behavior.Update(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task LocalSource_BehaviorOnAfterOff_RaisesForNewTracksOnly()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = false,
        });
        var coordinator = Build(lyrics, registry, behavior);
        var raised = new List<string>();
        coordinator.LocalLyricsMissing += t => raised.Add(t.Id);

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        behavior.Update(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });

        coordinator.InjectLoadedTrack(Track("t2"));
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(new[] { "t2" }, raised);
    }

    [Fact]
    public async Task LocalSource_ClearLyrics_AllowsRetrigger()
    {
        var lyrics = new FakeLyricsService();
        var registry = new PlaybackSourceRegistry();
        var behavior = new LyricsBehaviorService(new LyricsBehaviorSettings());
        var coordinator = Build(lyrics, registry, behavior);
        var raised = 0;
        coordinator.LocalLyricsMissing += _ => raised++;

        await coordinator.SetSourceAsync("Local");
        coordinator.InjectLoadedTrack(Track("t1"));
        await coordinator.ReloadCurrentTrackAsync();
        coordinator.ClearLocalPromptState(); // helper added in step 3
        await coordinator.ReloadCurrentTrackAsync();

        Assert.Equal(2, raised);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~PlaybackCoordinatorLocalMissingPromptTests" -nologo -v minimal`
Expected: Build fails because `LyricsBehaviorService`, `LocalLyricsMissing`, `InjectLoadedTrack`, `ClearLocalPromptState` are missing.

- [ ] **Step 3: Extend `PlaybackCoordinator`**

Modify `src/ABLyrics.App/Services/PlaybackCoordinator.cs`:

1. Add field near the top of the class:

```csharp
private readonly HashSet<string> _localPromptedTrackIds = new();
private readonly object _localPromptLock = new();
private readonly LyricsBehaviorService _lyricsBehavior;
```

2. Add constructor parameter (between `lyricsService` and `displaySettings`):

```csharp
public PlaybackCoordinator(
    PlaybackSourceRegistry registry,
    string initialSourceId,
    ILyricsService lyricsService,
    LyricsBehaviorService lyricsBehavior,
    DisplaySettingsService? displaySettings = null)
```

3. Assign it in the constructor:

```csharp
_lyricsBehavior = lyricsBehavior;
_lyricsBehavior.SettingsChanged += (_, _) => { /* read each call */ };
```

4. Add the public event:

```csharp
public event Action<TrackInfo>? LocalLyricsMissing;
```

5. Modify `ReloadCurrentTrackAsync` to read the current track, fetch lyrics, and raise the event when missing. Replace the body with:

```csharp
private async Task ReloadCurrentTrackAsync()
{
    if (_loadedTrackId is null) return;

    var currentTrack = new TrackInfo
    {
        Id = _loadedTrackId,
        Name = TrackTitle,
        Artist = ArtistName,
        DurationMs = _trackDurationMs,
    };

    LyricsResult? lyrics;
    if (!string.IsNullOrWhiteSpace(_lyricsActiveSource))
    {
        if (_lyricsActiveSource != "Local")
        {
            StatusText = $"正在尝试 {_lyricsActiveSource}…";
            LoadingFlash?.Invoke();
        }
        lyrics = await _lyricsService.FetchFromSourceAsync(currentTrack, _lyricsActiveSource).ConfigureAwait(false);
    }
    else
    {
        StatusText = "正在尝试 LRCLIB…";
        LoadingFlash?.Invoke();
        lyrics = await _lyricsService.FetchLyricsAsync(currentTrack).ConfigureAwait(false);
    }

    _syncEngine.SetDurationMs(_trackDurationMs);
    _syncEngine.Load(lyrics);
    LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_lyricsActiveSource) ? string.Empty : _lyricsActiveSource);
    StatusText = lyrics is null ? "暂无歌词" : string.Empty;

    if (_lyricsActiveSource == "Local" && lyrics is null)
    {
        bool added;
        lock (_localPromptLock)
        {
            added = _localPromptedTrackIds.Add(currentTrack.Id);
        }
        if (added && _lyricsBehavior.Current.PromptForLocalLyricsOnMissing)
        {
            LocalLyricsMissing?.Invoke(currentTrack);
        }
    }
}
```

6. Modify `ClearLyrics` to clear the dedup set:

```csharp
private void ClearLyrics()
{
    ClearLyricLines();
    _syncEngine.Load(null);
    lock (_localPromptLock)
    {
        _localPromptedTrackIds.Clear();
    }
}
```

7. Add test-only helpers at the end of the class (visible to test project via `InternalsVisibleTo`):

```csharp
internal void InjectLoadedTrack(TrackInfo track)
{
    _loadedTrackId = track.Id;
    _trackDurationMs = track.DurationMs;
    TrackTitle = track.Name;
    ArtistName = track.Artist;
}

internal void ClearLocalPromptState()
{
    lock (_localPromptLock)
    {
        _localPromptedTrackIds.Clear();
    }
}
```

- [ ] **Step 4: Update existing `PlaybackCoordinatorActiveSourceTests.cs`**

Modify `tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs` to pass a `LyricsBehaviorService` into the helper. Replace the `BuildCoordinator` method:

```csharp
private static PlaybackCoordinator BuildCoordinator(PlaybackSourceRegistry registry, string initialId)
{
    return new PlaybackCoordinator(
        registry,
        initialId,
        new FakeLyricsService(),
        new LyricsBehaviorService(new LyricsBehaviorSettings()),
        new DisplaySettingsService(new DisplayStyleSettings()));
}
```

Add the new using directives at the top of the test file (after existing usings):

```csharp
using ABLyrics.App.Configuration;
using ABLyrics.App.Services.Lyrics;
```

(The `using ABLyrics.App.Services.Lyrics;` line may already exist — keep one copy.)

- [ ] **Step 5: Run the new tests**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~PlaybackCoordinatorLocalMissingPromptTests|FullyQualifiedName~PlaybackCoordinatorActiveSourceTests" -nologo -v minimal`
Expected: All tests pass.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test ABLyrics.sln -nologo -v minimal`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/ABLyrics.App/Services/PlaybackCoordinator.cs tests/ABLyrics.App.Tests/PlaybackCoordinatorLocalMissingPromptTests.cs tests/ABLyrics.App.Tests/PlaybackCoordinatorActiveSourceTests.cs
git commit -m "feat(lyrics): raise LocalLyricsMissing event with dedup and behavior gate"
```

---

## Task 6: Add LyricsTag to StyleSettingsTabRouter

**Files:**
- Modify: `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs`
- Modify: `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`

- [ ] **Step 1: Update router**

Edit `src/ABLyrics.App/Views/StyleSettingsTabRouter.cs`:

1. Add constant after `PlaybackSourceTag`:

```csharp
public const string LyricsTag = "lyrics";
```

2. Update `KnownTags`:

```csharp
public static readonly IReadOnlyCollection<string> KnownTags = new[]
{
    AppearanceTag,
    LayoutTag,
    ColorTag,
    SyncTag,
    AboutTag,
    PlaybackSourceTag,
    LyricsTag,
};
```

3. Update `Resolve`:

```csharp
return tag switch
{
    AppearanceTag => "AppearancePage",
    LayoutTag => "LayoutPage",
    ColorTag => "ColorPage",
    SyncTag => "SyncPage",
    AboutTag => "AboutPage",
    PlaybackSourceTag => "PlaybackSourcePage",
    LyricsTag => "LyricsPage",
    _ => null,
};
```

- [ ] **Step 2: Update tests**

Edit `tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs`:

1. Add a new `[InlineData]` row to `Resolve_KnownTag_ReturnsMatchingPageName`:

```csharp
[InlineData(StyleSettingsTabRouter.LyricsTag, "LyricsPage")]
```

2. Update `KnownTags_ContainsExactlySixTags` to expect seven and include the new tag:

```csharp
[Fact]
public void KnownTags_ContainsExactlySevenTags()
{
    Assert.Equal(7, StyleSettingsTabRouter.KnownTags.Count);
    Assert.Contains(StyleSettingsTabRouter.AppearanceTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.LayoutTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.ColorTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.SyncTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.AboutTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.PlaybackSourceTag, StyleSettingsTabRouter.KnownTags);
    Assert.Contains(StyleSettingsTabRouter.LyricsTag, StyleSettingsTabRouter.KnownTags);
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/ABLyrics.App.Tests/ABLyrics.App.Tests.csproj --filter "FullyQualifiedName~StyleSettingsTabRouterTests" -nologo -v minimal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Views/StyleSettingsTabRouter.cs tests/ABLyrics.App.Tests/StyleSettingsTabRouterTests.cs
git commit -m "feat(settings): add Lyrics tab route"
```

---

## Task 7: Wire App.xaml.cs

**Files:**
- Modify: `src/ABLyrics.App/App.xaml.cs`

- [ ] **Step 1: Update App.xaml.cs**

Add the static property and accessor (near other `public static` members at the top):

```csharp
public static LyricsBehaviorService LyricsBehavior { get; private set; } = null!;

public static Services.Lyrics.LocalLyricsProvider GetLocalLyricsProvider()
{
    var app = (App)Current;
    return new Services.Lyrics.LocalLyricsProvider(app.Settings);
}
```

Initialize the service inside `OnStartup` after `Coordinator = new PlaybackCoordinator(...)`:

```csharp
LyricsBehavior = new LyricsBehaviorService(new LyricsBehaviorSettings { PromptForLocalLyricsOnMissing = true });
Coordinator = new PlaybackCoordinator(
    _sourceRegistry,
    Settings.Playback.ActiveSource,
    lyricsService,
    LyricsBehavior,
    DisplaySettings);
```

(Reorder so `LyricsBehavior` is constructed before `PlaybackCoordinator`.)

Add `using ABLyrics.App.Configuration;` at the top if not already present.

- [ ] **Step 2: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/App.xaml.cs
git commit -m "feat(app): initialize LyricsBehaviorService and expose LocalLyricsProvider"
```

---

## Task 8: Add LyricsPage to StyleSettingsWindow

**Files:**
- Modify: `src/ABLyrics.App/Views/StyleSettingsWindow.xaml`
- Modify: `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`

- [ ] **Step 1: Update XAML**

In `src/ABLyrics.App/Views/StyleSettingsWindow.xaml`:

1. Add the new nav item after the playback-source row:

```xml
<ui:NavigationViewItem Content="歌词" TargetPageTag="lyrics" Click="OnMenuItemClick" />
```

2. Add the new page (place it next to the playback source page, before the About page ScrollViewer):

```xml
<!-- 歌词页 -->
<ScrollViewer x:Name="LyricsPage" Visibility="Collapsed"
              VerticalScrollBarVisibility="Auto" Padding="24">
    <StackPanel>
        <TextBlock Text="歌词" FontSize="20" FontWeight="SemiBold" Margin="0,0,0,16" />
        <ui:Card Padding="20" Margin="0,0,0,16">
            <StackPanel>
                <TextBlock Text="本地歌词" FontWeight="SemiBold" Margin="0,0,0,8" />
                <DockPanel>
                    <ui:ToggleSwitch x:Name="PromptLocalMissingSwitch"
                                     DockPanel.Dock="Right"
                                     Content="缺失时弹出选择对话框"
                                     Toggled="OnPromptLocalMissingToggled" />
                </DockPanel>
                <TextBlock TextWrapping="Wrap" Opacity="0.7" FontSize="12" Margin="0,8,0,0"
                           Text="切换到本地歌词源时，若在歌词库找不到当前曲目，会弹出文件选择框。关闭后不再提示，需要时可使用顶部 AppBar 菜单「Local ▸ 导入歌词文件…」。" />
            </StackPanel>
        </ui:Card>
    </StackPanel>
</ScrollViewer>
```

- [ ] **Step 2: Update code-behind**

In `src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs`:

1. Add `using ABLyrics.App.Configuration;` and `using ABLyrics.App.Services;` if not present.

2. Add a field for the lyrics behavior service in the constructor section:

```csharp
private readonly LyricsBehaviorService _lyricsBehavior;

public StyleSettingsWindow(
    DisplaySettingsService displaySettings,
    PlaybackCoordinator coordinator,
    LyricsBehaviorService lyricsBehavior)
{
    _displaySettings = displaySettings;
    _coordinator = coordinator;
    _lyricsBehavior = lyricsBehavior;
    _fontChoices = SystemFontCatalog.GetInstalledFontFamilies();

    try
    {
        InitializeComponent();
        FontFamilyCombo.ItemsSource = _fontChoices;
        WireEvents();
        LoadFrom(_displaySettings.Current);
        _sourceRegistry = App.GetPlaybackSourceRegistry();
        SetCoordinatorReference(_coordinator);
        _coordinator.SourceStateChanged += () => Dispatcher.BeginInvoke(RefreshPlaybackSourcePanel);
        LoadLyricsBehaviorFrom(_lyricsBehavior.Current);
        RefreshPlaybackSourcePanel();
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "初始化样式设置窗口失败");
        throw;
    }
}
```

3. Update `OnMenuItemClick` to also collapse/show the new page:

```csharp
LyricsPage.Visibility = Visibility.Collapsed;
```

And in the switch:

```csharp
case nameof(LyricsPage): LyricsPage.Visibility = Visibility.Visible; break;
```

4. Add new methods:

```csharp
private void LoadLyricsBehaviorFrom(LyricsBehaviorSettings settings)
{
    _isLoading = true;
    try
    {
        PromptLocalMissingSwitch.IsOn = settings.PromptForLocalLyricsOnMissing;
    }
    finally
    {
        _isLoading = false;
    }
}

private void OnPromptLocalMissingToggled(object sender, RoutedEventArgs e)
{
    if (_isLoading) return;
    try
    {
        _lyricsBehavior.Update(new LyricsBehaviorSettings
        {
            PromptForLocalLyricsOnMissing = PromptLocalMissingSwitch.IsOn,
        });
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "保存歌词行为失败");
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Views/StyleSettingsWindow.xaml src/ABLyrics.App/Views/StyleSettingsWindow.xaml.cs
git commit -m "feat(settings): add Lyrics page with local-import prompt toggle"
```

---

## Task 9: Update App.OnStyleSettingsClick to pass the service

**Files:**
- Modify: `src/ABLyrics.App/App.xaml.cs`

- [ ] **Step 1: Update the call site**

Replace the existing `OnStyleSettingsClick`:

```csharp
private void OnStyleSettingsClick()
{
    try
    {
        var window = new StyleSettingsWindow(DisplaySettings, Coordinator, LyricsBehavior);
        window.ShowDialog();
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "打开样式设置失败");
        if (!DevExceptionReporter.IsEnabled)
        {
            System.Windows.MessageBox.Show(ex.Message, "样式设置", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ABLyrics.App/App.xaml.cs
git commit -m "feat(app): pass LyricsBehavior to StyleSettingsWindow"
```

---

## Task 10: Wire AppBarWindow menu, prompt, and open-folder actions

**Files:**
- Modify: `src/ABLyrics.App/Views/AppBarWindow.xaml.cs`

- [ ] **Step 1: Update AppBarWindow.xaml.cs**

Replace the existing class. Changes:

1. Add field and constructor wiring:

```csharp
private readonly Services.Lyrics.LocalLyricsProvider _localProvider;
private readonly System.Collections.Generic.HashSet<string> _alreadyPrompted = new();

public AppBarWindow(PlaybackCoordinator coordinator, DisplaySettingsService displaySettings)
{
    InitializeComponent();
    _coordinator = coordinator;
    _localProvider = App.GetLocalLyricsProvider();
    DataContext = coordinator;

    _coordinator.LocalLyricsMissing += OnLocalLyricsMissing;

    _sourceMenu.ItemClicked += (_, e) =>
    {
        if (e.ClickedItem?.Tag is string source)
        {
            _ = _coordinator.SetSourceAsync(source);
        }
    };

    _lifecycle = new LyricsHostLifecycle(
        this,
        coordinator,
        displaySettings,
        ApplyStyle,
        ApplyLayout,
        ChromeBorder,
        onClosed: () =>
        {
            _appBarController?.Dispose();
            _appBarController = null;
        });

    TrackInfoPanel.MouseLeftButtonDown += OnTrackInfoMouseLeftButtonDown;
    PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
    SourceInitialized += OnSourceInitialized;
}
```

2. Replace `OnSourceTagClick` to build the hierarchical menu:

```csharp
private void OnSourceTagClick(object sender, RoutedEventArgs e)
{
    _sourceMenu.Items.Clear();

    foreach (var source in _coordinator.AvailableSources)
    {
        var top = new Forms.ToolStripMenuItem(source)
        {
            Tag = source,
            Checked = source == _coordinator.ActiveSource,
        };

        if (source == "Local")
        {
            var trackId = _coordinator.GetCurrentTrackId();
            var importItem = new Forms.ToolStripMenuItem("导入歌词文件…")
            {
                Enabled = !string.IsNullOrEmpty(trackId),
            };
            importItem.Click += (_, _) => _ = PromptForLocalLyricsAsync(GetCurrentTrack());
            top.DropDownItems.Add(importItem);

            var openFolderItem = new Forms.ToolStripMenuItem("打开歌词库文件夹");
            openFolderItem.Click += (_, _) => OpenLocalLyricsLibrary();
            top.DropDownItems.Add(openFolderItem);
        }

        _sourceMenu.Items.Add(top);
    }

    _sourceMenu.Show(GetSourceTagScreenPos());
}
```

3. Add the new methods:

```csharp
private void OnLocalLyricsMissing(Models.TrackInfo track)
{
    Dispatcher.BeginInvoke(() => _ = PromptForLocalLyricsAsync(track));
}

private Models.TrackInfo GetCurrentTrack()
{
    return new Models.TrackInfo
    {
        Id = _coordinator.GetCurrentTrackId() ?? string.Empty,
        Name = _coordinator.TrackTitle,
        Artist = _coordinator.ArtistName,
    };
}

private async Task PromptForLocalLyricsAsync(Models.TrackInfo track)
{
    if (string.IsNullOrEmpty(track.Id)) return;

    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = $"为 {track.Artist} - {track.Name} 选择本地歌词",
        Filter = "LRC 歌词 (*.lrc)|*.lrc|文本歌词 (*.txt)|*.txt|所有文件 (*.*)|*.*",
        FilterIndex = 1,
        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        CheckFileExists = true,
        Multiselect = false,
    };

    if (dialog.ShowDialog() != true) return;

    try
    {
        await _localProvider.ImportAsync(dialog.FileName, track);
        if (_coordinator.ActiveSource != "Local")
        {
            await _coordinator.SetSourceAsync("Local");
        }
        else
        {
            await _coordinator.ForceReloadAsync();
        }
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "导入本地歌词失败");
    }
}

private void OpenLocalLyricsLibrary()
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_localProvider.LibraryPath}\"")
        {
            UseShellExecute = true,
        });
    }
    catch (Exception ex)
    {
        DevExceptionReporter.Show(ex, "打开歌词库失败");
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Run full test suite**

Run: `dotnet test ABLyrics.sln -nologo -v minimal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/ABLyrics.App/Views/AppBarWindow.xaml.cs
git commit -m "feat(appbar): Local submenu with import lyrics and open library folder"
```

---

## Task 11: Update README usage section

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Find and update the usage section**

Open `README.md`, locate the "使用步骤" section, and append the following paragraph at the end of that section (or the most relevant subsection):

```markdown
- **本地歌词库**：在 `设置 → 歌词` 中可开启/关闭"缺失时弹出选择对话框"。开启后，切到本地歌词源且库内找不到当前曲目时会自动弹出文件选择框；也可在 AppBar 顶部点歌词源标签 → `Local ▸ 导入歌词文件…` 随时手动导入，或 `Local ▸ 打开歌词库文件夹` 直接打开本地歌词目录（默认 `%APPDATA%/ABLyrics/lyrics`）。
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: document local lyrics import flow and toggle"
```

---

## Task 12: Final verification

- [ ] **Step 1: Build**

Run: `dotnet build ABLyrics.sln -nologo -v minimal`
Expected: Build succeeded.

- [ ] **Step 2: Test**

Run: `dotnet test ABLyrics.sln -nologo -v minimal`
Expected: All tests pass (existing + new).

- [ ] **Step 3: Manual smoke check**

If you have a local runtime available:
1. Launch the app and confirm the AppBar lyrics source menu still shows `LRCLIB / Netease / Local`.
2. Right-click `Local` and verify the submenu shows `导入歌词文件…` (greyed when no track) and `打开歌词库文件夹`.
3. Open `设置 → 歌词` and confirm the toggle is present and on; flip it, restart the app, and confirm the value persists.
4. (Optional) point `LocalPath` in `appsettings.json` at a temporary empty folder, switch to Local, and confirm the picker fires.

- [ ] **Step 4: Final commit (if any verification fixes needed)**

```bash
git add -A
git commit -m "chore: address verification findings"
```

---

## Self-Review Checklist

- [x] Spec coverage: prompt event (T5), file picker UI (T10), open-folder UI (T10), settings toggle (T8), persistence (T2), dedup (T5), runtime sync (T5 + T8), README (T11).
- [x] No placeholders: every step has the actual code or command.
- [x] Type consistency: `LyricsBehaviorService`, `LyricsBehaviorSettings`, `LocalLyricsMissing`, `InjectLoadedTrack`, `ClearLocalPromptState`, `PromptLocalMissingSwitch` consistent across tasks.
- [x] Backwards-compat: `PlaybackCoordinator` constructor is updated in T5; existing test helper updated in same task; `StyleSettingsWindow` constructor is updated in T8 and `App.OnStyleSettingsClick` updated in T9 to match.