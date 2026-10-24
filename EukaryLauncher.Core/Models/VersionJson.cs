using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Models;

/// <summary>
/// 版本 JSON（versions/&lt;id&gt;/&lt;id&gt;.json）的数据模型。
/// 1.13+ 使用 arguments 数组；1.12 及更早使用 minecraftArguments 字符串，两者都保留字段。
/// </summary>
public sealed class VersionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>父版本 id。Forge / Fabric / OptiFine 版本靠这个继承原版。</summary>
    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = string.Empty;

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("downloads")]
    public Dictionary<string, DownloadInfo>? Downloads { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("arguments")]
    public ArgumentsSpec? Arguments { get; set; }

    /// <summary>1.13 之前的旧格式，整串参数用空格分隔。</summary>
    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionInfo? JavaVersion { get; set; }

    [JsonPropertyName("logging")]
    public LoggingSpec? Logging { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }

    [JsonPropertyName("minimumLauncherVersion")]
    public int MinimumLauncherVersion { get; set; }

    /// <summary>客户端主 jar 的下载信息。</summary>
    public DownloadInfo? ClientDownload =>
        Downloads is not null && Downloads.TryGetValue("client", out var d) ? d : null;
}

public sealed class ArgumentsSpec
{
    [JsonPropertyName("game")]
    public List<ArgumentEntry> Game { get; set; } = new();

    [JsonPropertyName("jvm")]
    public List<ArgumentEntry> Jvm { get; set; } = new();
}

/// <summary>
/// arguments 数组里的元素：既可能是纯字符串，也可能是 { rules, value } 对象。
/// 反序列化统一成"规则 + 一个或多个人值"。
/// </summary>
[JsonConverter(typeof(ArgumentEntryConverter))]
public sealed class ArgumentEntry
{
    public List<Rule>? Rules { get; set; }

    /// <summary>展开后的参数片段（一个 entry 可能贡献多个 token）。</summary>
    public List<string> Values { get; set; } = new();
}

public sealed class Rule
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "allow";

    [JsonPropertyName("os")]
    public OsCondition? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }

    public bool IsAllow => string.Equals(Action, "allow", StringComparison.OrdinalIgnoreCase);
}

public sealed class OsCondition
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class Library
{
    /// <summary>maven 坐标，如 org.lwjgl:lwjgl:3.3.2。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; set; }

    /// <summary>os name -> classifier，如 { "windows": "natives-windows" }。</summary>
    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("extract")]
    public ExtractSpec? Extract { get; set; }

    /// <summary>部分库（Forge）自带 maven 仓库基址。</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>LiteLoader / 老 Forge 用，标记该库仅客户端需要。</summary>
    [JsonPropertyName("clientreq")]
    public bool? ClientReq { get; set; }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadInfo? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public sealed class ExtractSpec
{
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

public sealed class DownloadInfo
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long? TotalSize { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class AssetIndexFile
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = new();

    /// <summary>旧版本为 true，需要把资源映射到 resources 目录而非 objects。</summary>
    [JsonPropertyName("virtual")]
    public bool Virtual { get; set; }

    [JsonPropertyName("map_to_resources")]
    public bool MapToResources { get; set; }
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>assets/objects/&lt;hash[0..2]&gt;/&lt;hash&gt;</summary>
    public string RelativePath => $"{Hash[..2]}/{Hash}";
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public sealed class LoggingSpec
{
    [JsonPropertyName("client")]
    public LoggingEntry? Client { get; set; }
}

public sealed class LoggingEntry
{
    [JsonPropertyName("argument")]
    public string? Argument { get; set; }

    [JsonPropertyName("file")]
    public DownloadInfo? File { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
