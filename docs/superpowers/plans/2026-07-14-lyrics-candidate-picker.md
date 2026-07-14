---
title: 歌词库内版本选择/切换窗口 - 实施计划
date: 2026-07-14
parent-spec: ../specs/2026-07-14-lyrics-candidate-picker-design.md
---

# 歌词库内版本选择/切换窗口 - 实施计划

## 总览

按 spec 实现选版本窗口。整体顺序：基础类型 → 持久化层 → 数据源搜索层 → 聚合服务
→ 现有服务扩展 → 业务协调 → UI 窗口 → 集成到启动与右键菜单。

每步采用 TDD：先写测试（红）、再实现（绿）、必要时重构（小绿）。

## 步骤序列

### Step 1：`TrackKey`（红→绿）

文件：
- 新建：`src/ABLyrics.App/Configuration/TrackKey.cs`
- 新建：`tests/ABLyrics.App.Tests/TrackKeyTests.cs`

实现要点：

```csharp
namespace ABLyrics.App.Configuration;

public static class TrackKey
{
    public static string From(TrackInfo track) =>
        From(track.Artist, track.Album, track.Name);

    public static string From(string artist, string album, string name)
    {
        var a = Normalize(artist);
        var al = Normalize(album);
        var n = Normalize(name);
        return $"{a}||{al}||{n}";
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return string.Join(' ', s.Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
```

测试用例：
- `From_TrimsAndCollapsesWhitespace`
- `From_IsCaseInsensitive`
- `From_EmptyAlbum_KeepsSeparator`（→ `artist||||name`）
- `From_SameInputs_ProducesSameKey`
- `From_TrackInfoDelegatesCorrectly`

跑 `dotnet test --filter "FullyQualifiedName~TrackKeyTests"` 全绿。

```bash
git add tests/ABLyrics.App.Tests/TrackKeyTests.cs src/ABLyrics.App/Configuration/TrackKey.cs
git commit -m "feat: TrackKey 规范化键"
```

### Step 2：`LyricsCandidate` + `CandidateOrigin`

文件：
- 新建：`src/ABLyrics.App/Models/LyricsCandidate.cs`

无测试（纯数据 record）。代码：

```csharp
namespace ABLyrics.App.Models;

public sealed class LyricsCandidate
{
    public required string Source { get; init; }       // "Local" / "LRCLIB" / "Netease"
    public required string Label { get; init; }
    public string? SyncedLyrics { get; init; }
    public string? PlainLyrics { get; init; }
    public int DurationMs { get; init; }
    public required CandidateOrigin Origin { get; init; }
    public bool IsAvailable { get; init; } = true;
}

public abstract record CandidateOrigin
{
    public sealed record Local(string FilePath) : CandidateOrigin;
    public sealed record Lrclib(int LrclibId) : CandidateOrigin;
    public sealed record Netease(long NeteaseSongId) : CandidateOrigin;
}
```

### Step 3：`LyricsOverrideStore`（红→绿）

文件：
- 新建：`src/ABLyrics.App/Configuration/LyricsOverrideStore.cs`
- 新建：`tests/ABLyrics.App.Tests/LyricsOverrideStoreTests.cs`

实现要点：

