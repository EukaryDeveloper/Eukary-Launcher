using System.Text.Json;
using System.Text.Json.Serialization;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

public static class LoaderIds
{
    public const string Forge = "forge";
    public const string NeoForge = "neoforge";
    public const string Fabric = "fabric";
    public const string OptiFine = "optifine";

    public static string DisplayName(string id) => id switch
    {
        Forge => "Forge",
        NeoForge => "NeoForge",
        Fabric => "Fabric",
        OptiFine => "OptiFine",
        _ => id,
    };

    /// <summary>加载器 slug 是否已知（Modrinth 的 loaders 字段与这里对齐）。</summary>
    public static bool IsKnown(string id) =>
        id is Forge or NeoForge or Fabric or OptiFine;
}

/// <summary>meta.fabricmc.net 返回的一条记录。真实的结构是嵌套的：loader 字段里才是版本信息。</summary>
public sealed class FabricLoaderEntry
{
    [JsonPropertyName("loader")] public FabricLoaderVersion Loader { get; set; } = new();
}

public sealed class FabricLoaderVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("maven")] public string Maven { get; set; } = "";
    [JsonPropertyName("stable")] public bool Stable { get; set; }
}

/// <summary>
/// Fabric 官方元服务。Fabric 的安装方式非常干净：拿一份完整的版本 JSON 存下来就行，
/// 不需要跑安装器，这也是它比 Forge 好处理的原因。
/// </summary>
public sealed class FabricMetaService
{
    public const string MetaBase = "https://meta.fabricmc.net/v2";

    private readonly HttpClient _http;

    public FabricMetaService(HttpClient? http = null) => _http = http ?? HttpClientFactory.Create();

    /// <summary>该游戏版本上可用的 Fabric Loader 版本，最新的在前。拿不到就返回空（说明这个 MC 版本还不支持）。</summary>
    public async Task<IReadOnlyList<FabricLoaderVersion>> GetLoaderVersionsAsync(
        string gameVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{MetaBase}/versions/loader/{Uri.EscapeDataString(gameVersion)}";
            var json = await _http.GetStringAsync(url, ct);
            var list = JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json, JsonOptions.Default);
            return list?.Select(e => e.Loader).Where(l => l.Version.Length > 0).ToList()
                   ?? new List<FabricLoaderVersion>();
        }
        catch (HttpRequestException)
        {
            return Array.Empty<FabricLoaderVersion>();
        }
    }

    /// <summary>取 <c>fabric-loader-&lt;loader&gt;-&lt;mc&gt;</c> 这一份完整版本 JSON，已是合并后的形态。</summary>
    public async Task<VersionJson?> GetProfileAsync(
        string gameVersion, string loaderVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{MetaBase}/versions/loader/{Uri.EscapeDataString(gameVersion)}"
                      + $"/{Uri.EscapeDataString(loaderVersion)}/profile/json";
            var json = await _http.GetStringAsync(url, ct);
            var profile = JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Default);
            if (profile is null) return null;

            // 元服务给的 id 形如 fabric-loader-0.16.9-1.20.4，直接沿用，避免和其它加载器撞名
            profile.Id = $"fabric-loader-{loaderVersion}-{gameVersion}";
            return profile;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}

/// <summary>
/// 从已安装的版本里反推「某个 MC 版本上装了哪些加载器」。
/// 没有实例系统（M4 才做）之前，这是最省事也最准的判断方式。
/// </summary>
public static class LoaderDetector
{
    /// <summary>这个版本 JSON 里带了哪些加载器。</summary>
    public static IReadOnlyList<string> Detect(VersionJson json)
    {
        var result = new List<string>();
        var libs = json.Libraries.Select(l => l.Name).ToList();
        var mainClass = json.MainClass ?? "";

        if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase)
            || libs.Any(n => n.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase)))
            result.Add(LoaderIds.Fabric);

        if (libs.Any(n => n.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase))
            || mainClass.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            result.Add(LoaderIds.NeoForge);

        if (libs.Any(n => n.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase))
            || mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase))
            result.Add(LoaderIds.Forge);

        if (json.Id.Contains("OptiFine", StringComparison.OrdinalIgnoreCase)
            || libs.Any(n => n.Contains("optifine", StringComparison.OrdinalIgnoreCase)))
            result.Add(LoaderIds.OptiFine);

        return result;
    }

    /// <summary>这个版本是给哪个 MC 版本装的。</summary>
    public static string? DetectGameVersion(string versionId, VersionJson json)
    {
        if (!string.IsNullOrWhiteSpace(json.InheritsFrom)) return json.InheritsFrom;

        // Fabric：靠 intermediary 的 maven 坐标 net.fabricmc:intermediary:<mc>
        var intermediary = json.Libraries
            .Select(l => l.Name)
            .FirstOrDefault(n => n.Contains(":intermediary:", StringComparison.OrdinalIgnoreCase));
        if (intermediary is not null) return intermediary.Split(':').Last();

        var mojang = json.Libraries
            .Select(l => l.Name)
            .FirstOrDefault(n => n.StartsWith("com.mojang:minecraft:", StringComparison.OrdinalIgnoreCase));
        if (mojang is not null) return mojang.Split(':').Last();

        // Forge / OptiFine：1.20.4-forge-49.0.30 / 1.20.4-OptiFine_HD_U_I7
        if (versionId.Contains("-forge-", StringComparison.OrdinalIgnoreCase))
            return versionId.Split("-forge-")[0];
        if (versionId.Contains("-OptiFine", StringComparison.OrdinalIgnoreCase))
            return versionId.Split("-OptiFine")[0];

        return null;
    }
}
