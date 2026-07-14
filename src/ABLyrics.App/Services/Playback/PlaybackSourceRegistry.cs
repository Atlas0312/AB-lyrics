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