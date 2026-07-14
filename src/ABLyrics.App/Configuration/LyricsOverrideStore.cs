using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ABLyrics.App.Models;

namespace ABLyrics.App.Configuration;

/// <summary>
/// 歌词版本覆盖项的持久化：trackKey → <see cref="CandidateOrigin"/> 的字典写到
/// %LOCALAPPDATA%/ABLyrics/lyrics-overrides.json。LRCLIB 等接口可能在升级，
/// JSON 字段未来可能新增，统一用 <c>version</c> 字段标记文件格式版本以便后向兼容。
/// </summary>
public sealed class LyricsOverrideStore
{
    public const string DefaultFileName = "lyrics-overrides.json";
    internal const int CurrentSchemaVersion = 1;

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ABLyrics",
        DefaultFileName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 默认文件路径（开发期可在测试/其它场景下用 <c>path</c> 参数覆盖）。
    /// </summary>
    public static string GetDefaultPath() => DefaultPath;

    /// <summary>
    /// 加载全部覆盖项。文件不存在 → 空；JSON 损坏 → 静默返回空（在 DEBUG 模式
    /// 下通过 <c>DevExceptionReporter</c> 报告到 stderr，便于排查）。
    /// </summary>
    public IReadOnlyDictionary<string, CandidateOrigin> Load(string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!File.Exists(p))
        {
            return new Dictionary<string, CandidateOrigin>();
        }

        try
        {
            var json = File.ReadAllText(p);
            var dto = JsonSerializer.Deserialize<OverrideFileDto>(json, JsonOptions);
            if (dto?.Overrides is null)
            {
                return new Dictionary<string, CandidateOrigin>();
            }

            var result = new Dictionary<string, CandidateOrigin>(dto.Overrides.Count);
            foreach (var kv in dto.Overrides)
            {
                result[kv.Key] = DeserializeOrigin(kv.Value);
            }
            return result;
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "歌词覆盖项加载失败");
            return new Dictionary<string, CandidateOrigin>();
        }
    }

    /// <summary>
    /// 写入单个覆盖项（Add 或 Replace）。先 Load 再 Put 再序列化整文件，
    /// 保证其他键不被破坏。目录不存在时自动创建。
    /// </summary>
    public void Save(string trackKey, CandidateOrigin origin, string? path = null)
    {
        var p = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var current = Load(p);
        var newMap = new Dictionary<string, CandidateOrigin>(current.Count + 1);
        foreach (var kv in current)
        {
            newMap[kv.Key] = kv.Value;
        }
        newMap[trackKey] = origin;

        var dto = new OverrideFileDto
        {
            Version = CurrentSchemaVersion,
            Overrides = ToOriginDtoMap(newMap),
        };
        File.WriteAllText(p, JsonSerializer.Serialize(dto, JsonOptions));
    }

    /// <summary>
    /// 移除单个覆盖项。文件不存在或不包含该 key → 直接返回不写盘。
    /// 若 Remove 后字典已空，仍写一次空文件以便显式持久化"无覆盖"。
    /// </summary>
    public void Remove(string trackKey, string? path = null)
    {
        var p = path ?? DefaultPath;
        if (!File.Exists(p)) return;

        var current = Load(p);
        if (!current.ContainsKey(trackKey))
        {
            return;
        }

        var newMap = new Dictionary<string, CandidateOrigin>(current.Count);
        foreach (var kv in current)
        {
            if (kv.Key != trackKey)
            {
                newMap[kv.Key] = kv.Value;
            }
        }

        var dto = new OverrideFileDto
        {
            Version = CurrentSchemaVersion,
            Overrides = ToOriginDtoMap(newMap),
        };
        File.WriteAllText(p, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static Dictionary<string, OriginDto> ToOriginDtoMap(
        Dictionary<string, CandidateOrigin> map)
    {
        var result = new Dictionary<string, OriginDto>(map.Count);
        foreach (var kv in map)
        {
            result[kv.Key] = SerializeOrigin(kv.Value);
        }
        return result;
    }

    private static OriginDto SerializeOrigin(CandidateOrigin origin) => origin switch
    {
        CandidateOrigin.Local l => new OriginDto { Kind = "Local", FilePath = l.FilePath },
        CandidateOrigin.Lrclib l => new OriginDto { Kind = "Lrclib", LrclibId = l.LrclibId },
        CandidateOrigin.Netease n => new OriginDto { Kind = "Netease", NeteaseSongId = n.NeteaseSongId },
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
    };

    private static CandidateOrigin DeserializeOrigin(OriginDto dto)
    {
        return dto.Kind switch
        {
            "Local" => new CandidateOrigin.Local(dto.FilePath ?? string.Empty),
            "Lrclib" => new CandidateOrigin.Lrclib(dto.LrclibId ?? 0),
            "Netease" => new CandidateOrigin.Netease(dto.NeteaseSongId ?? 0L),
            _ => throw new InvalidDataException(
                $"未知的歌词来源类型: '{dto.Kind}'。可能因 schema 版本不兼容，请升级 ABLyrics。"),
        };
    }

    /// <summary>
    /// 文件根 DTO：版本 + 覆盖项字典。版本号用于以后做 migration。
    /// </summary>
    private sealed class OverrideFileDto
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("overrides")]
        public Dictionary<string, OriginDto> Overrides { get; set; } = new();
    }

    /// <summary>
    /// 单条覆盖项的 DTO：用一个 <c>kind</c> 字段标 record 变体类型，
    /// 其余字段按 kind 选填。
    /// </summary>
    private sealed class OriginDto
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("lrclibId")]
        public int? LrclibId { get; set; }

        [JsonPropertyName("neteaseSongId")]
        public long? NeteaseSongId { get; set; }
    }
}
