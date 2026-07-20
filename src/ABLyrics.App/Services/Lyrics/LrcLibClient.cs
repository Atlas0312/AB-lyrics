using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ABLyrics.App.Services.Lyrics;

internal sealed class LrcLibClient
{
    private readonly HttpClient _httpClient;

    public LrcLibClient(string userAgent)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://lrclib.net/api/"),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    /// <summary>
    /// 测试专用构造：通过 <see cref="HttpMessageHandler"/> 替身注入
    /// <see cref="HttpClient"/>，避免真实网络请求。
    /// </summary>
    internal LrcLibClient(HttpMessageHandler handler)
    {
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://lrclib.net/api/"),
        };
    }

    public async Task<LrcLibLyricsResponse?> GetAsync(
        string trackName,
        string artistName,
        string? albumName,
        double? durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var query = $"get?track_name={Uri.EscapeDataString(trackName)}&artist_name={Uri.EscapeDataString(artistName)}";
        if (!string.IsNullOrWhiteSpace(albumName))
        {
            query += $"&album_name={Uri.EscapeDataString(albumName)}";
        }

        if (durationSeconds.HasValue)
        {
            query += $"&duration={durationSeconds.Value}";
        }

        using var response = await _httpClient.GetAsync(query, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<LrcLibLyricsResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 按 LRCLIB 绝对 id 拉取：<c>GET /api/get/{id}</c>。
    /// 覆盖项恢复必须走这条路径，不能用元数据精确匹配（同曲多条目时会拿到错误版本）。
    /// </summary>
    public async Task<LrcLibLyricsResponse?> GetByIdAsync(
        int lrclibId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient
            .GetAsync($"get/{lrclibId}", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<LrcLibLyricsResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 多候选搜索：调 LRCLIB <c>/api/search</c>，把返回的 JSON 数组解析成
    /// <see cref="LrcLibSearchHit"/> 列表。duration 是秒（float），统一换算为 ms。
    /// 任何错误（网络/非 2xx/JSON 损坏）一律返回空列表，让上层调用者自行兜底。
    /// </summary>
    public async Task<IReadOnlyList<LrcLibSearchHit>> SearchAsync(
        string trackName,
        string artistName,
        string? albumName,
        CancellationToken cancellationToken = default)
    {
        var query = $"search?track_name={Uri.EscapeDataString(trackName)}" +
                    $"&artist_name={Uri.EscapeDataString(artistName)}";
        if (!string.IsNullOrWhiteSpace(albumName))
        {
            query += $"&album_name={Uri.EscapeDataString(albumName)}";
        }

        try
        {
            using var response = await _httpClient.GetAsync(query, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<LrcLibSearchHit>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var items = await JsonSerializer.DeserializeAsync<List<LrcLibSearchItem>>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (items is null || items.Count == 0)
            {
                return Array.Empty<LrcLibSearchHit>();
            }

            var hits = new List<LrcLibSearchHit>(items.Count);
            foreach (var item in items)
            {
                if (item is null) continue;
                hits.Add(new LrcLibSearchHit(
                    Id: item.Id,
                    TrackName: item.TrackName ?? string.Empty,
                    ArtistName: item.ArtistName ?? string.Empty,
                    AlbumName: item.AlbumName ?? string.Empty,
                    DurationMs: (int)Math.Round(item.Duration * 1000.0),
                    SyncedLyrics: item.SyncedLyrics,
                    PlainLyrics: item.PlainLyrics));
            }
            return hits;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 网络异常 / JSON 损坏 → 空列表，让上层做兜底
            return Array.Empty<LrcLibSearchHit>();
        }
    }

    /// <summary>
    /// 探活：发一个轻量 GET 到 <c>/api/search?q=test</c>，2xx 即视为可达。
    /// 任何异常返回 false。
    /// </summary>
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("search?q=test", cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 单条搜索命中，供 <see cref="LyricsSearchService"/> 按 duration 距离排序时使用。
/// 内部类型：UI 不直接消费。
/// </summary>
internal sealed record LrcLibSearchHit(
    int Id,
    string TrackName,
    string ArtistName,
    string AlbumName,
    int DurationMs,
    string? SyncedLyrics,
    string? PlainLyrics);

/// <summary>
/// LRCLIB /api/search 数组元素的反序列化目标。字段名按官方 camelCase JSON 一一对应。
/// </summary>
internal sealed class LrcLibSearchItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    /// <summary>LRCLIB 返回的是秒（float），需 ×1000 转为毫秒。</summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }
}

internal sealed class LrcLibLyricsResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }
}