using System.Text.Json;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>搜索条件。project_type 决定这一页是模组 / 资源包 / 数据包 / 光影。</summary>
public sealed record ModrinthSearchRequest(
    string Query = "",
    string ProjectType = "mod",
    string? Loader = null,
    string? GameVersion = null,
    string? Category = null,
    int Limit = 20,
    int Offset = 0);

/// <summary>
/// Modrinth v2 API 客户端。无需鉴权，只需要带一个像样的 UA（HttpClientFactory 已带）。
/// 文档：https://docs.modrinth.com/api/
/// </summary>
public sealed class ModrinthService
{
    public const string ApiBase = "https://api.modrinth.com/v2";

    private readonly HttpClient _http;

    public ModrinthService(HttpClient? http = null) => _http = http ?? HttpClientFactory.Create();

    /// <summary>项目类型 → 中文页名（各 Tab 共用一个搜索器）。</summary>
    public static string ProjectTypeOf(DownloadTabKind tab) => tab switch
    {
        DownloadTabKind.Mod => "mod",
        DownloadTabKind.DataPack => "datapack",
        DownloadTabKind.ResourcePack => "resourcepack",
        DownloadTabKind.ShaderPack => "shader",
        DownloadTabKind.ModPack => "modpack",
        _ => "mod",
    };

    public async Task<ModrinthSearchResponse> SearchAsync(ModrinthSearchRequest request, CancellationToken ct = default)
    {
        // facets 是 [[a],[b]] 这种嵌套数组：内层 OR，外层 AND
        var facets = new List<string> { $"\"project_type:{request.ProjectType}\"" };
        if (!string.IsNullOrEmpty(request.Loader)) facets.Add($"\"categories:{request.Loader}\"");
        if (!string.IsNullOrEmpty(request.Category)) facets.Add($"\"categories:{request.Category}\"");
        if (!string.IsNullOrEmpty(request.GameVersion)) facets.Add($"\"versions:{request.GameVersion}\"");

        var facetJson = "[" + string.Join(",", facets.Select(f => "[" + f + "]")) + "]";

        var url = $"{ApiBase}/search"
                  + $"?query={Uri.EscapeDataString(request.Query)}"
                  + $"&limit={Math.Clamp(request.Limit, 1, 100)}"
                  + $"&offset={Math.Max(0, request.Offset)}"
                  + "&index=relevance"
                  + $"&facets={Uri.EscapeDataString(facetJson)}";

        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<ModrinthSearchResponse>(json, JsonOptions.Default)
               ?? new ModrinthSearchResponse();
    }

    /// <summary>取项目的全部版本（可按加载器 / 游戏版本过滤）。</summary>
    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectId,
        IReadOnlyList<string>? loaders = null,
        IReadOnlyList<string>? gameVersions = null,
        CancellationToken ct = default)
    {
        var url = $"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version";
        var args = new List<string>();

        if (loaders is { Count: > 0 })
            args.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(loaders)));
        if (gameVersions is { Count: > 0 })
            args.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(gameVersions)));

        if (args.Count > 0) url += "?" + string.Join("&", args);

        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<List<ModrinthVersion>>(json, JsonOptions.Default)
               ?? new List<ModrinthVersion>();
    }

    public async Task<ModrinthProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/project/{Uri.EscapeDataString(projectId)}";
        try
        {
            var json = await _http.GetStringAsync(url, ct);
            return JsonSerializer.Deserialize<ModrinthProject>(json, JsonOptions.Default);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public static string ProjectUrl(string slugOrId) => $"https://modrinth.com/mod/{slugOrId}";
}

/// <summary>下载页的 Tab 类型（Core 层用得到搜索类型，View 层用得到顺序）。</summary>
public enum DownloadTabKind
{
    Vanilla,
    Mod,
    ModPack,
    DataPack,
    ResourcePack,
    ShaderPack,
    Queue,
}
