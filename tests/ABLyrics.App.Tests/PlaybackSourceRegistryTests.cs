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