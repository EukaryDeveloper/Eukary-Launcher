using System.Text.Json;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// .minecraft 目录里的所有关键路径。路径规则集中在这里，避免各处硬编码。
/// </summary>
public sealed class GamePaths
{
    public GamePaths(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string VersionsDir => Path.Combine(Root, "versions");
    public string LibrariesDir => Path.Combine(Root, "libraries");
    public string AssetsDir => Path.Combine(Root, "assets");
    public string AssetIndexesDir => Path.Combine(AssetsDir, "indexes");
    public string AssetObjectsDir => Path.Combine(AssetsDir, "objects");
    public string LogConfigsDir => Path.Combine(AssetsDir, "log_configs");
    public string ResourcePacksDir => Path.Combine(Root, "resourcepacks");
    public string ShaderPacksDir => Path.Combine(Root, "shaderpacks");
    public string SavesDir => Path.Combine(Root, "saves");

    public string VersionDir(string versionId) => Path.Combine(VersionsDir, versionId);
    public string VersionJsonPath(string versionId) => Path.Combine(VersionDir(versionId), versionId + ".json");
    public string VersionJarPath(string versionId) => Path.Combine(VersionDir(versionId), versionId + ".jar");

    /// <summary>natives 按版本隔离，避免不同版本的 lwjgl 互相污染。</summary>
    public string NativesDir(string versionId) => Path.Combine(VersionDir(versionId), "natives");

    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EukaryLauncher", ".minecraft");

    /// <summary>启动器自身的数据目录（runtime、日志、配置）。</summary>
    public static string AppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EukaryLauncher");

    public static string RuntimeRoot => Path.Combine(AppDataRoot, "runtime");
    public static string LogsRoot => Path.Combine(AppDataRoot, "logs");
    public static string SettingsPath => Path.Combine(AppDataRoot, "settings.json");
    public static string PluginsRoot => Path.Combine(AppDataRoot, "plugins");
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };
}
