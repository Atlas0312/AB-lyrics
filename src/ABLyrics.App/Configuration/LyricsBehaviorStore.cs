using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

public sealed class LyricsBehaviorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public LyricsBehaviorStore() : this(DefaultPath) { }

    public LyricsBehaviorStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "lyrics-behavior.json");

    public LyricsBehaviorSettings Load(LyricsBehaviorSettings defaults)
    {
        try
        {
            if (!File.Exists(_path)) return defaults.Clone();
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<LyricsBehaviorSettings>(json, JsonOptions);
            return loaded ?? defaults.Clone();
        }
        catch
        {
            return defaults.Clone();
        }
    }

    public void Save(LyricsBehaviorSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}