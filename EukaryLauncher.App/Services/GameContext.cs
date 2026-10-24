using System.Net.Http;
using System.Text.Json;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 跨页面共享的游戏上下文：路径、镜像、安装器、已安装版本列表。
/// 游戏目录变化时整体重建，避免页面之间各持一份过期状态。
/// </summary>
public sealed class GameContext
{
    private readonly SettingsService _settings;
    private readonly HttpClient _http;

    public GameContext(SettingsService settings)
    {
        _settings = settings;
        _http = HttpClientFactory.Create();
        Rebuild();
    }

    public GamePaths Paths { get; private set; } = null!;
    public IMirrorService Mirror { get; private set; } = null!;
    public VersionInstaller Installer { get; private set; } = null!;
    public JavaManager Java { get; private set; } = null!;
    public DownloadService Downloader { get; private set; } = null!;

    public HttpClient Http => _http;

    public event Action? InstalledVersionsChanged;

    private IReadOnlyList<string> _installed = Array.Empty<string>();
    public IReadOnlyList<string> InstalledVersions => _installed;

    public void Rebuild()
    {
        Paths = new GamePaths(_settings.Current.GameDirectory);
        Mirror = MirrorFactory.Create(_settings.Current.MirrorId);
        Installer = new VersionInstaller(Paths, Mirror, _http)
        {
            LibraryParallelism = Math.Clamp(_settings.Current.DownloadThreads, 1, 32),
            AssetParallelism = Math.Clamp(_settings.Current.DownloadThreads * 2, 4, 32),
        };
        Java = new JavaManager(GamePaths.RuntimeRoot, _http);
        Downloader = new DownloadService(_http);
        RefreshInstalled();
    }

    public void RefreshInstalled()
    {
        _installed = Installer.ListInstalled();
        InstalledVersionsChanged?.Invoke();
    }

    /// <summary>读取本地已安装版本的 JSON，并完成 inheritsFrom 合并。</summary>
    public VersionJson? LoadInstalledVersion(string versionId)
    {
        var path = Paths.VersionJsonPath(versionId);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Default);
            if (parsed is null) return null;

            return VersionMerger.Merge(parsed, parentId => LoadRawVersionJson(parentId));
        }
        catch
        {
            return null;
        }
    }

    private VersionJson? LoadRawVersionJson(string versionId)
    {
        var path = Paths.VersionJsonPath(versionId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(path), JsonOptions.Default);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>该版本是否真的装好了（json + jar 都在）。</summary>
    public bool IsInstalled(string versionId) =>
        File.Exists(Paths.VersionJsonPath(versionId)) && File.Exists(Paths.VersionJarPath(versionId));
}