```csharp
namespace ABLyrics.App.Configuration;

public sealed class LyricsOverrideStore
{
    public const string DefaultFileName = "lyrics-overrides.json";

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ABLyrics",
        DefaultFileName);

    public IReadOnlyDictionary<string, CandidateOrigin> Load(string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!File.Exists(p)) return new Dictionary<string, CandidateOrigin>();
        try
        {
            var json = File.ReadAllText(p);
            var dto = JsonSerializer.Deserialize<OverrideFileDto>(json);
            return dto?.Overrides is null
                ? new Dictionary<string, CandidateOrigin>()
                : dto.Overrides.ToDictionary(
                    kv => kv.Key,
                    kv => DeserializeOrigin(kv.Value));
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "覆盖项加载失败");
            return new Dictionary<string, CandidateOrigin>();
        }
    }

    public void Save(string trackKey, CandidateOrigin origin, string? path = null)
    {
        var p = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var current = Load(p).ToDictionary(kv => kv.Key, kv => kv.Value);
        current[trackKey] = origin;
        var dto = new OverrideFileDto
        {
            Version = 1,
            Overrides = current.ToDictionary(
                kv => kv.Key,
                kv => SerializeOrigin(kv.Value)),
        };
        File.WriteAllText(p, JsonSerializer.Serialize(dto,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Remove(string trackKey, string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!File.Exists(p)) return;
        var current = Load(p).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!current.Remove(trackKey)) return;
        var dto = new OverrideFileDto
        {
            Version = 1,
            Overrides = current.ToDictionary(
                kv => kv.Key,
                kv => SerializeOrigin(kv.Value)),
        };
        File.WriteAllText(p, JsonSerializer.Serialize(dto,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // DTO 与序列化辅助方法（内嵌在文件内）
    private sealed class OverrideFileDto
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("overrides")] public Dictionary<string, OriginDto> Overrides { get; set; } = new();
    }

    private sealed class OriginDto
    {
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("filePath")] public string? FilePath { get; set; }
        [JsonPropertyName("lrclibId")] public int? LrclibId { get; set; }
        [JsonPropertyName("neteaseSongId")] public long? NeteaseSongId { get; set; }
    }

    private static OriginDto SerializeOrigin(CandidateOrigin o) => o switch
    {
        CandidateOrigin.Local l => new OriginDto { Kind = "Local", FilePath = l.FilePath },
        CandidateOrigin.Lrclib l => new OriginDto { Kind = "Lrclib", LrclibId = l.LrclibId },
        CandidateOrigin.Netease n => new OriginDto { Kind = "Netease", NeteaseSongId = n.NeteaseSongId },
        _ => throw new ArgumentOutOfRangeException(nameof(o)),
    };

    private static CandidateOrigin DeserializeOrigin(OriginDto dto) => dto.Kind switch
    {
        "Local" => new CandidateOrigin.Local(dto.FilePath ?? ""),
        "Lrclib" => new CandidateOrigin.Lrclib(dto.LrclibId ?? 0),
        "Netease" => new CandidateOrigin.Netease(dto.NeteaseSongId ?? 0),
        _ => throw new InvalidDataException($"Unknown origin kind: {dto.Kind}"),
    };
}
```

测试用例（用临时目录、`Path.Combine(Path.GetTempPath(), "lyrics-override-" + Guid.NewGuid())`）：
- `Load_FileMissing_ReturnsEmpty`
- `Load_CorruptJson_ReturnsEmpty`
- `Save_AndLoad_RoundTrips_Local`
- `Save_AndLoad_RoundTrips_Lrclib`
- `Save_ExistingKey_Replaces`
- `Remove_ExistingKey_Deletes`
- `Remove_NonExistingKey_NoOp`

跑全绿。

```bash
git commit -m "feat: LyricsOverrideStore 持久化覆盖项"
```

### Step 4：`LrcLibClient.SearchAsync`（红→绿）

文件：
- 修改：`src/ABLyrics.App/Services/Lyrics/LrcLibClient.cs`
- 新建：`tests/ABLyrics.App.Tests/LrcLibClientSearchTests.cs`

修改要点：
1. 新增 `SearchAsync(trackName, artistName, albumName, cancellationToken)` 方法
2. 新增 `internal sealed class LrcLibSearchHit` DTO
3. 新增 `internal sealed class LrcLibSearchItem` JSON 反序列化目标（`[JsonPropertyName("id")]`、`trackName`、`artistName`、`albumName`、`duration`（秒数，转 ms）、`syncedLyrics`、`plainLyrics`）
4. JSON 反序列化数组 → `IReadOnlyList<LrcLibSearchHit>`

测试要点：
- `SearchAsync_ReturnsHits`：用 `HttpMessageHandler` 替身注入 `[{...}]` JSON
- `SearchAsync_NonSuccess_ReturnsEmpty`
- `SearchAsync_NetworkError_ReturnsEmpty`
- `SearchAsync_QueryStringContainsParameters`

为便于测试，`HttpClient` 通过构造函数注入（重载现有构造）。

```bash
git commit -m "feat: LrcLibClient.SearchAsync 多候选"
```

### Step 5：`LocalLyricsSearchProvider`（红→绿）

文件：
- 新建：`src/ABLyrics.App/Services/Lyrics/LocalLyricsSearchProvider.cs`
- 新建：`tests/ABLyrics.App.Tests/LocalLyricsSearchProviderTests.cs`

实现要点（与现有 `LocalLyricsProvider.FindFile` 共享 `_libraryPath`，但**不**写盘）：

