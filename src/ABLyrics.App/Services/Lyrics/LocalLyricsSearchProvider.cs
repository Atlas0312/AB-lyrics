using System.IO;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;

namespace ABLyrics.App.Services.Lyrics;

/// <summary>
/// 本地歌词库的"多候选"搜索提供者：基于现有 <see cref="LocalLyricsProvider"/> 的
/// 文件命名模板做 primary 匹配，再叠加文件名同时含 Artist + Name（大小写不敏感）
/// 的模糊匹配。返回的候选会同步读盘把内容附上，UI 不必再二次访问文件系统。
/// 与 <see cref="LocalLyricsProvider"/> 并存，不修改后者任何现有行为。
/// </summary>
public sealed class LocalLyricsSearchProvider
{
    private readonly string _libraryPath;

    public LocalLyricsSearchProvider(AppSettings settings)
    {
        var configured = settings.Lyrics.LocalPath;
        _libraryPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ABLyrics", "lyrics")
            : configured;

        Directory.CreateDirectory(_libraryPath);
    }

    /// <summary>
    /// 暴露库目录，便于 Step 6 的探活判定。
    /// </summary>
    public string LibraryPath => _libraryPath;

    /// <summary>
    /// 多候选搜索。返回候选按"文件名短度升序"排列，让短命名的精确命中优先。
    /// 去重按文件路径（OrdinalIgnoreCase）——同一文件被 primary + 模糊双重匹配只列一次。
    /// </summary>
    public IReadOnlyList<LyricsCandidate> Search(TrackInfo track)
    {
        if (!Directory.Exists(_libraryPath))
        {
            return Array.Empty<LyricsCandidate>();
        }

        var artistLower = (track.Artist ?? string.Empty).ToLowerInvariant();
        var nameLower = (track.Name ?? string.Empty).ToLowerInvariant();
        var primary = LocalLyricsProvider.BuildFileNamePublic(track);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<(string path, int length, bool isPrimary)>();

        // 1) primary 匹配：按现有命名模板精确找一次
        TryAdd(primary, isPrimary: true, seen, matches);

        // 2) 模糊匹配：文件名同时包含 Artist 和 Name（大小写不敏感）
        if (artistLower.Length > 0 && nameLower.Length > 0)
        {
            foreach (var file in Directory.EnumerateFiles(_libraryPath, "*.lrc"))
            {
                var fname = Path.GetFileName(file);
                var fnameLower = fname.ToLowerInvariant();
                if (fnameLower.Contains(artistLower) && fnameLower.Contains(nameLower))
                {
                    TryAdd(file, isPrimary: false, seen, matches);
                }
            }
        }

        // 3) 排序：文件名短度升序；同长度保留扫描顺序稳定
        matches.Sort((a, b) => a.length.CompareTo(b.length));

        // 4) 读盘 + 转 Candidate
        var result = new List<LyricsCandidate>(matches.Count);
        foreach (var (path, _, _) in matches)
        {
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch
            {
                // 单文件读取失败不应拖累整体结果
                continue;
            }

            result.Add(new LyricsCandidate
            {
                Source = "Local",
                Label = Path.GetFileNameWithoutExtension(path),
                SyncedLyrics = content,
                PlainLyrics = content,
                DurationMs = track.DurationMs,
                Origin = new CandidateOrigin.Local(path),
            });
        }
        return result;
    }

    private void TryAdd(
        string pathOrName,
        bool isPrimary,
        HashSet<string> seen,
        List<(string path, int length, bool isPrimary)> matches)
    {
        string? fullPath = null;

        if (Path.IsPathRooted(pathOrName))
        {
            if (File.Exists(pathOrName)) fullPath = pathOrName;
        }
        else
        {
            var candidate = Path.Combine(_libraryPath, pathOrName);
            if (File.Exists(candidate)) fullPath = candidate;
        }

        if (fullPath is null) return;

        // 路径归一后去重
        if (!seen.Add(fullPath)) return;

        var fname = Path.GetFileName(fullPath);
        matches.Add((fullPath, fname.Length, isPrimary));
    }
}