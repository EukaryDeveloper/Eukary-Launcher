using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>版本列表里的一行：一个版本的主文件。</summary>
public sealed class ModFileRow
{
    public ModFileRow(ModrinthVersion version, ModrinthFile file, string loader, string gameVersion)
    {
        Version = version;
        File = file;
        Loader = loader;
        GameVersion = gameVersion;
    }

    public ModrinthVersion Version { get; }
    public ModrinthFile File { get; }
    public string Loader { get; }
    public string GameVersion { get; }

    public string VersionNumber => Version.VersionNumber;
    public string DisplayName => string.IsNullOrWhiteSpace(Version.Name) ? Version.VersionNumber : Version.Name;
    public string FileName => File.Filename;

    /// <summary>发布通道的首字母，与 Modrinth 的配色一致：R 正式 / B 测试 / A 内部。</summary>
    public string TypeLetter => Version.VersionType switch
    {
        "release" => "R",
        "beta" => "B",
        "alpha" => "A",
        _ => "?",
    };

    public string TypeName => Version.VersionType switch
    {
        "release" => "正式版",
        "beta" => "测试版",
        "alpha" => "内部版",
        _ => Version.VersionType,
    };

    public string DownloadsText => $"下载 {Humanize.Count(Version.Downloads)}";
    public string PublishedText => Humanize.Date(Version.DatePublished);
}

/// <summary>按「加载器 + 游戏版本」分组的一块，点击标题展开文件列表。</summary>
public sealed partial class ModVersionGroup : ObservableObject
{
    public ModVersionGroup(string loader, string gameVersion)
    {
        Loader = loader;
        GameVersion = gameVersion;
    }

    public string Loader { get; }
    public string GameVersion { get; }
    public List<ModFileRow> Rows { get; } = new();

    public string Title => Loader.Length == 0
        ? GameVersion
        : $"{LoaderIds.DisplayName(Loader)} {GameVersion}";

    [ObservableProperty] private bool isExpanded;
}

/// <summary>
/// 模组的版本选择页。点某个文件后才真正入队——入队前会先检查
/// 对应的游戏版本和加载器是否都装好了，缺什么就提示什么。
/// </summary>
public sealed partial class ModDetailViewModel : ViewModelBase
{
    private readonly ModrinthProject _project;
    private readonly GameContext _context = AppServices.Context;
    private readonly ModrinthService _modrinth;
    private readonly DownloadManager _queue = AppServices.Queue;
    private readonly ToastService _toast = AppServices.Toasts;
    private readonly LogService _log = LogService.Instance;

    private List<ModrinthVersion> _versions = new();

    public event Action? BackRequested;

    public ModDetailViewModel(ModrinthProject project)
    {
        _project = project;
        _modrinth = new ModrinthService(_context.Http);
    }

    public ModrinthProject Project => _project;
    public string Title => _project.Title;
    public string Description => _project.Description;
    public string ProjectUrl => ModrinthService.ProjectUrl(_project.Slug);

    public ObservableCollection<ModVersionGroup> Groups { get; } = new();

    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private string statusText = "读取版本列表…";
    [ObservableProperty] private int selectedVersionIndex;

    public IReadOnlyList<string> VersionChips { get; private set; } = new[] { "全部" };

    partial void OnSelectedVersionIndexChanged(int value) => RebuildGroups();

    private string CurrentGameVersion =>
        SelectedVersionIndex > 0 && SelectedVersionIndex < VersionChips.Count
            ? VersionChips[SelectedVersionIndex]
            : "";

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusText = "读取版本列表…";

