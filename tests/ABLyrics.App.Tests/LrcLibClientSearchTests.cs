using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ABLyrics.App.Services.Lyrics;
using Xunit;

namespace ABLyrics.App.Tests;

public class LrcLibClientSearchTests
{
    /// <summary>
    /// 测试替身：记录最近一次请求的 URI，并把脚本化的响应回给 HttpClient。
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public Uri? LastRequestUri { get; private set; }
        public string? LastQuery { get; private set; }

        public void Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
            });
        }

        public void Enqueue(HttpResponseMessage response)
        {
            _responses.Enqueue(_ => response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastQuery = request.RequestUri?.Query;
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            var factory = _responses.Dequeue();
            return Task.FromResult(factory(request));
        }
    }

    private static LrcLibClient NewClient(StubHandler handler)
    {
        // 走 internal 构造函数以注入 HttpMessageHandler 替身
        var ctor = typeof(LrcLibClient).GetConstructor(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(HttpMessageHandler) },
            modifiers: null);
        Assert.NotNull(ctor);
        return (LrcLibClient)ctor!.Invoke(new object[] { handler });
    }

    [Fact]
    public async Task SearchAsync_ReturnsParsedHits()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              {
                "id": 100,
                "trackName": "晴天",
                "artistName": "周杰伦",
                "albumName": "七里香",
                "duration": 269.0,
                "syncedLyrics": "[00:01.00] hello",
                "plainLyrics": "hello"
              },
              {
                "id": 101,
                "trackName": "晴天 (live)",
                "artistName": "周杰伦",
                "albumName": "七里香",
                "duration": 312.5,
                "syncedLyrics": null,
                "plainLyrics": "live plain"
              }
            ]
            """);
        var client = NewClient(handler);

        var hits = await client.SearchAsync("晴天", "周杰伦", "七里香", CancellationToken.None);

        Assert.Equal(2, hits.Count);
        Assert.Equal(100, hits[0].Id);
        Assert.Equal("晴天", hits[0].TrackName);
        Assert.Equal("周杰伦", hits[0].ArtistName);
        Assert.Equal("七里香", hits[0].AlbumName);
        Assert.Equal("[00:01.00] hello", hits[0].SyncedLyrics);
        Assert.Equal("hello", hits[0].PlainLyrics);
        Assert.Equal(101, hits[1].Id);
        Assert.Null(hits[1].SyncedLyrics);
    }

    [Fact]
    public async Task SearchAsync_NonSuccess_ReturnsEmpty()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "boom");
        var client = NewClient(handler);

        var hits = await client.SearchAsync("晴天", "周杰伦", "七里香", CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAsync_QueryStringUsesQParameter()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var client = NewClient(handler);

        // CJK 艺人会并入 q；album 不再作为硬过滤参数。
        await client.SearchAsync("晴天", "周杰伦", "七里香", CancellationToken.None);

        Assert.NotNull(handler.LastRequestUri);
        var query = handler.LastQuery ?? string.Empty;
        Assert.Contains("q=", query);
        Assert.DoesNotContain("track_name=", query);
        Assert.DoesNotContain("album_name=", query);
        Assert.Contains(Uri.EscapeDataString("晴天 周杰伦"), query);
    }

    [Fact]
    public async Task SearchAsync_EnglishArtist_NotAppendedToQ()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var client = NewClient(handler);

        await client.SearchAsync(
            "相爱很难 - 电影\"男人四十\"歌曲",
            "Jacky Cheung, Anita Mui",
            "Album",
            CancellationToken.None);

        var query = handler.LastQuery ?? string.Empty;
        Assert.Contains("q=", query);
        Assert.Contains(Uri.EscapeDataString("相爱很难"), query);
        Assert.DoesNotContain("Jacky", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSearchQuery_StripsSuffixAndKeepsCjkArtist()
    {
        Assert.Equal(
            "相爱很难 张学友",
            LrcLibClient.BuildSearchQuery("相爱很难 - 电影\"男人四十\"歌曲", "张学友"));
        Assert.Equal(
            "相爱很难",
            LrcLibClient.BuildSearchQuery("相爱很难 - 电影\"男人四十\"歌曲", "Jacky Cheung, Anita Mui"));
        Assert.Equal(
            "相爱很难",
            LrcLibClient.BuildSearchQuery("相爱很难(电影\"男人四十\"歌曲)", "Anita Mui"));
    }

    [Fact]
    public async Task SearchAsync_DurationSecondsConvertedToMs()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            [
              { "id": 1, "trackName": "x", "artistName": "y", "albumName": "z",
                "duration": 269.5, "syncedLyrics": null, "plainLyrics": null }
            ]
            """);
        var client = NewClient(handler);

        var hits = await client.SearchAsync("x", "y", "z", CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal(269500, hit.DurationMs);
    }

    [Fact]
    public async Task SearchAsync_EmptyArray_ReturnsEmpty()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "[]");
        var client = NewClient(handler);

        var hits = await client.SearchAsync("x", "y", "z", CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task ProbeAsync_SuccessReturnsTrue()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, "");
        var client = NewClient(handler);

        var ok = await client.ProbeAsync(CancellationToken.None);

        Assert.True(ok);
    }

    [Fact]
    public async Task ProbeAsync_FailureReturnsFalse()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "");
        var client = NewClient(handler);

        var ok = await client.ProbeAsync(CancellationToken.None);

        Assert.False(ok);
    }
}