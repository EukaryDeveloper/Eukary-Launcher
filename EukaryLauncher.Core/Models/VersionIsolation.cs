namespace EukaryLauncher.Core.Models;

/// <summary>
/// 版本隔离策略：决定新安装的版本是否用 versions/&lt;id&gt;/ 作为独立游戏目录
/// （存档、资源包、配置、options.txt 都放版本目录里，不与其他版本混）。
/// 库、资产仍然共享游戏根目录的 libraries/ 与 assets/。
/// </summary>
public enum VersionIsolation
{
    /// <summary>不隔离，所有版本共用游戏根目录。</summary>
    Off = 0,

    /// <summary>只隔离可安装 Mod 的版本（Fabric / Forge / NeoForge / OptiFine 等）。</summary>
    ModdedOnly = 1,

    /// <summary>只隔离非正式版（快照、旧测试版）。</summary>
    SnapshotsOnly = 2,

    /// <summary>隔离可安装 Mod 的版本与非正式版。</summary>
    ModdedAndSnapshots = 3,

    /// <summary>隔离所有版本（默认）。</summary>
    All = 4,
}

/// <summary>按全局设置判断一个版本要不要隔离。判定是纯函数，安装时与启动时共用。</summary>
public static class IsolationPolicy
{
    /// <summary>库文件或主类里带这些关键字的，认定为可安装 Mod 的版本。</summary>
    private static readonly string[] LoaderKeywords = { "fabric", "forge", "quilt", "optifine", "lexforge" };

    public static bool IsModded(VersionJson version)
    {
        if (version.InheritsFrom is { Length: > 0 }) return true;
        if (version.MainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase)
            || version.MainClass.Contains("forge", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var lib in version.Libraries)
        {
            var name = lib.Name;
            if (name is null) continue;
            foreach (var keyword in LoaderKeywords)
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }

    /// <summary>非 release 的都算「非正式版」：快照、old_beta、old_alpha。
    /// 版本 JSON 缺 type 字段时按 release 处理（Fabric 元服务的 profile 就不带）。</summary>
    public static bool IsSnapshot(VersionJson version) =>
        version.Type is { Length: > 0 } type
        && !string.Equals(type, "release", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldIsolate(VersionIsolation setting, VersionJson version) => setting switch
    {
        VersionIsolation.All => true,
        VersionIsolation.ModdedOnly => IsModded(version),
        VersionIsolation.SnapshotsOnly => IsSnapshot(version),
        VersionIsolation.ModdedAndSnapshots => IsModded(version) || IsSnapshot(version),
        _ => false,
    };
}
