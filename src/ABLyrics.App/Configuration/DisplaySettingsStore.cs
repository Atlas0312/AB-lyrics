using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

internal static class DisplaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "display-settings.json");

    public static DisplayStyleSettings Load(DisplayStyleSettings defaults) => Load(FilePath, defaults);

    public static DisplayStyleSettings Load(string path, DisplayStyleSettings defaults)
    {
        try
        {
            if (!File.Exists(path))
            {
                return defaults.Clone();
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<DisplayStyleSettings>(json, JsonOptions) ?? defaults.Clone();
            ApplyDefaults(loaded, defaults);
            return loaded;
        }
        catch
        {
            return defaults.Clone();
        }
    }

    public static void Save(DisplayStyleSettings settings) => Save(FilePath, settings);

    public static void Save(string path, DisplayStyleSettings settings)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static void ApplyDefaults(DisplayStyleSettings loaded, DisplayStyleSettings defaults)
    {
        // Backfill fields that may have been absent in older serialized configs.
        // Zero or negative values are treated as "missing" so users can still
        // explicitly opt into a 0 (e.g. SyncOffsetMs) by setting the default to 0.
        if (loaded.SongInfoFontSize <= 0) loaded.SongInfoFontSize = defaults.SongInfoFontSize;
    }
}
