using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

public enum LoaderState
{
    Checking,
    Available,
    Incompatible,
    Installed,
}

/// <summary>加载器选择页里的一行。</summary>
public sealed partial class LoaderRow : ObservableObject
{
    public LoaderRow(string id)
    {
        Id = id;
        Name = LoaderIds.DisplayName(id);
    }

    public string Id { get; }
    public string Name { get; }

    [ObservableProperty] private LoaderState state = LoaderState.Checking;
    [ObservableProperty] private string version = "";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private bool isSelected;

    public string StateText => State switch
    {
        LoaderState.Checking => "获取中",
        LoaderState.Available => "可以添加",
        LoaderState.Incompatible => "无",
        LoaderState.Installed => "已安装",
        _ => "",
    };

    public bool Selectable => State is LoaderState.Available;

    partial void OnStateChanged(LoaderState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Selectable));
    }

    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(Hint));
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(Selectable));
}

/// <summary>
/// 原版游戏点「继续」之后的选择页：挑一个模组加载器，选中的加载器会反过来影响其它加载器能否添加。
/// 真正开始下载要等点「开始下载」——这也是整个下载页「所有下载都排队」的统一入口。
/// </summary>
public sealed partial class LoaderSelectViewModel : ViewModelBase
{
    private readonly GameContext _context = AppServices.Context;
    private readonly DownloadManager _queue = AppServices.Queue;
    private readonly ToastService _toast = AppServices.Toasts;
    private readonly LogService _log = LogService.Instance;
    private readonly FabricMetaService _fabric;

    private string? _fabricLoaderVersion;

    /// <summary>各加载器自身（不考虑互斥）的可用性，互斥计算时以它为基准。</summary>
    private readonly Dictionary<string, LoaderState> _baseStates = new();

    public event Action? BackRequested;

    public LoaderSelectViewModel()
    {
        _fabric = new FabricMetaService(_context.Http);
        foreach (var id in new[] { LoaderIds.Forge, LoaderIds.NeoForge, LoaderIds.Fabric, LoaderIds.OptiFine })
            Rows.Add(new LoaderRow(id));
    }

    public ObservableCollection<LoaderRow> Rows { get; } = new();

    [ObservableProperty] private string gameVersion = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isChecking = true;

    [ObservableProperty] private LoaderRow? selectedLoader;
    partial void OnSelectedLoaderChanged(LoaderRow? value) => ApplyCompatibility();

    public bool HasSelection => SelectedLoader is not null;

    /// <summary>进入页面时调用：拿版本信息并跑一遍可用性检查。</summary>
    public async Task InitializeAsync(string gameVersion)
    {
        GameVersion = gameVersion;
        StatusText = $"Minecraft {gameVersion} · 正在获取加载器信息…";
        IsChecking = true;

        var installed = _context.InstalledLoaders(gameVersion);
        var tasks = Rows.Select(row => CheckAsync(row, gameVersion, installed));
        await Task.WhenAll(tasks);

        foreach (var row in Rows) _baseStates[row.Id] = row.State;
        ApplyCompatibility();
        IsChecking = false;
        StatusText = installed.Count == 0
            ? $"Minecraft {gameVersion} · 选择一个模组加载器，也可以不选直接安装原版"
            : $"Minecraft {gameVersion} · 已装 {string.Join(" / ", installed.Select(LoaderIds.DisplayName))}";
    }

    private async Task CheckAsync(LoaderRow row, string gameVersion, IReadOnlyList<string> installed)
    {
        if (installed.Contains(row.Id))
        {
            row.State = LoaderState.Installed;
            row.Hint = "该版本已安装";
            return;
        }

        switch (row.Id)
        {
            case LoaderIds.Fabric:
            {
                var versions = await _fabric.GetLoaderVersionsAsync(gameVersion);
                var latest = versions.FirstOrDefault(v => v.Stable) ?? versions.FirstOrDefault();
                if (latest is null)
                {
                    row.State = LoaderState.Incompatible;
                    row.Hint = "该游戏版本暂无可用加载器";
                }
                else
                {
                    _fabricLoaderVersion = latest.Version;
                    row.State = LoaderState.Available;
                    row.Version = latest.Version;
                    row.Hint = "可自动安装";
                }
                break;
            }

            case LoaderIds.NeoForge:
            {
                // NeoForge 只覆盖 1.20.1 及之后的版本
                if (IsAtLeast(gameVersion, "1.20.1"))
                {
                    row.State = LoaderState.Available;
                    row.Hint = "自动安装将在 M5 支持";
                }
                else
                {
                    row.State = LoaderState.Incompatible;
                    row.Hint = "该游戏版本不支持";
                }
                break;
            }

            case LoaderIds.Forge:
            {
                row.State = LoaderState.Available;
                row.Hint = "自动安装将在 M5 支持";
                break;
            }

            case LoaderIds.OptiFine:
            {
                var isSnapshot = gameVersion.Contains('w', StringComparison.OrdinalIgnoreCase)
                                 || (!gameVersion.Contains('.') && gameVersion.Length > 6);
                if (isSnapshot)
                {
                    row.State = LoaderState.Incompatible;
                    row.Hint = "快照版本不支持";
                }
                else
                {
                    row.State = LoaderState.Available;
                    row.Hint = "自动安装将在 M5 支持";
                }
                break;
            }
        }
    }