```csharp
namespace ABLyrics.App.Services.Lyrics;

public sealed class LocalLyricsSearchProvider
{
    private readonly string _libraryPath;

    public LocalLyricsSearchProvider(AppSettings settings)
    {
        var configured = settings.Lyrics.LocalPath;
        _libraryPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ABLyrics", "lyrics")
            : configured;
        Directory.CreateDirectory(_libraryPath);
    }

    public IReadOnlyList<LyricsCandidate> Search(TrackInfo track)
    {
        var matches = new List<(string path, int length, bool fromFind)>();

        // 1. 现有 FindFile 命中（来自 LocalLyricsProvider 单实例）
        //    简化：直接复用 BuildFileName 的相同逻辑重新跑——避免引入 Provider 之间的耦合
        var primary = BuildFileName(track);
        var primaryHit = Directory.EnumerateFiles(_libraryPath, "*.lrc")
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), primary,
                StringComparison.OrdinalIgnoreCase));
        if (primaryHit is not null)
            matches.Add((primaryHit, Path.GetFileName(primaryHit).Length, true));

        // 2. 宽松匹配：文件名同时含 Artist 和 Name
        var artistLower = track.Artist.ToLowerInvariant();
        var nameLower = track.Name.ToLowerInvariant();
        foreach (var f in Directory.EnumerateFiles(_libraryPath, "*.lrc"))
        {
            var fname = Path.GetFileName(f).ToLowerInvariant();
            if (fname.Contains(artistLower) && fname.Contains(nameLower))
                matches.Add((f, fname.Length, false));
        }

        // 3. 去重 + 排序（文件名短度升序）+ 转 Candidate
        return matches
            .GroupBy(m => m.path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.length)
            .Select(m =>
            {
                var content = File.ReadAllText(m.path);
                return new LyricsCandidate
                {
                    Source = "Local",
                    Label = Path.GetFileNameWithoutExtension(m.path),
                    SyncedLyrics = content,
                    PlainLyrics = content,
                    DurationMs = track.DurationMs,
                    Origin = new CandidateOrigin.Local(m.path),
                };
            })
            .ToList();
    }

    private static string BuildFileName(TrackInfo track) =>
        LocalLyricsProvider.BuildFileNamePublic(track);  // 见下面注
```

> **注**：需要把 `LocalLyricsProvider.BuildFileName` 暴露为 `internal static`，给 `LocalLyricsSearchProvider` 复用。
> 或者把 `BuildFileName` 内联复制（避免破坏现有 `LocalLyricsProvider` 内部封装）。
> **推荐暴露**——加 `internal static` 即可。

测试用例：
- `Search_NoFiles_ReturnsEmpty`
- `Search_FindsArtistNameIntersection`
- `Search_OrdersByFileNameLength`
- `Search_SameFileListedOnce`

```bash
git commit -m "feat: LocalLyricsSearchProvider 宽松匹配多候选"
```

### Step 6：`LyricsSearchService`（红→绿）

文件：
- 新建：`src/ABLyrics.App/Services/Lyrics/ILyricsSearchService.cs`
- 新建：`src/ABLyrics.App/Services/Lyrics/LyricsSearchService.cs`
- 新建：`tests/ABLyrics.App.Tests/LyricsSearchServiceTests.cs`

实现要点：

