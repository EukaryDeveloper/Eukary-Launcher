using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 启动器按游戏版本分目录存模组（mods/&lt;mc&gt;/），避免 1.20.4 的模组混进 1.19.4；
/// 但游戏只认游戏目录下的 mods/，M4 的独立实例做出来之前，启动前必须搬一次。
/// 搬的规则只有两条：先把不属于这个版本的、我们自己登记过的模组撤走，
/// 再把这个版本的模组复制过去。用户自己丢进 mods/ 的文件一律不碰。
/// </summary>
public static class ModDeployer
{
    /// <summary>把 <paramref name="gameVersion"/> 的模组同步到游戏真正会读的 mods 目录。</summary>
    /// <param name="gameDir">版本隔离时传版本目录（mods 落 versions/&lt;id&gt;/mods），null 表示游戏根目录。</param>
    /// <returns>复制到位的模组数量。</returns>
    public static int Deploy(GamePaths paths, string gameVersion, ModIndexService index, string? gameDir = null)
    {
        var shared = gameDir is null ? SharedModsDir(paths) : Path.Combine(gameDir, "mods");
        Directory.CreateDirectory(shared);

        // 1. 撤走别的游戏版本的模组。
        //    只有当这个文件在我们自己的分目录里也有一份时才删——删得掉就一定能还原，绝不丢文件。
        foreach (var (otherVersion, entries) in index.All())
        {
            if (string.Equals(otherVersion, gameVersion, StringComparison.OrdinalIgnoreCase)) continue;

            var source = paths.ModsDir(otherVersion);
            foreach (var entry in entries.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.FileName)) continue;
                if (!File.Exists(Path.Combine(source, entry.FileName))) continue;

                var target = Path.Combine(shared, entry.FileName);
                if (!File.Exists(target)) continue;

                TryDelete(target);
            }
        }

        // 2. 把这个版本的模组放进去
        var from = paths.ModsDir(gameVersion);
        if (!Directory.Exists(from)) return 0;

        var count = 0;
        foreach (var file in Directory.GetFiles(from, "*.jar"))
        {
            var target = Path.Combine(shared, Path.GetFileName(file));
            try
            {
                if (!File.Exists(target) || new FileInfo(file).Length != new FileInfo(target).Length)
                    File.Copy(file, target, overwrite: true);
                count++;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warn($"模组 {Path.GetFileName(file)} 同步失败：{ex.Message}");
            }
        }

        return count;
    }

    /// <summary>游戏会去读的 mods 目录：游戏目录下的 mods/，不是分目录。</summary>
    public static string SharedModsDir(GamePaths paths) => Path.Combine(paths.Root, "mods");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex)
        {
            LogService.Instance.Warn($"清理旧模组 {Path.GetFileName(path)} 失败：{ex.Message}");
        }
    }
}
