using System.IO;
using System.Text.Json;

namespace ABLyrics.App.Configuration;

public sealed class PlaybackStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public PlaybackStateStore() : this(DefaultPath) { }

    public PlaybackStateStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics",
            "playback-state.json");

    public PlaybackSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new PlaybackSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<PlaybackSettings>(json, JsonOptions) ?? new PlaybackSettings();
        }
        catch
        {
            return new PlaybackSettings();
        }
    }

    public void Save(PlaybackSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