```csharp
public interface ILyricsSearchService
{
    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(
        TrackInfo track, string library, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken ct = default);
}

internal sealed class LyricsSearchService : ILyricsSearchService
{
    private readonly AppSettings _settings;
    private readonly LrcLibClient _lrcLibClient;
    private readonly LocalLyricsSearchProvider _localSearch;
    private readonly NetEaseSettings _netEase;

    public LyricsSearchService(AppSettings settings)
    {
        _settings = settings;
        _lrcLibClient = new LrcLibClient(settings.Lyrics.UserAgent);
        _localSearch = new LocalLyricsSearchProvider(settings);
        _netEase = settings.NetEase;
    }

    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(
        TrackInfo track, string library, CancellationToken ct = default)
    {
        return library switch
        {
            "Local" => _localSearch.Search(track),
            "LRCLIB" => await SearchLrclibAsync(track, ct).ConfigureAwait(false),
            "Netease" => await SearchNeteaseAsync(track, ct).ConfigureAwait(false),
            _ => Array.Empty<LyricsCandidate>(),
        };
    }

    public async Task<IReadOnlyDictionary<string, bool>> ProbeAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, bool>
        {
            ["LRCLIB"] = await ProbeLrclibAsync(ct).ConfigureAwait(false),
            ["Local"] = ProbeLocal(),
        };
        if (!string.IsNullOrWhiteSpace(_netEase.MusicU))
            result["Netease"] = await ProbeNeteaseAsync(ct).ConfigureAwait(false);
        return result;
    }

    // ---- private helpers ----

    private async Task<IReadOnlyList<LyricsCandidate>> SearchLrclibAsync(
        TrackInfo track, CancellationToken ct)
    {
        var hits = await _lrcLibClient.SearchAsync(
            track.Name, track.Artist, track.Album, ct).ConfigureAwait(false);
        return hits
            .OrderBy(h => Math.Abs(h.DurationMs - track.DurationMs))
            .Select(h => new LyricsCandidate
            {
                Source = "LRCLIB",
                Label = BuildLrclibLabel(h),
                SyncedLyrics = h.SyncedLyrics,
                PlainLyrics = h.PlainLyrics,
                DurationMs = h.DurationMs,
                Origin = new CandidateOrigin.Lrclib(h.Id),
            })
            .ToList();
    }

    private async Task<IReadOnlyList<LyricsCandidate>> SearchNeteaseAsync(
        TrackInfo track, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_netEase.MusicU)) return Array.Empty<LyricsCandidate>();
        try
        {
            var api = new Api();
            var keyword = $"{track.Artist} {track.Name}".Trim();
            var search = await api.SearchNew(keyword).ConfigureAwait(false);
            var best = search?.Result?.Songs?
                .OrderBy(s => Math.Abs(s.Duration - track.DurationMs))
                .FirstOrDefault();
            if (best is null) return Array.Empty<LyricsCandidate>();
            var lyric = await api.GetLyric(best.Id).ConfigureAwait(false);
            var synced = lyric?.Lrc?.Lyric;
            return new[]
            {
                new LyricsCandidate
                {
                    Source = "Netease",
                    Label = best.Name ?? track.Name,
                    SyncedLyrics = synced,
                    PlainLyrics = synced,
                    DurationMs = best.Duration,
                    Origin = new CandidateOrigin.Netease(best.Id),
                },
            };
        }
        catch
        {
            return Array.Empty<LyricsCandidate>();
        }
    }

    private async Task<bool> ProbeLrclibAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var resp = await _lrcLibClient.ProbeAsync(cts.Token).ConfigureAwait(false);
            return resp;
        }
        catch { return false; }
    }

    private bool ProbeLocal()
    {
        try
        {
            return Directory.Exists(_settings.Lyrics.LocalPath ?? "")
                ? true  // 存在即视为可达
                : true; // LocalLyricsSearchProvider 构造时会 CreateDirectory；非空路径即可
        }
        catch { return false; }
    }

    private async Task<bool> ProbeNeteaseAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_netEase.MusicU)) return false;
        try
        {
            var api = new Api();
            await api.SearchNew("test").ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    private static string BuildLrclibLabel(LrcLibSearchHit h)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(h.AlbumName)) parts.Add(h.AlbumName);
        parts.Add(FormatDuration(h.DurationMs));
        return string.Join(" · ", parts);
    }

    private static string FormatDuration(int ms)
    {
        var s = ms / 1000;
        return $"{s / 60}:{(s % 60):D2}";
    }
}
```

> **注**：需要在 `LrcLibClient` 上加 `ProbeAsync`（HTTP HEAD/GET 测试）+ 把 `SearchAsync` 返回类型用 `internal sealed class LrcLibSearchHit` 暴露给 `LyricsSearchService`。
> `LrcLibSearchHit` 已在 Step 4 中定义。

测试要点：
- 用 `AppSettings { Lyrics = new() { LocalPath = _tmpDir }, NetEase = new() { MusicU = "" } }` 构造
- `SearchAsync_Local_ReturnsLocalCandidates`
- `SearchAsync_Lrclib_DelegatesToLrcLibClient`（Fake 注入）
- `SearchAsync_NeteaseNoMusicU_ReturnsEmpty`
- `SearchAsync_UnknownLibrary_ReturnsEmpty`
- `ProbeAsync_LrclibReachable_True` / `_False`（用 `HttpMessageHandler`）
- `ProbeAsync_LocalReachable_True`

```bash
git commit -m "feat: LyricsSearchService 聚合 + 探活"
```

### Step 7：扩展 `LyricsService.FetchCandidateAsync`

文件：
- 修改：`src/ABLyrics.App/Services/Lyrics/LyricsService.cs`
- 修改：`src/ABLyrics.App/Services/Lyrics/ILyricsService.cs`

修改要点：

