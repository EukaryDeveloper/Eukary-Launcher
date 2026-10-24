using System.Collections.ObjectModel;
using System.IO;
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

/// <summary>版本列表页：已安装卡片 + 可下载清单。</summary>
public sealed partial class VersionsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly GameContext _context;
    private readonly LogService _log = LogService.Instance;

    private CancellationTokenSource? _cts;
    private List<AvailableVersionRow> _allRows = new();

    public VersionsViewModel(SettingsService settings)
    {
        _settings = settings;
        _context = AppServices.Context;
        _context.InstalledVersionsChanged += () =>
            AppServices.Post(RefreshInstalled);
    }

    public ObservableCollection<InstalledVersionCard> Installed { get; } = new();
    public ObservableCollection<AvailableVersionRow> Available { get; } = new();

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool showSnapshots;
    [ObservableProperty] private bool showOldVersions;
    [ObservableProperty] private bool isLoadingManifest;
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private AvailableVersionRow? selectedRow;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnShowSnapshotsChanged(bool value) => ApplyFilter();
    partial void OnShowOldVersionsChanged(bool value) => ApplyFilter();

    public override async Task OnNavigatedAsync()
    {
        RefreshInstalled();
        await LoadManifestAsync();
    }

    private void RefreshInstalled()
    {
        Installed.Clear();
        foreach (var id in _context.InstalledVersions)
        {
            var json = _context.LoadInstalledVersion(id);
            var jarPath = _context.Paths.VersionJarPath(id);
            var size = File.Exists(jarPath) ? new FileInfo(jarPath).Length : 0;

            Installed.Add(new InstalledVersionCard
            {
                Id = id,
                TypeLabel = json?.Type switch
                {
                    "release" => "正式版",
                    "snapshot" => "快照",
                    _ => "未知",
                },
                SizeText = FormatBytes(size),
                ReleaseTimeText = json?.ReleaseTime?.LocalDateTime.ToString("yyyy-MM-dd") ?? "—",
            });
        }
    }

    private async Task LoadManifestAsync()
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
    private async Task InstallAsync(AvailableVersionRow? row)
    {
        if (row is null) return;
        if (IsInstalling) return;

        _cts = new CancellationTokenSource();
        IsInstalling = true;
        ProgressValue = 0;
        StatusText = $"安装 {row.Id}…";

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressText = p.Message;
            ProgressValue = p.TotalFiles > 0
                ? Math.Clamp(p.CompletedFiles * 100.0 / p.TotalFiles, 0, 100)
                : 0;
            StatusText = $"{row.Id} · {p.Message}";
        });

        try
        {
            await _context.Installer.InstallAsync(row.Id, progress, _cts.Token);

            _context.RefreshInstalled();
            RefreshInstalled();
            ApplyFilter();
            StatusText = $"{row.Id} 安装完成";
            _log.Info($"{row.Id} 安装完成");
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消安装";
        }
        catch (Exception ex)
        {
            StatusText = "安装失败：" + ex.Message;
            _log.Error($"安装 {row.Id} 失败：{ex}");
        }
        finally
        {
            IsInstalling = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private void CancelInstall() => _cts?.Cancel();

    [RelayCommand]
    private void OpenGameFolder()
    {
        try
        {
            Directory.CreateDirectory(_context.Paths.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
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

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
