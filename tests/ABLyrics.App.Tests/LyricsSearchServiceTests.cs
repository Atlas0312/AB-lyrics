using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;
using Xunit;

namespace ABLyrics.App.Tests;

public class LyricsSearchServiceTests : IDisposable
{
    private readonly string _dir;

    public LyricsSearchServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsSearchSvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public void Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            var factory = _responses.Dequeue();
            return Task.FromResult(factory(request));
        }
    }

    private static LrcLibClient BuildLrcLibClient(StubHandler handler)
    {
        var ctor = typeof(LrcLibClient).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(HttpMessageHandler) },
            modifiers: null);
        return (LrcLibClient)ctor!.Invoke(new object[] { handler });
    }

    private static TrackInfo Track(int durationMs = 180_000, string artist = "Artist", string name = "Song", string album = "Album")
        => new()
        {
            Id = "t1",
            Name = name,
            Artist = artist,
            Album = album,
            DurationMs = durationMs,
        };

    private static AppSettings NewSettings(bool withMusicU = false)
    {
        return new AppSettings
        {
            Lyrics = new LyricsSettings { LocalPath = "" }, // 临时目录经构造函数创建
            NetEase = new NetEaseSettings { MusicU = withMusicU ? "MUSIC_U_TOKEN" : string.Empty },
        };
    }

    /// <summary>
    /// 通过 internal 测试构造在 AppSettings 之外注入自定义 LrcLibClient。
    /// </summary>
    private static LyricsSearchService NewService(AppSettings settings, LrcLibClient? lrcLibClient = null)
    {
        if (lrcLibClient is null)
        {
            return new LyricsSearchService(settings);
        }
        var ctor = typeof(LyricsSearchService).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(AppSettings), typeof(LrcLibClient) },
            modifiers: null);
        Assert.NotNull(ctor);
        return (LyricsSearchService)ctor!.Invoke(new object[] { settings, lrcLibClient });
    }

    private LyricsSearchService NewServiceWithLocalDir(LrcLibClient? lrcLibClient = null, bool withMusicU = false)
    {
        var settings = new AppSettings
        {
            Lyrics = new LyricsSettings { LocalPath = _dir },
            NetEase = new NetEaseSettings { MusicU = withMusicU ? "MUSIC_U_TOKEN" : string.Empty },
        };
        return NewService(settings, lrcLibClient);
    }

    // ---------- SearchAsync / Local ----------

    [Fact]
    public void SearchAsync_LocalLibrary_ReturnsLocalCandidates()
    {
        File.WriteAllText(Path.Combine(_dir, "Artist - Album - Song.lrc"), "[00:01.00] lyrics");
        var service = NewServiceWithLocalDir();

        var candidates = service.SearchAsync(Track(), "Local", CancellationToken.None)
            .GetAwaiter().GetResult();

        var c = Assert.Single(candidates);
        Assert.Equal("Local", c.Source);
        Assert.Equal("Artist - Album - Song", c.Label);
        Assert.IsType<CandidateOrigin.Local>(c.Origin);
    }

    // ---------- SearchAsync / LRCLIB ----------

    [Fact]
    public void SearchAsync_LrclibLibrary_SortsByDurationProximity()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              { "id": 1, "trackName": "Song", "artistName": "Artist", "albumName": "Album",
                "duration": 60.0,  "syncedLyrics": null, "plainLyrics": null },
              { "id": 2, "trackName": "Song", "artistName": "Artist", "albumName": "Album",
                "duration": 180.0, "syncedLyrics": null, "plainLyrics": null },
              { "id": 3, "trackName": "Song", "artistName": "Artist", "albumName": "Album",
                "duration": 300.0, "syncedLyrics": null, "plainLyrics": null }
            ]
            """);
        var lrcLib = BuildLrcLibClient(handler);
        var service = NewServiceWithLocalDir(lrcLib);

        // target duration = 180000 ms (180 s) → id=2 离 180s 最近，应排在第一
        var candidates = service.SearchAsync(Track(durationMs: 180_000), "LRCLIB", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(3, candidates.Count);
        Assert.Equal(2, candidates[0].Origin.GetType().GetProperty("LrclibId")!.GetValue(candidates[0].Origin));
        Assert.Equal(1, candidates[1].Origin.GetType().GetProperty("LrclibId")!.GetValue(candidates[1].Origin));
        Assert.Equal(3, candidates[2].Origin.GetType().GetProperty("LrclibId")!.GetValue(candidates[2].Origin));
    }

    [Fact]
    public void SearchAsync_LrclibLibrary_ParsesAlbumAndDuration()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              { "id": 42, "trackName": "Song", "artistName": "Artist", "albumName": "Album X",
                "duration": 200.0, "syncedLyrics": "[00:01.00] lyric", "plainLyrics": "lyric" }
            ]
            """);
        var lrcLib = BuildLrcLibClient(handler);
        var service = NewServiceWithLocalDir(lrcLib);

        var candidates = service.SearchAsync(Track(durationMs: 200_000), "LRCLIB", CancellationToken.None)
            .GetAwaiter().GetResult();

        var c = Assert.Single(candidates);
        Assert.Equal("LRCLIB", c.Source);
        Assert.Equal("Album X · 3:20", c.Label);
        Assert.Equal(200_000, c.DurationMs);
        Assert.Equal("[00:01.00] lyric", c.SyncedLyrics);
        Assert.Equal("lyric", c.PlainLyrics);
        var origin = Assert.IsType<CandidateOrigin.Lrclib>(c.Origin);
        Assert.Equal(42, origin.LrclibId);
    }

    [Fact]
    public void SearchAsync_LrclibLibrary_NoAlbum_FormatsDurationOnly()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              { "id": 9, "trackName": "Song", "artistName": "Artist", "albumName": "",
                "duration": 65.0, "syncedLyrics": null, "plainLyrics": null }
            ]
            """);
        var lrcLib = BuildLrcLibClient(handler);
        var service = NewServiceWithLocalDir(lrcLib);

        var candidates = service.SearchAsync(Track(durationMs: 65_000), "LRCLIB", CancellationToken.None)
            .GetAwaiter().GetResult();

        var c = Assert.Single(candidates);
        Assert.Equal("1:05", c.Label);
    }

    // ---------- SearchAsync / Netease / Unknown ----------

    [Fact]
    public void SearchAsync_NeteaseNoMusicU_ReturnsEmpty()
    {
        var service = NewServiceWithLocalDir(withMusicU: false);

        var candidates = service.SearchAsync(Track(), "Netease", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Empty(candidates);
    }

    [Fact]
    public void SearchAsync_UnknownLibrary_ReturnsEmpty()
    {
        var service = NewServiceWithLocalDir();

        var candidates = service.SearchAsync(Track(), "BogusLib", CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Empty(candidates);
    }

    // ---------- ProbeAsync ----------

    [Fact]
    public void ProbeAsync_LrclibReachable_True()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var lrcLib = BuildLrcLibClient(handler);
        var service = NewServiceWithLocalDir(lrcLib);

        var probe = service.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(probe["LRCLIB"]);
        Assert.True(probe["Local"]);
    }

    [Fact]
    public void ProbeAsync_LrclibReachable_False()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "");
        var lrcLib = BuildLrcLibClient(handler);
        var service = NewServiceWithLocalDir(lrcLib);

        var probe = service.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(probe["LRCLIB"]);
    }

    [Fact]
    public void ProbeAsync_LocalReachable_True()
    {
        var service = NewServiceWithLocalDir();

        var probe = service.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(probe["Local"]);
    }

    [Fact]
    public void ProbeAsync_NeteaseSkipped_WhenMusicUEmpty()
    {
        var service = NewServiceWithLocalDir(withMusicU: false);

        var probe = service.ProbeAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(probe.ContainsKey("Netease"));
    }
}