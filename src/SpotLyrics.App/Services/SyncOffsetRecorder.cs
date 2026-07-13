using System.IO;

namespace SpotLyrics.App.Services;

internal static class SyncOffsetRecorder
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotLyrics",
        "logs",
        "sync-offset.csv");

    private static readonly object Lock = new();

    public static void Record(int offsetMs, string source = "slider")
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);

            var fileExists = File.Exists(FilePath);
            lock (Lock)
            {
                using var writer = new StreamWriter(FilePath, append: true);
                if (!fileExists)
                {
                    writer.WriteLine("timestamp,offset_ms,source");
                }
                writer.WriteLine($"{DateTimeOffset.Now:O},{offsetMs},{source}");
            }
        }
        catch
        {
            // swallow — recording failure must not affect functionality
        }
    }
}
