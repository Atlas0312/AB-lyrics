using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

internal sealed class LocalLyricsProvider
{
    // Windows 文件名上限 255，扣除 ".lrc" 后缀与盘符/目录前缀后给的缓冲。
    // 作用于整段 stem（不含 .lrc）。
    private const int MaxStemLength = 196;

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
        var destPath = Path.Combine(_libraryPath, BuildFileName(track));
        File.Copy(sourcePath, destPath, overwrite: true);
        return Task.CompletedTask;
    }

    private string? FindFile(TrackInfo track)
    {
        var primary = BuildFileName(track);
        var files = Directory.EnumerateFiles(_libraryPath, "*.lrc");
        var hit = files.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), primary, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            return hit;
        }

        // Album 非空时回退到旧模板：库里可能还有手贴的 {Artist} - {Name}.lrc。
        // Album 为空时 primary 本身就是旧模板，无需回退。
        if (string.IsNullOrWhiteSpace(track.Album))
        {
            return null;
        }

        var legacy = SanitizeFileName($"{track.Artist} - {track.Name}.lrc");
        return Directory.EnumerateFiles(_libraryPath, "*.lrc")
            .FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), legacy, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildFileName(TrackInfo track) => BuildFileNamePublic(track);

    /// <summary>
    /// 暴露给兄弟类 <see cref="LocalLyricsSearchProvider"/> 复用：候选搜索时也要按
    /// 同样的"Artist - Album - Name.lrc"模板构造 primary 文件名，避免定义两遍。
    /// </summary>
    internal static string BuildFileNamePublic(TrackInfo track)
    {
        var stem = string.IsNullOrWhiteSpace(track.Album)
            ? $"{track.Artist} - {track.Name}"
            : $"{track.Artist} - {track.Album} - {track.Name}";
        return SanitizeFileName(TruncateToLimit(stem) + ".lrc");
    }

    private static string TruncateToLimit(string stem)
    {
        if (stem.Length <= MaxStemLength)
        {
            return stem;
        }

        // Name 不可动，Artist 也尽量保留；超出预算从 Album 段截断。
        // stem 形如 "Artist - Album - Name" 或 "Artist - Name"。
        var parts = stem.Split(" - ");
        if (parts.Length == 0)
        {
            return stem[..MaxStemLength];
        }

        var name = parts[^1];
        var artist = parts.Length >= 2 ? parts[0] : string.Empty;
        var album = parts.Length >= 3 ? string.Join(" - ", parts[1..^1]) : string.Empty;

        // 预留 "Artist - " + " - " + "..." (3) + Album
        var fixedOverhead = artist.Length + 6; // " - " 分隔 + "..."
        if (name.Length + fixedOverhead >= MaxStemLength)
        {
            // 极端兜底：连 name 都放不下时直接截 stem 末尾（不应发生，name 通常 < 100）
            return stem[..MaxStemLength];
        }

        var albumBudget = MaxStemLength - name.Length - fixedOverhead;
        if (albumBudget <= 0)
        {
            album = string.Empty;
        }
        else if (album.Length > albumBudget)
        {
            album = album[..Math.Max(0, albumBudget - 3)] + "...";
        }

        return string.IsNullOrEmpty(album)
            ? $"{artist} - {name}"
            : $"{artist} - {album} - {name}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
