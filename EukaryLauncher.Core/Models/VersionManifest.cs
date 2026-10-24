using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Models;

/// <summary>
/// version_manifest_v2.json 的根结构。
/// </summary>
public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestInfo? Latest { get; set; }

    [JsonPropertyName("versions")]
    public List<VersionManifestEntry> Versions { get; set; } = new();
}

public sealed class LatestInfo
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = string.Empty;

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;
}

public sealed class VersionManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>该版本版本 JSON 的下载地址（官方源）。</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTimeOffset Time { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset ReleaseTime { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("complianceLevel")]
    public int ComplianceLevel { get; set; }

    public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);
    public bool IsSnapshot => string.Equals(Type, "snapshot", StringComparison.OrdinalIgnoreCase);
}
