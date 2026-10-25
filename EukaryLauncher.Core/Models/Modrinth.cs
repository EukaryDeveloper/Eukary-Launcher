using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Models;

/// <summary>Modrinth 上的一个项目（mod / 资源包 / 光影 / 数据包 / 整合包）。</summary>
public sealed class ModrinthProject
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "mod";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    [JsonPropertyName("downloads")] public int Downloads { get; set; }
    [JsonPropertyName("follows")] public int Follows { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("date_modified")] public DateTimeOffset DateModified { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
    [JsonPropertyName("body")] public string? Body { get; set; }
}

public sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")] public List<ModrinthProject> Hits { get; set; } = new();
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
}

/// <summary>项目的一个版本，下面挂 1~N 个可下载文件。</summary>
public sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("changelog")] public string? Changelog { get; set; }
    [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }
    [JsonPropertyName("downloads")] public int Downloads { get; set; }
    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "release";
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("featured")] public bool Featured { get; set; }

    /// <summary>主文件（Modrinth 会标 primary，取不到就退回第一个）。</summary>
    public ModrinthFile? PrimaryFile => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

public sealed class ModrinthFile
{
    [JsonPropertyName("hashes")] public Dictionary<string, string> Hashes { get; set; } = new();
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }

    public string Sha1 => Hashes.TryGetValue("sha1", out var v) ? v : "";
    public string Sha512 => Hashes.TryGetValue("sha512", out var v) ? v : "";
}

/// <summary>Modrinth 的 category slug → 中文。没列到的直接显示英文原文。</summary>
public static class ModrinthCategories
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["fabric"] = "Fabric",
        ["forge"] = "Forge",
        ["neoforge"] = "NeoForge",
        ["quilt"] = "Quilt",
        ["optimization"] = "性能优化",
        ["library"] = "库",
        ["utility"] = "实用",
        ["decoration"] = "装饰",
        ["technology"] = "科技",
        ["magic"] = "魔法",
        ["worldgen"] = "世界生成",
        ["mobs"] = "生物",
        ["food"] = "食物",
        ["adventure"] = "冒险",
        ["cursed"] = "咒",
        ["storage"] = "存储",
        ["transportation"] = "交通",
        ["redstone"] = "红石",
        ["equipment"] = "装备",
        ["misc"] = "杂项",
        ["social"] = "社交",
        ["cosmetic"] = "外观",
        ["ui"] = "界面",
        ["game-mechanics"] = "游戏机制",
        ["audio"] = "音频",
        ["core-shaders"] = "核心着色器",
    };

    public static string ToChinese(string slug) => Map.TryGetValue(slug, out var cn) ? cn : slug;

    /// <summary>把 categories 分成「加载器」与「标签」两组，界面上分开显示。</summary>
    public static (IReadOnlyList<string> Loaders, IReadOnlyList<string> Tags) Split(IEnumerable<string> categories)
    {
        var loaders = new List<string>();
        var tags = new List<string>();
        foreach (var c in categories)
        {
            if (c is "fabric" or "forge" or "neoforge" or "quilt" or "rift" or "liteloader")
                loaders.Add(ToChinese(c));
            else if (c.StartsWith("game-")) continue;  // 游戏机制内部用，界面不展示
            else tags.Add(ToChinese(c));
        }
        return (loaders, tags);
    }
}
