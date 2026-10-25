using System.Text.Json;
using System.Text.Json.Serialization;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

public sealed class ModIndexEntry
{
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("versionId")] public string VersionId { get; set; } = "";
    [JsonPropertyName("versionNumber")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("loader")] public string Loader { get; set; } = "";
    [JsonPropertyName("installedAt")] public DateTimeOffset InstalledAt { get; set; }
}

/// <summary>
/// 记录「哪个游戏版本上装了哪个模组的哪个版本」。
/// 游戏目录里只放 jar 本身，索引放在启动器自己的数据目录——
/// 免得往 mods 文件夹里塞额外文件，游戏加载时的行为也更可控。
/// </summary>
public sealed class ModIndexService
{
    private readonly string _path;
    private Dictionary<string, Dictionary<string, ModIndexEntry>> _data = new();

    public ModIndexService()
    {
        _path = Path.Combine(GamePaths.AppDataRoot, "mods-index.json");
        Load();
    }

    /// <summary>列表页 / 详情页用来显示「已安装」标记。</summary>
    public IReadOnlyDictionary<string, ModIndexEntry> ForGameVersion(string gameVersion) =>
        _data.TryGetValue(gameVersion, out var map) ? map : new Dictionary<string, ModIndexEntry>();

    public bool IsInstalled(string gameVersion, string projectId) =>
        _data.TryGetValue(gameVersion, out var map) && map.ContainsKey(projectId);

    public void Record(string gameVersion, ModrinthProject project, ModrinthVersion version,
                       ModrinthFile file, string loader)
    {
        if (!_data.TryGetValue(gameVersion, out var map))
        {
            map = new Dictionary<string, ModIndexEntry>();
            _data[gameVersion] = map;
        }

        map[project.ProjectId] = new ModIndexEntry
        {
            ProjectId = project.ProjectId,
            Title = project.Title,
            Slug = project.Slug,
            VersionId = version.Id,
            VersionNumber = version.VersionNumber,
            FileName = file.Filename,
            Loader = loader,
            InstalledAt = DateTimeOffset.Now,
        };

        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ModIndexEntry>>>(json, JsonOptions.Default)
                    ?? new Dictionary<string, Dictionary<string, ModIndexEntry>>();
        }
        catch
        {
            _data = new Dictionary<string, Dictionary<string, ModIndexEntry>>();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions.Default));
        }
        catch
        {
            // 索引写不进去不影响游戏本身，静默
        }
    }
}
