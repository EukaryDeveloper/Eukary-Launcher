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
        LoaderState.Available => "可添加",
        LoaderState.Incompatible => "不可用",
        LoaderState.Installed => "已安装",
        _ => "",
    };

    /// <summary>能点 = 现在可以勾上，或者已经勾上（再点一下取消）。</summary>
    public bool CanToggle => State == LoaderState.Available || IsSelected;

    partial void OnStateChanged(LoaderState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanToggle));
    }

    partial void OnVersionChanged(string value) => OnPropertyChanged(nameof(Hint));

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
}

/// <summary>
/// 原版游戏点「继续」之后的选择页：挑要装哪些模组加载器。
/// 能共存的可以多选（Forge + OptiFine 就是最典型的组合），不能共存的会直接置灰并说明原因。
/// 真正开始下载要等点「开始下载」——这也是整个下载页「所有下载都排队」的统一入口。
/// </summary>
public sealed partial class LoaderSelectViewModel : ViewModelBase
{
    private readonly GameContext _context = AppServices.Context;
    private readonly DownloadManager _queue = AppServices.Queue;
    private readonly ToastService _toast = AppServices.Toasts;
    private readonly LogService _log = LogService.Instance;
    private readonly FabricMetaService _fabric;

    /// <summary>各加载器自身（不考虑互斥）能不能装，互斥计算时以它为基准。</summary>
    private readonly Dictionary<string, bool> _usable = new();

    private ForgeBuild? _forge;
    private NeoForgeBuild? _neoForge;
    private OptiFineBuild? _optiFine;
    private string? _fabricLoaderVersion;

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

    public IReadOnlyList<string> SelectedIds =>
        Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();

    public bool HasSelection => Rows.Any(r => r.IsSelected);

