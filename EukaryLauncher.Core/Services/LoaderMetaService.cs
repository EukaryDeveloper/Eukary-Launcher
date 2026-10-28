namespace EukaryLauncher.Core.Services;

/// <summary>BMCLAPI 的 Forge 列表里的一条。</summary>
internal sealed class ForgeListEntry
{
    public string? Version { get; set; }
    public List<ForgeListFile>? Files { get; set; }
}

internal sealed class ForgeListFile
{
    public string? Format { get; set; }
    public string? Category { get; set; }
}

/// <summary>BMCLAPI 的 NeoForge 列表里的一条。</summary>
internal sealed class NeoForgeListEntry
{
    public string? Version { get; set; }
    public string? RawVersion { get; set; }
    public string? InstallerPath { get; set; }
}

/// <summary>BMCLAPI 的 OptiFine 列表里的一条。</summary>
internal sealed class OptiFineListEntry
{
    public string? McVersion { get; set; }
    public string? Type { get; set; }
    public string? Patch { get; set; }
    public string? FileName { get; set; }
    public string? Forge { get; set; }
}

public sealed record ForgeBuild(string GameVersion, string Version)
{
    /// <summary>maven 坐标里的版本段，形如 1.20.4-49.0.31。</summary>
    public string MavenVersion => $"{GameVersion}-{Version}";
    public string VersionId => $"{GameVersion}-forge-{Version}";
}

public sealed record NeoForgeBuild(string GameVersion, string Version, string InstallerPath)
{
    public string VersionId => $"{GameVersion}-neoforge-{Version}";
}

public sealed record OptiFineBuild(string GameVersion, string Type, string Patch, string FileName, string Forge)
{
    /// <summary>OptiFine 自己的版本号，形如 HD_U_I7。</summary>
    public string SelfVersion => $"{Type}_{Patch}";

    /// <summary>maven 坐标里的版本段，形如 1.20.4_HD_U_I7。</summary>
    public string MavenVersion => $"{GameVersion}_{SelfVersion}";

    public string VersionId => $"{GameVersion}-OptiFine_{SelfVersion}";

    public bool IsPreview =>
        Patch.StartsWith("pre", StringComparison.OrdinalIgnoreCase)
        || FileName.StartsWith("preview_", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => IsPreview ? $"{SelfVersion}（预览）" : SelfVersion;

    /// <summary>OptiFine 官方标出的兼容 Forge 版本，形如 "Forge 49.0.31" / "Forge N/A"。</summary>
    public string ForgeCompatText => Forge;
}

/// <summary>
/// 加载器的版本清单。三个来源都是 BMCLAPI 的公开接口——
/// Forge / NeoForge 官方只有网页没有 API，OptiFine 更是连网页 API 都没有。
/// 拿不到就返回空列表，由调用方决定怎么提示。
/// </summary>
public static class LoaderMeta
{
    private const string Bmclapi = "https://bmclapi2.bangbang93.com";

    private static readonly HttpClient Http = HttpClientFactory.Create();

    private static async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(url, ct);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, JsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return default;
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    // ---------------------------------------------------------------- Forge

    public static async Task<IReadOnlyList<ForgeBuild>> GetForgeBuildsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        var entries = await GetJsonAsync<List<ForgeListEntry>>(
            $"{Bmclapi}/forge/minecraft/{Uri.EscapeDataString(gameVersion)}", ct);

