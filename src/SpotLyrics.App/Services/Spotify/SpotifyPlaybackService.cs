using System.Net;
using System.Net.Http;
using System.Text.Json;
using SpotLyrics.App.Models;

namespace SpotLyrics.App.Services.Spotify;

public sealed class SpotifyPlaybackService : ISpotifyPlaybackService, IDisposable
{
    private readonly SpotifyApiClient _apiClient;

    public SpotifyPlaybackService(ISpotifyAuthService authService)
    {
        _apiClient = new SpotifyApiClient(authService);
    }

    public async Task<PlaybackState?> GetCurrentPlaybackAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _apiClient
            .SendAsync(HttpMethod.Get, "me/player/currently-playing", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"获取播放状态失败 ({(int)response.StatusCode}): {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var artists = item.GetProperty("artists").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        string? isrc = null;
        if (item.TryGetProperty("external_ids", out var externalIds) &&
            externalIds.TryGetProperty("isrc", out var isrcElement))
        {
            isrc = isrcElement.GetString();
        }

        var track = new TrackInfo
        {
            Id = item.GetProperty("id").GetString() ?? string.Empty,
            Name = item.GetProperty("name").GetString() ?? string.Empty,
            Artist = string.Join(", ", artists),
            Album = item.GetProperty("album").GetProperty("name").GetString() ?? string.Empty,
            DurationMs = item.GetProperty("duration_ms").GetInt32(),
            Isrc = isrc,
        };

        return new PlaybackState
        {
            Track = track,
            ProgressMs = root.TryGetProperty("progress_ms", out var progress) ? progress.GetInt64() : 0,
            IsPlaying = root.TryGetProperty("is_playing", out var playing) && playing.GetBoolean(),
        };
    }

    public void Dispose() => _apiClient.Dispose();
}
