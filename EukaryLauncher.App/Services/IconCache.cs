using System.Collections.Concurrent;
using System.Net.Http;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 模组图标缓存。Modrinth 的图标是外链小图，反复翻页时不要每次都重新拉，
/// 内存里按 URL 存一份足够（一页 20 个，最多几 MB）。
/// </summary>
public sealed class IconCache
{
    public static IconCache Instance { get; } = new();

    private readonly ConcurrentDictionary<string, byte[]> _memory = new();
    private readonly ConcurrentDictionary<string, bool> _failed = new();

    private IconCache()
    {
        Directory.CreateDirectory(DiskRoot);
    }

    private static string DiskRoot => Path.Combine(GamePaths.AppDataRoot, "cache", "icons");

    public bool TryGet(string? url, out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(url)) return false;
        return _memory.TryGetValue(url, out bytes);
    }

    public async Task<byte[]?> GetAsync(string? url, HttpClient http, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (_memory.TryGetValue(url, out var cached)) return cached;
        if (_failed.ContainsKey(url)) return null;

        var path = DiskPathFor(url);
        try
        {
            byte[] data;
            if (File.Exists(path))
            {
                data = await File.ReadAllBytesAsync(path, ct);
            }
            else
            {
                data = await http.GetByteArrayAsync(url, ct);
                try { await File.WriteAllBytesAsync(path, data, ct); }
                catch { /* 缓存写失败无所谓 */ }
            }

            _memory[url] = data;
            return data;
        }
        catch
        {
            _failed[url] = true;
            return null;
        }
    }

    private static string DiskPathFor(string url)
    {
        var name = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(url)))[..16] + ".img";
        return Path.Combine(DiskRoot, name);
    }
}
