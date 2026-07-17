using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ABLyrics.App.Services.Spotify;

internal sealed class SpotifyTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SpotifyTokenStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABLyrics");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "spotify-tokens.dat");
    }

    public SpotifyTokenData? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_path);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SpotifyTokenData>(jsonBytes, JsonOptions);
        }
        catch (Exception ex)
        {
            // 别再静默吞了——DPAPI 解密失败 / 文件被别的进程半写 / 反序列化损坏 都会让用户误判为
            // "Spotify 服务端撤销"。至少在 DEBUG 时把异常类型与堆栈写到控制台，便于排查。
            DevExceptionReporter.Show(ex, "读取 Spotify token 缓存失败，将视为未登录");
            return null;
        }
    }

    public void Save(SpotifyTokenData data)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions));
        var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

internal sealed class SpotifyTokenData
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
