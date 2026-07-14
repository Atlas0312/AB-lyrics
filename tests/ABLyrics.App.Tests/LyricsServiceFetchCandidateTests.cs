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

public class LyricsServiceFetchCandidateTests : IDisposable
{
    private readonly string _dir;

    public LyricsServiceFetchCandidateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ABLyricsSvcFC-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// 通过 internal 构造注入 LrcLibClient（避免真实网络请求）。
    /// </summary>
    private LyricsService NewService(LrcLibClient lrcLibClient)
    {
        var settings = new AppSettings
        {
            Lyrics = new LyricsSettings { LocalPath = _dir },
        };
        var ctor = typeof(LyricsService).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(AppSettings), typeof(LrcLibClient) },
            modifiers: null);
        Assert.NotNull(ctor);
        return (LyricsService)ctor!.Invoke(new object[] { settings, lrcLibClient });
    }

    private static TrackInfo Track(string name = "Song", string artist = "Artist", string album = "Album", int durationMs = 200_000)
        => new()
        {
            Id = "t1",
            Name = name,
            Artist = artist,
            Album = album,
            DurationMs = durationMs,
        };

    // ---------- Local ----------

    [Fact]
    public void FetchCandidateAsync_LocalFile_ReadsContent()
    {
        var file = Path.Combine(_dir, "Artist - Album - Song.lrc");
        File.WriteAllText(file, "[00:01.00] hello");

        var service = NewService(BuildLrcLibClient(new StubHandler()));
        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Local(file), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.Equal("Local", result!.Source);
        Assert.Equal("[00:01.00] hello", result.SyncedLyrics);
        Assert.Equal("[00:01.00] hello", result.PlainLyrics);
    }

    [Fact]
    public void FetchCandidateAsync_LocalFileMissing_ReturnsNull()
    {
        var missing = Path.Combine(_dir, "nonexistent.lrc");

        var service = NewService(BuildLrcLibClient(new StubHandler()));
        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Local(missing), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Null(result);
    }

    // ---------- Lrclib ----------

    [Fact]
    public void FetchCandidateAsync_LrclibHit_ReturnsSynced()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            { "syncedLyrics": "[00:01.00] synced", "plainLyrics": "synced" }
            """);
        var service = NewService(BuildLrcLibClient(handler));

        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Lrclib(123), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.Equal("LRCLIB", result!.Source);
        Assert.Equal("[00:01.00] synced", result.SyncedLyrics);
        Assert.Equal("synced", result.PlainLyrics);
    }

    [Fact]
    public void FetchCandidateAsync_LrclibHitPlainOnly_ReturnsPlain()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            { "syncedLyrics": null, "plainLyrics": "plain only" }
            """);
        var service = NewService(BuildLrcLibClient(handler));

        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Lrclib(1), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.Equal("LRCLIB", result!.Source);
        Assert.Null(result.SyncedLyrics);
        Assert.Equal("plain only", result.PlainLyrics);
    }

    [Fact]
    public void FetchCandidateAsync_LrclibFailure_ReturnsNull()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "");
        var service = NewService(BuildLrcLibClient(handler));

        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Lrclib(1), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Null(result);
    }

    // ---------- Netease ----------

    [Fact]
    public void FetchCandidateAsync_Netease_ReturnsNull()
    {
        var service = NewService(BuildLrcLibClient(new StubHandler()));

        var result = service
            .FetchCandidateAsync(Track(), new CandidateOrigin.Netease(123L), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Null(result);
    }

    // ---------- 原有方法未受影响 ----------

    [Fact]
    public void AvailableSources_StillIncludesDefaultLibraries()
    {
        var service = NewService(BuildLrcLibClient(new StubHandler()));

        Assert.Contains("LRCLIB", service.AvailableSources);
        Assert.Contains("Local", service.AvailableSources);
    }
}