using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

internal static class DisplaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "display-settings.json");

    public static DisplayStyleSettings Load(DisplayStyleSettings defaults)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return defaults.Clone();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<DisplayStyleSettings>(json, JsonOptions) ?? defaults.Clone();
        }
        catch
        {
            return defaults.Clone();
        }
    }

    public static void Save(DisplayStyleSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