```csharp
// ILyricsService.cs 新增
Task<LyricsResult?> FetchCandidateAsync(
    TrackInfo track, CandidateOrigin origin, CancellationToken ct = default);

// LyricsService.cs 新增
public async Task<LyricsResult?> FetchCandidateAsync(
    TrackInfo track, CandidateOrigin origin, CancellationToken ct = default)
{
    switch (origin)
    {
        case CandidateOrigin.Local l:
            {
                if (!File.Exists(l.FilePath)) return null;
                var content = await File.ReadAllTextAsync(l.FilePath, ct).ConfigureAwait(false);
                return new LyricsResult
                {
                    Source = "Local",
                    SyncedLyrics = content,
                    PlainLyrics = content,
                };
            }
        case CandidateOrigin.Lrclib l:
            {
                var resp = await _lrcLibClient.GetAsync(
                    track.Name, track.Artist, track.Album,
                    track.DurationMs / 1000.0, ct).ConfigureAwait(false);
                if (resp is null) return null;
                var synced = Normalize(resp.SyncedLyrics);
                var plain = Normalize(resp.PlainLyrics);
                if (!string.IsNullOrWhiteSpace(synced))
                    return new LyricsResult { Source = "LRCLIB", SyncedLyrics = synced, PlainLyrics = plain };
                return !string.IsNullOrWhiteSpace(plain)
                    ? new LyricsResult { Source = "LRCLIB", PlainLyrics = plain }
                    : null;
            }
        case CandidateOrigin.Netease n:
            {
                // 简化：Netease 不在覆盖项的常见来源
                return null;
            }
        default:
            return null;
    }
}
```

测试扩展：在 `LyricsService` 现有测试（如有）或新文件中加 `FetchCandidateAsync_Local_ReturnsContent`、`FetchCandidateAsync_LocalFileMissing_ReturnsNull` 等。

```bash
git commit -m "feat: LyricsService.FetchCandidateAsync 支持 origin"
```

### Step 8：扩展 `LyricsSyncEngine.LoadParsed`

文件：
- 修改：`src/ABLyrics.App/Services/Lyrics/LyricsSyncEngine.cs`
- 测试：在已有 `LyricsSyncEngineTests.cs`（若存在）或新建中扩展

修改要点：

```csharp
public void LoadParsed(LyricsData? data, string[] plainLines)
{
    _lyricsData = data;
    _plainLines = plainLines;
}
```

测试：
- `LoadParsed_ThenGetFrame_ReturnsExpected`：构造 `LyricsData`（含一行 + `StartTime = 5000`）+ `GetFrame(6000)` → `CurrentLine == text`

> 注：如果现有 `Load(LyricsResult?)` 测试已覆盖相同逻辑，本条测试可省略。

```bash
git commit -m "refactor: LyricsSyncEngine.LoadParsed 避免重复 Parse"
```

### Step 9：`PlaybackCoordinator` 集成

文件：
- 修改：`src/ABLyrics.App/Services/PlaybackCoordinator.cs`
- 新建：`tests/ABLyrics.App.Tests/PlaybackCoordinatorOverrideTests.cs`

修改要点（构造器注入 `ILyricsSearchService` + `LyricsOverrideStore`）：