        return (entries ?? new List<ForgeListEntry>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Version)
                        && e.Files is { } files
                        && files.Any(f => f.Format == "jar" && f.Category == "installer"))
            .Select(e => new ForgeBuild(gameVersion, e.Version!))
            .OrderByDescending(b => b.Version, VersionTextComparer.Instance)
            .ToList();
    }

    /// <summary>最新的 Forge（按版本号排，不是按接口返回顺序）。</summary>
    public static ForgeBuild? LatestForge(IReadOnlyList<ForgeBuild> builds) =>
        builds.OrderByDescending(b => b.Version, VersionTextComparer.Instance).FirstOrDefault();

    public static string ForgeInstallerUrl(ForgeBuild build, bool official = false)
    {
        var name = $"forge-{build.MavenVersion}-installer.jar";
        return official
            ? $"https://maven.minecraftforge.net/net/minecraftforge/forge/{build.MavenVersion}/{name}"
            : $"{Bmclapi}/maven/net/minecraftforge/forge/{build.MavenVersion}/{name}";
    }

    // ------------------------------------------------------------- NeoForge

    public static async Task<IReadOnlyList<NeoForgeBuild>> GetNeoForgeBuildsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        var entries = await GetJsonAsync<List<NeoForgeListEntry>>(
            $"{Bmclapi}/neoforge/list/{Uri.EscapeDataString(gameVersion)}", ct);

        return (entries ?? new List<NeoForgeListEntry>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Version))
            .Select(e =>
            {
                var version = e.Version!;
                // 部分条目（尤其是老的 1.20.1 那批）没有给 installerPath，按坐标补出来
                var path = string.IsNullOrWhiteSpace(e.InstallerPath)
                    ? $"/maven/net/neoforged/neoforge/{version}/neoforge-{version}-installer.jar"
                    : e.InstallerPath!;
                return new NeoForgeBuild(gameVersion, version, path);
            })
            .OrderByDescending(b => b.Version, VersionTextComparer.Instance)
            .ToList();
    }

    /// <summary>
    /// 最新的稳定版 NeoForge。接口里 beta / alpha 混在一起，
    /// 默认只挑正式版；该游戏版本一个正式版都没有时才退到最新的预览版。
    /// </summary>
    public static NeoForgeBuild? LatestNeoForge(IReadOnlyList<NeoForgeBuild> builds)
    {
        static bool IsPreview(NeoForgeBuild b) =>
            b.Version.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || b.Version.Contains("alpha", StringComparison.OrdinalIgnoreCase);

        var ordered = builds.OrderByDescending(b => b.Version, VersionTextComparer.Instance).ToList();
        return ordered.FirstOrDefault(b => !IsPreview(b)) ?? ordered.FirstOrDefault();
    }

    public static string NeoForgeInstallerUrl(NeoForgeBuild build, bool official = false)
    {
        var path = build.InstallerPath.TrimStart('/');
        // BMCLAPI 的路径带 /maven 前缀，官方仓库的根目录下直接就是 net/…
        return official
            ? "https://maven.neoforged.net/releases/" + path.Replace("maven/", "", StringComparison.Ordinal)
            : $"{Bmclapi}/{path}";
    }

    // ------------------------------------------------------------- OptiFine

    public static async Task<IReadOnlyList<OptiFineBuild>> GetOptiFineBuildsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        var entries = await GetJsonAsync<List<OptiFineListEntry>>(
            $"{Bmclapi}/optifine/{Uri.EscapeDataString(gameVersion)}", ct);

        return (entries ?? new List<OptiFineListEntry>())
            .Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.Patch))
            .Select(e => new OptiFineBuild(
                gameVersion,
                e.Type!,
                e.Patch!,
                e.FileName ?? $"OptiFine_{gameVersion}_{e.Type}_{e.Patch}.jar",
                e.Forge ?? ""))
            .OrderByDescending(b => b.Patch, VersionTextComparer.Instance)
            .ToList();
    }

    /// <summary>正式版优先；该游戏版本还没出正式版时选最新的预览版。</summary>
    public static OptiFineBuild? LatestOptiFine(IReadOnlyList<OptiFineBuild> builds)
    {
        var ordered = builds
            .OrderBy(b => b.IsPreview)
            .ThenByDescending(b => b.Patch, VersionTextComparer.Instance)
            .ToList();
        return ordered.FirstOrDefault();
    }

    /// <summary>
    /// OptiFine 的下载链接。官方站点没有公开 API（下载还要过广告页），
    /// 所以只走 BMCLAPI——这也是国内启动器圈的通用做法。
    /// </summary>
    public static string OptiFineDownloadUrl(OptiFineBuild build) =>
        $"{Bmclapi}/optifine/{Uri.EscapeDataString(build.GameVersion)}"
        + $"/{Uri.EscapeDataString(build.Type)}/{Uri.EscapeDataString(build.Patch)}";

    /// <summary>安装包放到启动器自己的目录里，装完也不删，重装同类版本能直接复用。</summary>
    public static string LocalInstallerPath(string fileName) =>
        Path.Combine(GamePaths.AppDataRoot, "installers", fileName);
}

/// <summary>
/// 按数字分段比较版本串（49.2.13 &gt; 49.0.31，I7 &gt; I6）。
/// 非数字段就按字符串比，比不了也不崩。
/// </summary>
internal sealed class VersionTextComparer : IComparer<string>
{
    public static readonly VersionTextComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var left = Split(x);
        var right = Split(y);

        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;
            if (a != b) return a.CompareTo(b);
        }

        return string.CompareOrdinal(x, y);
    }

    private static int[] Split(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new[] { 0 };

        var parts = value.Split('.', '_', '-');
        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            // 纯数字直接解析；I7 这种"字母+数字"取尾部的数字
            var segment = parts[i];
            if (!int.TryParse(segment, out numbers[i]))
            {
                var digits = new string(segment.Reverse().TakeWhile(char.IsAsciiDigit).Reverse().ToArray());
                int.TryParse(digits, out numbers[i]);
            }
        }
        return numbers;
    }
}
