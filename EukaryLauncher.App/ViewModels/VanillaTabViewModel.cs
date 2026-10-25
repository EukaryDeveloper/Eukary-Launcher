using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

public sealed class InstalledVersionCard
{
    public required string Id { get; init; }
    public required string TypeLabel { get; init; }
    public required string SizeText { get; init; }
    public required string ReleaseTimeText { get; init; }
    public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();

    public string LoadersText => Loaders.Count == 0
        ? ""
        : string.Join(" · ", Loaders.Select(LoaderIds.DisplayName));
}

public sealed class AvailableVersionRow
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required DateTimeOffset ReleaseTime { get; init; }
    public bool Installed { get; set; }
    public string ReleaseTimeText => ReleaseTime.LocalDateTime.ToString("yyyy-MM-dd");
    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧 Beta",
        "old_alpha" => "旧 Alpha",
        _ => Type,
    };
}

/// <summary>
/// 下载页 · 原版游戏：已安装卡片 + 可下载清单。
/// 点「继续」不再直接下载，而是先去选模组加载器，确认后才入队。
/// </summary>
public sealed partial class VanillaTabViewModel : ViewModelBase
{
    private readonly GameContext _context = AppServices.Context;
    private readonly LogService _log = LogService.Instance;

    private List<AvailableVersionRow> _allRows = new();

    public event Action<AvailableVersionRow>? ContinueRequested;

    public VanillaTabViewModel()
    {
        _context.InstalledVersionsChanged += () => AppServices.Post(RefreshInstalled);
    }

    public ObservableCollection<InstalledVersionCard> Installed { get; } = new();
    public ObservableCollection<AvailableVersionRow> Available { get; } = new();

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool showSnapshots;
    [ObservableProperty] private bool showOldVersions;
    [ObservableProperty] private bool isLoadingManifest;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private AvailableVersionRow? selectedRow;

    /// <summary>已安装区默认收起，点标题展开。</summary>
    [ObservableProperty] private bool isInstalledExpanded;

    public string InstalledHeaderText => Installed.Count == 0 ? "已安装" : $"已安装（{Installed.Count}）";

    [RelayCommand]
    private void ToggleInstalled() => IsInstalledExpanded = !IsInstalledExpanded;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowSnapshotsChanged(bool value) => ApplyFilter();
    partial void OnShowOldVersionsChanged(bool value) => ApplyFilter();

    public override async Task OnNavigatedAsync()
    {
        RefreshInstalled();
        if (_allRows.Count == 0) await LoadManifestAsync();
    }

    public IReadOnlyList<string> VersionIds => _allRows.Select(r => r.Id).ToList();

    private void RefreshInstalled()
    {
        Installed.Clear();
        foreach (var id in _context.InstalledVersions)
        {
            var json = _context.LoadInstalledVersion(id);
            var jarPath = _context.Paths.VersionJarPath(id);
            var size = File.Exists(jarPath) ? new FileInfo(jarPath).Length : 0;

            var gameVersion = json is null ? null : LoaderDetector.DetectGameVersion(id, json);
            var loaders = json is null ? Array.Empty<string>() : LoaderDetector.Detect(json);

            Installed.Add(new InstalledVersionCard
            {
                Id = id,
                TypeLabel = json?.Type switch
                {
                    "release" => "正式版",
                    "snapshot" => "快照",
                    _ => "未知",
                },
                SizeText = Humanize.Bytes(size),
                ReleaseTimeText = json?.ReleaseTime?.LocalDateTime.ToString("yyyy-MM-dd") ?? "—",
                Loaders = loaders,
            });

            _ = gameVersion;
        }

        OnPropertyChanged(nameof(InstalledHeaderText));
    }

    public async Task LoadManifestAsync()
    {
        if (IsLoadingManifest) return;
        IsLoadingManifest = true;
        StatusText = "拉取版本清单…";

        try
        {
            var manifest = await _context.Installer.FetchManifestAsync(false);
            _allRows = manifest.Versions
                .OrderByDescending(v => v.ReleaseTime)
                .Select(v => new AvailableVersionRow
                {
                    Id = v.Id,
                    Type = v.Type,
                    ReleaseTime = v.ReleaseTime,
                })
                .ToList();
            ApplyFilter();
            StatusText = $"共 {_allRows.Count} 个版本";
        }
        catch (Exception ex)
        {
            StatusText = "版本清单拉取失败：" + ex.Message;
            _log.Error("版本清单拉取失败：" + ex.Message);
        }
        finally
        {
            IsLoadingManifest = false;
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<AvailableVersionRow> rows = _allRows;

        rows = rows.Where(r => r.Type == "release"
                               || (ShowSnapshots && r.Type == "snapshot")
                               || (ShowOldVersions && r.Type.StartsWith("old_", StringComparison.Ordinal)));

        if (!string.IsNullOrWhiteSpace(SearchText))
            rows = rows.Where(r => r.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        var snapshot = rows.Take(500).ToList();

        Available.Clear();
        foreach (var row in snapshot)
        {
            row.Installed = _context.IsInstalled(row.Id);
            Available.Add(row);
        }
    }

    [RelayCommand]
    private void Continue(AvailableVersionRow? row)
    {
        if (row is null) return;
        SelectedRow = row;
        ContinueRequested?.Invoke(row);
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            Directory.CreateDirectory(_context.Paths.Root);
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { _context.Paths.Root },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error("打开游戏目录失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(InstalledVersionCard? card)
    {
        if (card is null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"将删除 {card.Id} 的全部文件（版本目录、主 jar、natives），此操作不可撤销。",
            "删除版本",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        await Task.Run(() =>
        {
            try
            {
                Directory.Delete(_context.Paths.VersionDir(card.Id), recursive: true);
                _log.Info($"已删除版本 {card.Id}");
            }
            catch (Exception ex)
            {
                _log.Error($"删除 {card.Id} 失败：{ex.Message}");
            }
        });

        _context.RefreshInstalled();
        RefreshInstalled();
        ApplyFilter();
    }

    /// <summary>给加载器页用：把这一版对应的清单项找出来。</summary>
    public async Task<VersionManifestEntry?> FindEntryAsync(string versionId)
    {
        var manifest = await _context.Installer.FetchManifestAsync(false);
        return manifest.Versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.Ordinal));
    }
}