```csharp
private readonly ILyricsSearchService _searchService;
private readonly LyricsOverrideStore _overrideStore;
private readonly Dictionary<string, CandidateOrigin> _overrides;  // 内存缓存
private LyricsCandidate? _overrideCandidate;

public PlaybackCoordinator(
    PlaybackSourceRegistry registry,
    string initialSourceId,
    ILyricsService lyricsService,
    LyricsBehaviorService lyricsBehavior,
    DisplaySettingsService? displaySettings = null,
    ILyricsSearchService? searchService = null,
    LyricsOverrideStore? overrideStore = null)
{
    // ... 原有初始化 ...
    _searchService = searchService ?? new LyricsSearchService(App.Settings);
    _overrideStore = overrideStore ?? new LyricsOverrideStore();
    _overrides = _overrideStore.Load().ToDictionary(kv => kv.Key, kv => kv.Value);
}

public event Action<long>? ProgressMsChanged;

// LoadTrackAsync 开头插入：
private async Task LoadTrackAsync(TrackInfo track)
{
    _loadedTrackId = track.Id;
    _trackDurationMs = track.DurationMs;
    _trackAlbum = track.Album;
    _trackInfoLayout.ResetForNewTrack();
    ShouldCenterTrackInfo = true;
    ClearLyricLines();

    var key = TrackKey.From(track);
    if (_overrides.TryGetValue(key, out var persistedOrigin))
    {
        var candidate = await TryLoadFromOriginAsync(track, persistedOrigin);
        if (candidate is not null)
        {
            _overrideCandidate = candidate;
            ApplyCandidateToEngine(candidate);
            StatusText = string.Empty;
            LyricsSource = candidate.Source;
            return;
        }
        // 文件丢失：移除 override 并回退
        _overrideStore.Remove(key);
        _overrides.Remove(key);
    }

    // 兜底链（保留现有逻辑）
    StatusText = "正在尝试 LRCLIB…";
    LoadingFlash?.Invoke();
    var lyrics = await _lyricsService.FetchLyricsAsync(track).ConfigureAwait(false);
    _syncEngine.SetDurationMs(_trackDurationMs);
    _syncEngine.Load(lyrics);
    LyricsSource = lyrics?.Source ?? (string.IsNullOrEmpty(_lyricsActiveSource) ? string.Empty : _lyricsActiveSource);
    StatusText = lyrics is null ? "暂无歌词" : string.Empty;
}

public void OpenCandidatePicker()
{
    // 触发 UI 入口（具体由 App 或窗口管理）
    CandidatePickerRequested?.Invoke();
}

public event Action? CandidatePickerRequested;

public async Task ApplyCandidateAsync(LyricsCandidate candidate)
{
    var key = TrackKey.From(_trackTitle, _trackAlbum, _artistName);  // 当前 track
    // 注意：需要 _artistName 而不是 ArtistName（property）—— 直接读 backing
    // 实际实现：根据 _loadedTrackId 重新构建 TrackInfo
    // 简化：用 backing fields
    _overrides[key] = candidate.Origin;
    _overrideStore.Save(key, candidate.Origin);
    _overrideCandidate = candidate;
    ApplyCandidateToEngine(candidate);
    LyricsSource = candidate.Source;
    StatusText = string.Empty;
}

private async Task<LyricsCandidate?> TryLoadFromOriginAsync(
    TrackInfo track, CandidateOrigin origin)
{
    var result = await _lyricsService.FetchCandidateAsync(track, origin);
    if (result is null) return null;
    return new LyricsCandidate
    {
        Source = result.Source,
        Label = "覆盖项",
        SyncedLyrics = result.SyncedLyrics,
        PlainLyrics = result.PlainLyrics,
        DurationMs = track.DurationMs,
        Origin = origin,
    };
}

private void ApplyCandidateToEngine(LyricsCandidate candidate)
{
    _syncEngine.SetDurationMs(candidate.DurationMs);
    if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
    {
        var data = LrcParser.Parse(candidate.SyncedLyrics);
        var plain = string.IsNullOrWhiteSpace(candidate.PlainLyrics)
            ? Array.Empty<string>()
            : candidate.PlainLyrics.Split(
                ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _syncEngine.LoadParsed(data, plain);
    }
    else if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics))
    {
        var plain = candidate.PlainLyrics.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _syncEngine.LoadParsed(null, plain);
    }
    else
    {
        _syncEngine.LoadParsed(null, Array.Empty<string>());
    }
}
```

测试要点（需要把现有 `FakeLyricsService` 扩展加 `FetchCandidateAsync`，`FakeSource` 维持现有）：
- `LoadTrackAsync_HasOverride_AppliesOverrideBeforeLrclib`
- `LoadTrackAsync_NoOverride_FallsBackAsBefore`
- `LoadTrackAsync_OverrideStaleFile_RemovesOverrideAndFallsBack`
- `ApplyCandidateAsync_PersistsOverride`
- `ApplyCandidateAsync_FreshTrack_UsesOverrideImmediately`
- `OpenCandidatePicker_RaisesEvent`

```bash
git commit -m "feat: PlaybackCoordinator 覆盖项优先级 + 进度事件"
```

### Step 10：`CandidateColumnView` 用户控件

文件：
- 新建：`src/ABLyrics.App/Views/CandidateColumnView.xaml`
- 新建：`src/ABLyrics.App/Views/CandidateColumnView.xaml.cs`

无单测（纯 UI 渲染）。要点：

```xml
<!-- CandidateColumnView.xaml -->
<UserControl x:Class="ABLyrics.App.Views.CandidateColumnView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Border BorderBrush="Gray" BorderThickness="1" Padding="12" Margin="4">
        <StackPanel>
            <TextBlock x:Name="HeaderText" FontWeight="SemiBold" Margin="0,0,0,8"/>
            <TextBlock x:Name="PreviousLineText" Opacity="0.5" Margin="0,4"/>
            <TextBlock x:Name="CurrentLineText" FontSize="18" FontWeight="Bold" Margin="0,4"/>
            <TextBlock x:Name="NextLineText" Opacity="0.5" Margin="0,4"/>
        </StackPanel>
    </Border>
</UserControl>
```

