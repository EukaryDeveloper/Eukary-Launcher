using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// inheritsFrom 深度合并：把父版本的 libraries、arguments 等字段合并到子版本上，
/// 子版本已有的字段覆盖父版本。缺少库会直接导致 ClassNotFound，这是启动器第一大坑。
/// </summary>
public static class VersionMerger
{
    /// <summary>
    /// 把 <paramref name="child"/> 合并到其所有祖先之上，返回一份新的、可直接使用的版本 JSON。
    /// <paramref name="parentProvider"/> 用于按 id 取父版本 JSON。
    /// </summary>
    public static VersionJson Merge(VersionJson child, Func<string, VersionJson?> parentProvider)
    {
        var result = Clone(child);
        var seen = new HashSet<string>(StringComparer.Ordinal) { child.Id };

        var current = child;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current.InheritsFrom) && guard++ < 32)
        {
            var parentId = current.InheritsFrom!;
            if (!seen.Add(parentId))
                break; // 环形继承，兜底

            var parent = parentProvider(parentId);
            if (parent is null)
                throw new InvalidOperationException($"找不到父版本 {parentId} 的版本 JSON。");

            result = MergePair(result, parent);
            current = parent;
        }

        return result;
    }

    private static VersionJson MergePair(VersionJson child, VersionJson parent)
    {
        var merged = new VersionJson
        {
            Id = child.Id,
            InheritsFrom = null,
            MainClass = Pick(child.MainClass, parent.MainClass),
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Assets = child.Assets ?? parent.Assets,
            Downloads = MergeDownloads(child.Downloads, parent.Downloads),
            Libraries = MergeLibraries(child.Libraries, parent.Libraries),
            Arguments = MergeArguments(child.Arguments, parent.Arguments),
            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Logging = child.Logging ?? parent.Logging,
            Type = child.Type ?? parent.Type,
            Time = child.Time ?? parent.Time,
            ReleaseTime = child.ReleaseTime ?? parent.ReleaseTime,
            MinimumLauncherVersion = Math.Max(child.MinimumLauncherVersion, parent.MinimumLauncherVersion),
        };
        return merged;
    }

    private static string Pick(string child, string parent) =>
        string.IsNullOrWhiteSpace(child) ? parent : child;

    private static Dictionary<string, DownloadInfo>? MergeDownloads(
        Dictionary<string, DownloadInfo>? child,
        Dictionary<string, DownloadInfo>? parent)
    {
        if (child is null) return parent;
        if (parent is null) return child;

        var merged = new Dictionary<string, DownloadInfo>(parent, StringComparer.Ordinal);
        foreach (var (key, value) in child)
            merged[key] = value;
        return merged;
    }

    private static List<Library> MergeLibraries(List<Library> child, List<Library> parent)
    {
        // 子版本同名库优先，其余按父版本顺序补齐，最后追加子版本独有库
        var merged = new List<Library>();
        var childNames = new HashSet<string>(
            child.Select(l => l.Name), StringComparer.Ordinal);

        foreach (var parentLib in parent)
        {
            var own = child.FirstOrDefault(c => string.Equals(c.Name, parentLib.Name, StringComparison.Ordinal));
            merged.Add(own ?? parentLib);
        }

        foreach (var childLib in child)
        {
            if (!childNames.Contains(childLib.Name) || !parent.Any(p => string.Equals(p.Name, childLib.Name, StringComparison.Ordinal)))
                merged.Add(childLib);
        }

        return merged;
    }

    private static ArgumentsSpec? MergeArguments(ArgumentsSpec? child, ArgumentsSpec? parent)
    {
        if (child is null) return parent;
        if (parent is null) return child;

        return new ArgumentsSpec
        {
            // 游戏参数：父在前，子在后（子版本常追加 --tweakClass 之类）
            Game = MergeArgumentList(parent.Game, child.Game),
            Jvm = MergeArgumentList(parent.Jvm, child.Jvm),
        };
    }

    private static List<ArgumentEntry> MergeArgumentList(List<ArgumentEntry> first, List<ArgumentEntry> second)
    {
        var merged = new List<ArgumentEntry>(first.Count + second.Count);
        merged.AddRange(first.Select(CloneEntry));
        merged.AddRange(second.Select(CloneEntry));
        return merged;
    }

    private static ArgumentEntry CloneEntry(ArgumentEntry e) => new()
    {
        Rules = e.Rules,
        Values = new List<string>(e.Values),
    };

    private static VersionJson Clone(VersionJson src) => new()
    {
        Id = src.Id,
        InheritsFrom = src.InheritsFrom,
        MainClass = src.MainClass,
        AssetIndex = src.AssetIndex,
        Assets = src.Assets,
        Downloads = src.Downloads is null ? null : new Dictionary<string, DownloadInfo>(src.Downloads, StringComparer.Ordinal),
        Libraries = new List<Library>(src.Libraries),
        Arguments = src.Arguments is null
            ? null
            : new ArgumentsSpec
            {
                Game = src.Arguments.Game.Select(CloneEntry).ToList(),
                Jvm = src.Arguments.Jvm.Select(CloneEntry).ToList(),
            },
        MinecraftArguments = src.MinecraftArguments,
        JavaVersion = src.JavaVersion,
        Logging = src.Logging,
        Type = src.Type,
        Time = src.Time,
        ReleaseTime = src.ReleaseTime,
        MinimumLauncherVersion = src.MinimumLauncherVersion,
    };
}