        try
        {
            _versions = (await _modrinth.GetVersionsAsync(_project.ProjectId)).ToList();

            // 版本筛选条跟随模组页选的通道（正式版 / 快照版），顺序跟官方清单一致（新到旧）
            VersionChips = BuildVersionChips();
            OnPropertyChanged(nameof(VersionChips));

            RebuildGroups();
            StatusText = $"共 {_versions.Count} 个版本";
        }
        catch (Exception ex)
        {
            StatusText = "版本列表读取失败：" + ex.Message;
            _toast.Error("版本列表读取失败：" + ex.Message);
            _log.Error("版本列表读取失败：" + ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 把该模组支持的游戏版本按当前通道过滤。
    /// 优先用官方清单定顺序和类型；清单没拉到就退回旧逻辑（全部保留，按数字排序）。
    /// </summary>
    private string[] BuildVersionChips()
    {
        var supported = _versions.SelectMany(v => v.GameVersions).Distinct().ToList();
        var ordered = ModSearchOptions.Channel == ModReleaseChannel.Snapshot
            ? McVersionCatalog.Snapshots
            : McVersionCatalog.Releases;

        if (ordered.Count > 0)
        {
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < ordered.Count; i++)
                index.TryAdd(ordered[i], i);

            return new[] { "全部" }
                .Concat(supported.Where(index.ContainsKey).OrderBy(id => index[id]).Take(60))
                .ToArray();
        }

        // 清单不可用时的兜底：不做通道过滤，按老办法排
        return new[] { "全部" }
            .Concat(supported.OrderByDescending(v => v, new GameVersionComparer()).Take(60))
            .ToArray();
    }

    private void RebuildGroups()
    {
        Groups.Clear();

        var filter = CurrentGameVersion;

        var groups = new List<ModVersionGroup>();
        var ordered = _versions.OrderByDescending(v => v.DatePublished);

        foreach (var version in ordered)
        {
            var file = version.PrimaryFile;
            if (file is null) continue;

            var loader = version.Loaders.FirstOrDefault(LoaderIds.IsKnown) ?? "";

            foreach (var gameVersion in version.GameVersions)
            {
                if (filter.Length > 0 && !string.Equals(gameVersion, filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var group = groups.FirstOrDefault(g =>
                    g.Loader == loader && string.Equals(g.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase));

                if (group is null)
                {
                    group = new ModVersionGroup(loader, gameVersion);
                    groups.Add(group);
                }

                group.Rows.Add(new ModFileRow(version, file, loader, gameVersion));
            }
        }

        foreach (var group in groups.OrderBy(g => g.Loader).ThenByDescending(g => g.GameVersion, new GameVersionComparer()))
            Groups.Add(group);

        if (Groups.Count > 0) Groups[0].IsExpanded = true;

        StatusText = filter.Length == 0
            ? $"共 {_versions.Count} 个版本，按加载器与游戏版本分组"
            : $"Minecraft {filter} · 共 {Groups.Sum(g => g.Rows.Count)} 个可安装文件";
    }

    [RelayCommand]
    private void ToggleGroup(ModVersionGroup? group)
    {
        if (group is null) return;
        group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// 安装前的两道检查：游戏版本装了吗？该版本上装了对应加载器吗？
    /// 缺哪个就提示哪个，不缺才入队。
    /// </summary>
    [RelayCommand]
    private void Install(ModFileRow? row)
    {
        if (row is null) return;

        var gameVersion = row.GameVersion;
        var loader = row.Loader;

        if (!_context.IsMcVersionInstalled(gameVersion))
        {
            _toast.Warning($"尚未安装 Minecraft {gameVersion}，请先在「原版游戏」里安装它");
            return;
        }

        if (loader.Length > 0 && !_context.IsLoaderInstalled(gameVersion, loader))
        {
            _toast.Warning($"Minecraft {gameVersion} 上还没有 {LoaderIds.DisplayName(loader)}，" +
                           "请回到「原版游戏」点「继续」安装加载器");
            return;
        }

        if (AppServices.ModIndex.IsInstalled(gameVersion, _project.ProjectId))
        {
            var installed = AppServices.ModIndex.ForGameVersion(gameVersion)[_project.ProjectId];
            _toast.Warning($"{_project.Title} 在 {gameVersion} 上已安装（{installed.VersionNumber}）");
            return;
        }

        _queue.Enqueue(DownloadJobs.Mod(_project, row.Version, row.File, gameVersion, loader));
        _toast.Success($"开始安装 {_project.Title} {row.VersionNumber}（Minecraft {gameVersion}）");
        _log.Info($"入队模组：{_project.Title} {row.VersionNumber} / {gameVersion}");
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    [RelayCommand]
    private void OpenProjectPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectUrl)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打不开就算了
        }
    }
}

/// <summary>把 1.20.4 / 26.1-snapshot-4 这类版本串按数字从新到旧排。</summary>
internal sealed class GameVersionComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        static int[] Parse(string v)
        {
            var core = (v ?? "").Split('-')[0];
            var parts = core.Split('.');
            var nums = new int[3];
            for (var i = 0; i < Math.Min(parts.Length, 3); i++)
                int.TryParse(parts[i], out nums[i]);
            return nums;
        }

        var a = Parse(x ?? "");
        var b = Parse(y ?? "");
        for (var i = 0; i < 3; i++)
        {
            if (a[i] != b[i]) return b[i].CompareTo(a[i]);
        }
        return string.CompareOrdinal(y, x);
    }
}
