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