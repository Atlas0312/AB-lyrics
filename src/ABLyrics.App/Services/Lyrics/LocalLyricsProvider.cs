using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

internal sealed class LocalLyricsProvider
{
    private readonly string _libraryPath;

    public LocalLyricsProvider(AppSettings settings)
    {
        var configured = settings.Lyrics.LocalPath;
        _libraryPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ABLyrics", "lyrics")
            : configured;

        Directory.CreateDirectory(_libraryPath);
    }

    public string LibraryPath => _libraryPath;

    public Task<LyricsResult?> GetAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var file = FindFile(track);
        if (file is null)
        {
            return Task.FromResult<LyricsResult?>(null);
        }

        var content = File.ReadAllText(file);
        return Task.FromResult<LyricsResult?>(new LyricsResult
        {
            Source = "Local",
            SyncedLyrics = content,
            PlainLyrics = content,
        });
    }

    public bool HasMatch(TrackInfo track)
    {
        return FindFile(track) is not null;
    }

    public Task ImportAsync(string sourcePath, TrackInfo track)
    {
        var fileName = SanitizeFileName($"{track.Artist} - {track.Name}.lrc");
        var destPath = Path.Combine(_libraryPath, fileName);
        File.Copy(sourcePath, destPath, overwrite: true);
        return Task.CompletedTask;
    }

    private string? FindFile(TrackInfo track)
    {
        var pattern = SanitizeFileName($"{track.Artist} - {track.Name}.lrc");
        return Directory.EnumerateFiles(_libraryPath, "*.lrc")
            .FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
