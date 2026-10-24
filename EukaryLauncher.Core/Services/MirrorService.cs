namespace EukaryLauncher.Core.Services;

/// <summary>
/// 下载源抽象。国内直连 Mojang 源非常慢甚至超时，所以下载源必须可配置，不能硬编码死。
/// </summary>
public interface IMirrorService
{
    string Id { get; }
    string DisplayName { get; }

    string GetVersionManifestUrl();
    string GetVersionJsonUrl(string versionId, string officialUrl);
    string GetClientJarUrl(string versionId, string officialUrl);
    string GetLibraryUrl(string officialUrl, string mavenPath);
    string GetAssetIndexUrl(string assetIndexId, string officialUrl);
    string GetAssetObjectUrl(string hash, string officialUrl);
}

/// <summary>Mojang / Microsoft 官方源。</summary>
public sealed class OfficialMirror : IMirrorService
{
    public string Id => "official";
    public string DisplayName => "官方源（Mojang）";

    public string GetVersionManifestUrl() =>
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public string GetVersionJsonUrl(string versionId, string officialUrl) => officialUrl;

    public string GetClientJarUrl(string versionId, string officialUrl) => officialUrl;

    public string GetLibraryUrl(string officialUrl, string mavenPath) => officialUrl;

    public string GetAssetIndexUrl(string assetIndexId, string officialUrl) => officialUrl;

    public string GetAssetObjectUrl(string hash, string officialUrl) => officialUrl;
}

/// <summary>
/// BMCLAPI（Bangbang93 维护的国内公益镜像，启动器圈事实标准）。
/// 官方源地址不含版本 id，BMCLAPI 的 /version/{id}/... 需要版本 id，
/// 所以这里让调用方把版本 id 一起传进来。
/// </summary>
public sealed class BmclapiMirror : IMirrorService
{
    public const string BaseUrl = "https://bmclapi2.bangbang93.com";

    public string Id => "bmclapi";
    public string DisplayName => "BMCLAPI 镜像（国内推荐）";

    public string GetVersionManifestUrl() => $"{BaseUrl}/mc/game/version_manifest_v2.json";

    public string GetVersionJsonUrl(string versionId, string officialUrl) =>
        $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}/json";

    public string GetClientJarUrl(string versionId, string officialUrl) =>
        $"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}/client";

    public string GetLibraryUrl(string officialUrl, string mavenPath) =>
        $"{BaseUrl}/maven/{mavenPath.TrimStart('/')}";

    public string GetAssetIndexUrl(string assetIndexId, string officialUrl) =>
        $"{BaseUrl}/assets/indexes/{Uri.EscapeDataString(assetIndexId)}.json";

    public string GetAssetObjectUrl(string hash, string officialUrl) =>
        $"{BaseUrl}/assets/{hash[..2]}/{hash}";
}

public static class MirrorFactory
{
    public static IMirrorService Create(string? id) => id switch
    {
        "official" => new OfficialMirror(),
        "bmclapi" => new BmclapiMirror(),
        _ => new BmclapiMirror(), // 默认国内镜像
    };

    public static IReadOnlyList<IMirrorService> All { get; } =
        new IMirrorService[] { new BmclapiMirror(), new OfficialMirror() };

    /// <summary>给定一个源，返回另一个源（用作兜底）。</summary>
    public static IMirrorService OppositeOf(string id) =>
        string.Equals(id, "official", StringComparison.OrdinalIgnoreCase)
            ? new BmclapiMirror()
            : new OfficialMirror();
}
