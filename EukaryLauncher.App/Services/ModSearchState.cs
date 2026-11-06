using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>模组适配的 Minecraft 版本通道。</summary>
public enum ModReleaseChannel
{
    /// <summary>正式版（release）。</summary>
    Release = 0,

    /// <summary>快照版（snapshot）。</summary>
    Snapshot = 1,
}

/// <summary>
/// 模组页共享的搜索偏好：正式版 / 快照版。
/// 搜索页的单选和模组详情页的版本筛选条都读这里，两边保持一致。
/// </summary>
public static class ModSearchOptions
{
    public static ModReleaseChannel Channel { get; set; } = ModReleaseChannel.Release;
}

/// <summary>
/// Minecraft 版本目录：把官方清单按类型分成两个有序列表（新到旧）。
/// 模组搜索页的版本下拉和详情页的版本筛选条共用，避免各自拉一遍清单。
/// </summary>
public static class McVersionCatalog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static List<string>? _releases;
    private static List<string>? _snapshots;

    /// <summary>按清单原始顺序（新到旧）的正式版 id。</summary>
    public static IReadOnlyList<string> Releases => _releases ?? [];

    /// <summary>按清单原始顺序（新到旧）的快照版 id。</summary>
    public static IReadOnlyList<string> Snapshots => _snapshots ?? [];

    public static bool IsRelease(string id) => _releases is not null && _releases.Contains(id);
    public static bool IsSnapshot(string id) => _snapshots is not null && _snapshots.Contains(id);

    /// <summary>确保清单已加载（6 小时内的本地缓存直接用，失败时保持上次的列表）。</summary>
    public static async Task EnsureLoadedAsync(GameContext context)
    {
        if (_releases is not null) return;

        await Gate.WaitAsync();
        try
        {
            if (_releases is not null) return;

            var manifest = await context.Installer.FetchManifestAsync(false);
            _releases = new List<string>();
            _snapshots = new List<string>();

            foreach (var v in manifest.Versions) // 清单本身就是新到旧
            {
                switch (v.Type)
                {
                    case "release":
                        _releases.Add(v.Id);
                        break;
                    case "snapshot":
                        _snapshots.Add(v.Id);
                        break;
                    // old_alpha / old_beta 是远古版本，模组基本不支持，不进这两个通道
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
