using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotLyrics.App.Services.Lyrics;

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
}

internal sealed class LrcLibLyricsResponse
{
    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }
}