    /// <summary>选中一个加载器后，其它加载器是否还能加。</summary>
    private void ApplyCompatibility()
    {
        var selected = SelectedLoader?.Id;

        foreach (var row in Rows)
        {
            if (!_baseStates.TryGetValue(row.Id, out var baseState)) continue;

            row.State = IsCompatible(row.Id, selected) ? baseState : LoaderState.Incompatible;
            if (row.State == LoaderState.Incompatible && row.IsSelected) row.IsSelected = false;
        }

        OnPropertyChanged(nameof(HasSelection));
    }

    private static bool IsCompatible(string candidate, string? selected) => (candidate, selected) switch
    {
        (_, null) => true,
        // Fabric 与 OptiFine 不能共存（Fabric 生态用 Iris 代替）
        (LoaderIds.OptiFine, LoaderIds.Fabric) => false,
        (LoaderIds.Fabric, LoaderIds.OptiFine) => false,
        // Forge 与 NeoForge 同源分家，不能共存
        (LoaderIds.Forge, LoaderIds.NeoForge) => false,
        (LoaderIds.NeoForge, LoaderIds.Forge) => false,
        // Fabric 与 Forge 系也不能共存
        (LoaderIds.Fabric, LoaderIds.Forge or LoaderIds.NeoForge) => false,
        (LoaderIds.Forge or LoaderIds.NeoForge, LoaderIds.Fabric) => false,
        _ => true,
    };

    [RelayCommand]
    private void Select(LoaderRow? row)
    {
        if (row is null || !row.Selectable) return;

        // 再点一次取消选择
        if (SelectedLoader == row)
        {
            row.IsSelected = false;
            SelectedLoader = null;
            return;
        }

        foreach (var r in Rows) r.IsSelected = false;
        row.IsSelected = true;
        SelectedLoader = row;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        var vanillaInstalled = _context.IsInstalled(GameVersion);
        VersionManifestEntry? entry = null;

        if (!vanillaInstalled)
        {
            var manifest = await _context.Installer.FetchManifestAsync(false);
            entry = manifest.Versions.FirstOrDefault(v =>
                string.Equals(v.Id, GameVersion, StringComparison.Ordinal));

            if (entry is null)
            {
                _toast.Error($"版本清单里找不到 {GameVersion}，无法安装");
                return;
            }
        }

        if (!vanillaInstalled && entry is not null)
        {
            _queue.Enqueue(DownloadJobs.Vanilla(entry));
            _toast.Info($"Minecraft {GameVersion} 已加入下载队列");
        }

        if (SelectedLoader is { } loader)
        {
            if (loader.Id == LoaderIds.Fabric)
            {
                if (_fabricLoaderVersion is { Length: > 0 })
                {
                    _queue.Enqueue(DownloadJobs.FabricLoader(GameVersion, _fabricLoaderVersion));
                    _toast.Info($"Fabric Loader {_fabricLoaderVersion} 已加入下载队列");
                }
                else
                {
                    _toast.Warning("Fabric 加载器版本获取失败，请返回重试");
                }
            }
            else
            {
                _toast.Warning($"{loader.Name} 的自动安装将在 M5 支持，当前版本仅支持 Fabric");
                _log.Info($"用户选择了尚未支持的加载器：{loader.Name} / {GameVersion}");
            }
        }

        BackRequested?.Invoke();
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    private static bool IsAtLeast(string version, string baseline)
    {
        // 只比较 x.y.z 的前三段，快照（26.1-snapshot-4 这种）按前缀处理
        static int[] Parse(string v)
        {
            var core = v.Split('-')[0];
            var parts = core.Split('.');
            var nums = new int[3];
            for (var i = 0; i < Math.Min(parts.Length, 3); i++)
                int.TryParse(parts[i], out nums[i]);
            return nums;
        }

        var a = Parse(version);
        var b = Parse(baseline);
        for (var i = 0; i < 3; i++)
        {
            if (a[i] != b[i]) return a[i] > b[i];
        }
        return true;
    }
}