```csharp
// CandidateColumnView.xaml.cs
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Parsers;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;

namespace ABLyrics.App.Views;

public partial class CandidateColumnView : System.Windows.Controls.UserControl
{
    private readonly LyricsSyncEngine _engine = new();
    private LyricsCandidate? _candidate;

    public CandidateColumnView()
    {
        InitializeComponent();
    }

    public void Bind(LyricsCandidate candidate)
    {
        _candidate = candidate;
        HeaderText.Text = $"{candidate.Source} · {candidate.Label}";
        _engine.SetDurationMs(candidate.DurationMs);
        if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
        {
            var data = LrcParser.Parse(candidate.SyncedLyrics);
            var plain = (candidate.PlainLyrics ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _engine.LoadParsed(data, plain);
        }
    }

    public void OnProgress(long progressMs)
    {
        if (_candidate is null) return;
        var frame = _engine.GetFrame(progressMs);
        PreviousLineText.Text = frame.PreviousLine;
        CurrentLineText.Text = frame.CurrentLine;
        NextLineText.Text = frame.NextLine;
    }
}
```

```bash
git commit -m "feat: CandidateColumnView 单列用户控件"
```

### Step 11：`LyricsCandidatePickerWindow`

文件：
- 新建：`src/ABLyrics.App/Views/LyricsCandidatePickerWindow.xaml`
- 新建：`src/ABLyrics.App/Views/LyricsCandidatePickerWindow.xaml.cs`

实现要点：
- 单例模式：暴露 `Show(App coordinator, LyricsSearchService search, TrackInfo track)` 静态方法
- 布局：左侧 ListBox（候选） + 右侧 ItemsControl（对比区，CandidateColumnView 模板）
- 库下拉框 ComboBox（绑定 `LyricsSearchService` 的可用库）
- 按钮：`[刷新]` `[确认]` `[关闭]`
- 状态文本 TextBlock
- 订阅 `coordinator.ProgressMsChanged` 驱动所有列

XAML 大致结构：

```xml
<ui:FluentWindow x:Class="ABLyrics.App.Views.LyricsCandidatePickerWindow"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:views="clr-namespace:ABLyrics.App.Views"
                 Title="选择歌词版本" Height="600" Width="900">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Row 0: 顶部 -->
        <DockPanel Grid.Row="0" Margin="12,8">
            <TextBlock Text="库:" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <ComboBox x:Name="LibraryCombo" Width="120" SelectionChanged="OnLibraryChanged"/>
            <TextBlock x:Name="TrackInfoText" Margin="16,0,0,0" VerticalAlignment="Center"/>
        </DockPanel>

        <!-- Row 1: 主体两列 -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="280"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <ListBox x:Name="CandidatesList" Grid.Column="0"
                     SelectionMode="Single"
                     SelectionChanged="OnCandidateSelected"
                     HorizontalContentAlignment="Stretch">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <!-- 圆点 + Source + Label + 当前覆盖 + ✕ -->
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <ItemsControl x:Name="CompareColumns" Grid.Column="1">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <UniformGrid Rows="1"/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <views:CandidateColumnView/>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </Grid>

        <!-- Row 2: 底部 -->
        <DockPanel Grid.Row="2" Margin="12,8">
            <TextBlock x:Name="StatusText" VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" DockPanel.Dock="Right">
                <Button Content="刷新" Click="OnRefreshClick" Margin="0,0,8,0"/>
                <Button Content="确认" Click="OnConfirmClick" IsEnabled="False" x:Name="ConfirmButton" Margin="0,0,8,0"/>
                <Button Content="关闭" Click="OnCloseClick"/>
            </StackPanel>
        </DockPanel>
    </Grid>
</ui:FluentWindow>
```

代码隐藏文件要点：
- 构造时通过 DI 容器或构造函数参数注入 `coordinator`、`searchService`、`overrideStore`
- `ShowForTrack(TrackInfo track)` 实例方法（不在静态 API 里）
- 候选项 ViewModel 包装类含 `Candidate`、`IsOverride`、`IsAvailable`、`ClearCommand`
- 关闭事件改为 `Hide()`

```bash
git commit -m "feat: LyricsCandidatePickerWindow 选版本窗口"
```

### Step 12：App 集成

文件：
- 修改：`src/ABLyrics.App/App.xaml.cs`
- 修改：`src/ABLyrics.App/Views/AppBarWindow.xaml.cs`
- 修改：`src/ABLyrics.App/Views/OverlayWindow.xaml.cs`