    /// <summary>进入页面时调用：拿版本信息并跑一遍可用性检查。</summary>
    public async Task InitializeAsync(string gameVersion)
    {
        GameVersion = gameVersion;
        StatusText = $"Minecraft {gameVersion} · 正在获取加载器信息…";
        IsChecking = true;

        var installed = _context.InstalledLoaders(gameVersion);

        var tasks = Rows.Select(row => CheckAsync(row, gameVersion, installed));
        await Task.WhenAll(tasks);

        foreach (var row in Rows)
            _usable[row.Id] = row.State == LoaderState.Available;

        RefreshCompatibility();
        IsChecking = false;

        StatusText = installed.Count == 0
            ? $"Minecraft {gameVersion} · 勾选要一起装的加载器，也可以一个都不选"
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

            case LoaderIds.Forge:
            {
                var builds = await LoaderMeta.GetForgeBuildsAsync(gameVersion);
                var latest = LoaderMeta.LatestForge(builds);
                if (latest is null)
                {
                    row.Hint = "该游戏版本没有可用的 Forge";
                }
                else
                {
                    _forge = latest;
                    row.State = LoaderState.Available;
                    row.Version = latest.Version;
                    row.Hint = "可自动安装";
                }
                break;
            }

            case LoaderIds.NeoForge:
            {
                var builds = await LoaderMeta.GetNeoForgeBuildsAsync(gameVersion);
                var latest = LoaderMeta.LatestNeoForge(builds);
                if (latest is null)
                {
                    row.Hint = "该游戏版本没有可用的 NeoForge";
                }
                else
                {
                    _neoForge = latest;
                    row.State = LoaderState.Available;
                    row.Version = latest.Version;
                    row.Hint = "可自动安装";
                }
                break;
            }

            case LoaderIds.OptiFine:
            {
                var builds = await LoaderMeta.GetOptiFineBuildsAsync(gameVersion);
                var latest = LoaderMeta.LatestOptiFine(builds);
                if (latest is null)
                {
                    row.Hint = "该游戏版本没有可用的 OptiFine";
                }
                else
                {
                    _optiFine = latest;
                    row.State = LoaderState.Available;
                    row.Version = latest.SelfVersion;
                    row.Hint = latest.IsPreview
                        ? "预览版 · 与 Forge 一起装时作为模组装入"
                        : "与 Forge 一起装时作为模组装入";
                }
                break;
            }
        }
    }

    /// <summary>勾选项变化后重算每一行的可用性（互斥是双向的）。</summary>
    private void RefreshCompatibility()
    {
        var selected = SelectedIds;

        foreach (var row in Rows)
        {
            if (row.State == LoaderState.Installed) continue;

            // 已经选上的永远可以取消
            if (row.IsSelected)
            {
                row.State = LoaderState.Available;
                continue;
            }

            if (_usable.TryGetValue(row.Id, out var usable) && usable
                && CanCoexist(selected.Append(row.Id)))
            {
                row.State = LoaderState.Available;
                continue;
            }

            row.State = LoaderState.Incompatible;
            row.Hint = RejectReason(row.Id, selected);
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>这一组加载器能不能同时装在一个版本上。</summary>
    private static bool CanCoexist(IEnumerable<string> ids)
    {
        var set = ids.ToList();
        bool Has(string id) => set.Contains(id);

        // Forge / NeoForge / Fabric 是三套互斥的加载体系，只能选一个
        if (set.Count(id => id is LoaderIds.Forge or LoaderIds.NeoForge or LoaderIds.Fabric) > 1)
            return false;

        // Fabric 生态用 Iris 代替 OptiFine，两者不能共存
        if (Has(LoaderIds.Fabric) && Has(LoaderIds.OptiFine)) return false;

        // OptiFine 官方只标了 Forge 的兼容版本，NeoForge 上不保证能跑
        if (Has(LoaderIds.NeoForge) && Has(LoaderIds.OptiFine)) return false;

        return true;
    }

    /// <summary>为什么这一行现在不能勾。</summary>
    private string RejectReason(string candidate, IReadOnlyList<string> selected)
    {
        if (selected.Count == 0) return "该游戏版本暂不可用";

        if (candidate is LoaderIds.Forge or LoaderIds.NeoForge or LoaderIds.Fabric
            && selected.Any(s => s is LoaderIds.Forge or LoaderIds.NeoForge or LoaderIds.Fabric))
            return "与已选的加载器不能共存";

        if (candidate == LoaderIds.OptiFine)
        {
            if (selected.Contains(LoaderIds.Fabric)) return "Fabric 上请用 Iris 代替";
            if (selected.Contains(LoaderIds.NeoForge)) return "OptiFine 不支持 NeoForge";
        }

        if (candidate == LoaderIds.Fabric && selected.Contains(LoaderIds.OptiFine))
            return "与 OptiFine 不能共存（请用 Iris）";

        if (candidate == LoaderIds.NeoForge && selected.Contains(LoaderIds.OptiFine))
            return "与 OptiFine 不能共存";

        return "暂不可用";
    }

    [RelayCommand]
    private void Toggle(LoaderRow? row)
    {
        if (row is null) return;

        if (row.IsSelected)
        {
            row.IsSelected = false;
        }
        else
        {
            if (!_usable.TryGetValue(row.Id, out var usable) || !usable) return;
            if (!CanCoexist(SelectedIds.Append(row.Id))) return;
            row.IsSelected = true;
        }

        RefreshCompatibility();
    }

    public string SummaryText
    {
        get
        {
            var selected = SelectedIds;
            if (selected.Count == 0) return "不会安装任何加载器，只装原版";
            return string.Join(" + ", selected.Select(LoaderIds.DisplayName)) + " 将一起安装";
        }
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

        var selected = SelectedIds;
        var withForge = selected.Contains(LoaderIds.Forge);

        // 加载器先装，OptiFine 放最后——它要在加载器就位之后才决定以什么形态落地
        foreach (var id in selected)
        {
            switch (id)
            {
                case LoaderIds.Fabric:
                    if (_fabricLoaderVersion is { Length: > 0 })
                    {
                        _queue.Enqueue(DownloadJobs.FabricLoader(GameVersion, _fabricLoaderVersion));
                        _toast.Info($"Fabric Loader {_fabricLoaderVersion} 已加入下载队列");
                    }
                    else
                    {
                        _toast.Warning("Fabric 加载器版本获取失败，请返回重试");
                    }
                    break;

                case LoaderIds.Forge:
                    if (_forge is { } forge)
                    {
                        _queue.Enqueue(DownloadJobs.Forge(GameVersion, forge.Version));
                        _toast.Info($"Forge {forge.Version} 已加入下载队列");
                    }
                    break;

                case LoaderIds.NeoForge:
                    if (_neoForge is { } neo)
                    {
                        _queue.Enqueue(DownloadJobs.NeoForge(GameVersion, neo.Version));
                        _toast.Info($"NeoForge {neo.Version} 已加入下载队列");
                    }
                    break;
            }
        }

        if (selected.Contains(LoaderIds.OptiFine) && _optiFine is { } of)
        {
            _queue.Enqueue(DownloadJobs.OptiFine(of, withForge));
            _toast.Info($"OptiFine {of.SelfVersion} 已加入下载队列");
        }

        _log.Info($"准备安装 {GameVersion}" + (selected.Count == 0
            ? "（仅原版）"
            : $"（{string.Join(" + ", selected.Select(LoaderIds.DisplayName))}）"));

        BackRequested?.Invoke();
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();
}
