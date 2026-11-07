using System.Text.Json;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>版本隔离旗标的持久化载体，落在版本目录里的 sidecar 文件。</summary>
public sealed class VersionMetaFile
{
    /// <summary>true = 该版本用 versions/&lt;id&gt;/ 作独立游戏目录。</summary>
    public bool Isolated { get; set; }
}

/// <summary>
/// 每个版本是否隔离的记录。「版本隔离设定」只对之后新安装的版本生效：
/// 安装完成时按当时的全局设置把旗标写进版本目录，之后改设置不影响已装版本。
/// 这个功能之前安装的版本没有旗标，首次启动时按当前全局设置补记一次。
/// </summary>
public static class VersionIsolationStore
{
    private static string FlagPath(GamePaths paths, string versionId) =>
        Path.Combine(paths.VersionDir(versionId), "eukary.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>写旗标。版本目录不存在（还没装好）时跳过。</summary>
    public static void Set(GamePaths paths, string versionId, bool isolated)
    {
        try
        {
            var dir = paths.VersionDir(versionId);
            if (!Directory.Exists(dir)) return;

            var payload = new VersionMetaFile { Isolated = isolated };
            File.WriteAllText(FlagPath(paths, versionId), JsonSerializer.Serialize(payload, Options));
        }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"记录版本隔离状态失败（{versionId}）：{ex.Message}");
        }
    }

    /// <summary>读旗标；没有记录（旧安装）返回 null。</summary>
    public static bool? Get(GamePaths paths, string versionId)
    {
        try
        {
            var path = FlagPath(paths, versionId);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<VersionMetaFile>(File.ReadAllText(path), JsonOptions.Default)?.Isolated;
        }
        catch
        {
            return null;
        }
    }
}
