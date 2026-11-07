using System.Text.Json;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>扫描出的一个游戏版本条目（一个文件夹内的 versions/&lt;id&gt;）。</summary>
public sealed class FolderVersionInfo
{
    public string Id { get; init; } = "";
    /// <summary>本体游戏版本（从 inheritsFrom / 库坐标反推；原版即 id 本身）。</summary>
    public string GameVersion { get; init; } = "";
    /// <summary>MOD 加载器描述，如「Fabric 0.19.5」；原版为「原版」。</summary>
    public string LoaderText { get; init; } = "原版";
    /// <summary>jar 是否存在（json+jar 都在才算完整可启动）。</summary>
    public bool HasJar { get; init; }
}

/// <summary>
/// 扫描任意游戏文件夹下的 versions 目录，识别每个版本的本体版本号与加载器。
/// 与 GameContext.InstalledVersions 的区别：这里针对任意路径的文件夹，
/// 供版本选择页展示「这个文件夹里有哪些版本」。
/// </summary>
public static class FolderVersionScanner
{
    public static IReadOnlyList<FolderVersionInfo> Scan(string root)
    {
        var versionsDir = Path.Combine(root, "versions");
        var result = new List<FolderVersionInfo>();
        if (!Directory.Exists(versionsDir)) return result;

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = Path.Combine(dir, id + ".json");
            if (!File.Exists(jsonPath)) continue;

            VersionJson? json = null;
            try
            {
                json = JsonSerializer.Deserialize<VersionJson>(
                    File.ReadAllText(jsonPath), JsonOptions.Default);
            }
            catch
            {
                continue;
            }
            if (json is null) continue;

            var hasJar = File.Exists(Path.Combine(dir, id + ".jar"));
            var gameVersion = LoaderDetector.DetectGameVersion(id, json) ?? id;
            var loader = DescribeLoaders(id, json);

            result.Add(new FolderVersionInfo
            {
                Id = id,
                GameVersion = gameVersion,
                LoaderText = string.IsNullOrEmpty(loader) ? "原版" : loader,
                HasJar = hasJar,
            });
        }

        // 本体版本新→旧，再按 id 排
        return result
            .OrderByDescending(v => v.GameVersion, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(v => v.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>拼「Fabric 0.19.5」这类加载器描述；原版返回 null。</summary>
    private static string? DescribeLoaders(string id, VersionJson json)
    {
        var parts = new List<string>();
        var libs = json.Libraries.Select(l => l.Name ?? "").ToList();

        var fabric = libs.FirstOrDefault(n =>
            n.StartsWith("net.fabricmc:fabric-loader:", StringComparison.OrdinalIgnoreCase));
        if (fabric is not null)
            parts.Add("Fabric " + fabric.Split(':').Last());

        var neo = libs.FirstOrDefault(n =>
            n.StartsWith("net.neoforged:neoforge:", StringComparison.OrdinalIgnoreCase));
        if (neo is not null)
            parts.Add("NeoForge " + neo.Split(':').Last());

        var forge = libs.FirstOrDefault(n =>
            n.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase));
        if (forge is not null)
        {
            // 坐标形如 net.minecraftforge:forge:1.20.4-49.0.30
            parts.Add("Forge " + forge.Split(':').Last());
        }
        else if (id.Contains("-forge-", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Forge " + id.Split("-forge-").Last());
        }

        if (id.Contains("OptiFine", StringComparison.OrdinalIgnoreCase))
        {
            // id 形如 1.20.4-OptiFine_HD_U_I7，取 OptiFine 之后的部分还原成 HD U I7
            var tail = id[(id.IndexOf("OptiFine", StringComparison.OrdinalIgnoreCase) + "OptiFine".Length)..]
                .TrimStart('_');
            parts.Add("OptiFine " + tail.Replace('_', ' '));
        }
        else
        {
            var optifine = libs.FirstOrDefault(n =>
                n.StartsWith("OptiFine:OptiFine:", StringComparison.OrdinalIgnoreCase));
            if (optifine is not null)
                parts.Add("OptiFine " + optifine.Split(':').Last().Replace('_', ' '));
        }

        return parts.Count == 0 ? null : string.Join(" + ", parts);
    }
}