修改要点：

1. `App.OnStartup`：
   - 在 `Coordinator = new PlaybackCoordinator(...)` 时注入 `LyricsSearchService` + `LyricsOverrideStore`
   - 启动 `Task.Run` 异步执行 `_searchService.ProbeAsync()` → 把结果合并到 `_searchService` 状态（用一个 `IReadOnlyList<string> AvailableLibraries` 属性 + `LibraryAvailabilityChanged` 事件）

2. 托盘菜单：在 `BuildTrayContextMenu` 里加：
   ```csharp
   var pickerItem = new Forms.ToolStripMenuItem("选择歌词版本…");
   pickerItem.Click += (_, _) => Dispatcher.BeginInvoke(() => ShowCandidatePicker());
   menu.Items.Add(pickerItem);
   ```
   `ShowCandidatePicker()` 创建/激活 `LyricsCandidatePickerWindow`。

3. `AppBarWindow` 和 `OverlayWindow` 右键菜单各加一项 "选择歌词版本…"。

4. `AppBarWindow` / `OverlayWindow` 订阅 `coordinator.ProgressMsChanged`：
   - AppBar 已经有了 `TickInterpolation` 路径——**不**订阅新事件，避免双触发
   - Picker 窗口是唯一订阅 `ProgressMsChanged` 的 UI 元素

```bash
git commit -m "feat: App 集成 探活 + 启动 picker 入口"
```

### Step 13：文档同步

文件：
- 修改：`docs/AGENT_CONTEXT.md`

修改要点：
- 第 114 行表格新增一行：`| `%LOCALAPPDATA%/ABLyrics/lyrics-overrides.json` | 歌词版本覆盖项 | PlaybackCoordinator | JSON (WriteIndented) |`
- "常见任务入口"表新增一行：`| 修改歌词候选聚合 | `Services/Lyrics/LyricsSearchService.cs` + `ILyricsSearchService` | 不要忘了 AvailableSources 也要同步 |`
- "当前 WIP" 段加入本期
- 第 5.3 节 `FetchLyricsAsync` 兜底顺序说明加 "覆盖项凌驾其上"

```bash
git commit -m "docs: 同步歌词候选选择器相关路径与优先级"
```

### Step 14：全量验证

```powershell
dotnet build ABLyrics.sln
dotnet test ABLyrics.sln
```

期望：零警告 + 全部测试通过（含新增 ~25 条）。

## 风险与回退

- **窗口 UI 复杂度**：Step 10/11 是最大的代码量；若 UI 部分难以调通，**最坏情况**：
  - 保留 `OpenCandidatePicker` 事件与 `_overrideCandidate` 字段；窗口暂时是空壳
  - 但 `ApplyCandidateAsync` + `LoadTrackAsync` 优先级覆盖必须可用——这是核心交付物
- **`LrcLibClient.SearchAsync` 反序列化风险**：LRCLIB API 返回字段名可能微调；测试用 `HttpMessageHandler` 替身覆盖正面/反面场景
- **`_overrides` 内存缓存 vs JSON 一致性**：每次 `ApplyCandidateAsync`/`Remove` 同步写盘；启动时一次性 `Load()` 到内存
- **Netease 路径极少被覆盖**：本期 Netease 候选只是"单条"，覆盖项写入后再用 `FetchCandidateAsync` 拉一次——Netease `Origin` 暂时只在 `LyricsSearchService.SearchAsync` 中作为"单候选源"出现，`FetchCandidateAsync` 对 `CandidateOrigin.Netease` 返回 `null`，**不阻塞本期**（spec 已声明 Netease 不在多候选范围）

## 预估工时

| Step | 内容 | 估计 |
|------|------|------|
| 1 | TrackKey | 10 min |
| 2 | LyricsCandidate | 5 min |
| 3 | LyricsOverrideStore | 25 min |
| 4 | LrcLibClient.SearchAsync | 20 min |
| 5 | LocalLyricsSearchProvider | 20 min |
| 6 | LyricsSearchService | 30 min |
| 7 | LyricsService.FetchCandidateAsync | 15 min |
| 8 | LyricsSyncEngine.LoadParsed | 10 min |
| 9 | PlaybackCoordinator 集成 | 35 min |
| 10 | CandidateColumnView | 15 min |
| 11 | LyricsCandidatePickerWindow | 50 min |
| 12 | App 集成 | 20 min |
| 13 | 文档 | 10 min |
| 14 | 验证 | 10 min |
| **总计** | | **~4-5h** |