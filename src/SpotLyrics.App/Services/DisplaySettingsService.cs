using SpotLyrics.App.Configuration;

namespace SpotLyrics.App.Services;

public sealed class DisplaySettingsService
{
    public event EventHandler<DisplayStyleSettings>? SettingsChanged;

    public DisplayStyleSettings Current { get; private set; }

    public DisplaySettingsService(DisplayStyleSettings defaults)
    {
        Current = DisplaySettingsStore.Load(defaults);
    }

    public void Update(DisplayStyleSettings settings)
    {
        Current = settings.Clone();
        DisplaySettingsStore.Save(Current);
        SettingsChanged?.Invoke(this, Current);
    }
}
