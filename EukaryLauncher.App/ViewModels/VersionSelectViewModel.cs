using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>版本列表里的一条：大字本体版本、小字加载器、右侧切换按钮。</summary>
public sealed partial class FolderVersionRow : ObservableObject
{
    public FolderVersionInfo Info { get; }

    public FolderVersionRow(FolderVersionInfo info, string currentVersionId)
    {
        Info = info;
        UpdateCurrent(currentVersionId);
    }

    public string Id => Info.Id;
    public string GameVersion => Info.GameVersion;
    public string LoaderText => Info.HasJar ? Info.LoaderText : Info.LoaderText + "（文件不完整）";

    /// <summary>当前启动版本正指向它。</summary>
    [ObservableProperty]
    private bool isCurrent;

    public void UpdateCurrent(string currentVersionId) =>
        IsCurrent = string.Equals(Info.Id, currentVersionId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 版本选择页：左侧文件夹列表 + 右侧信息流（文件夹设置 / 版本列表 / 移除文件夹）。
/// 页面以全窗覆盖层形式打开，进出动画由 MainWindow 编排。
/// </summary>
public sealed partial class VersionSelectViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly HomeViewModel _home;

    public VersionSelectViewModel(SettingsService settings, HomeViewModel home)
    {
        _settings = settings;
        _home = home;
        RefreshFolders();
    }

    public ObservableCollection<GameFolderItem> Folders { get; } = new();

    [ObservableProperty]
    private GameFolderItem? selectedFolder;

    /// <summary>文件夹名称输入框（个性化显示名称，不是磁盘名）。</summary>
    [ObservableProperty]
    private string editableName = "";

    /// <summary>文件夹实际路径（只读展示）。</summary>
    [ObservableProperty]
    private string folderPath = "";

    public ObservableCollection<FolderVersionRow> Versions { get; } = new();

    /// <summary>非默认文件夹才显示「移除文件夹」栏目。</summary>
    [ObservableProperty]
    private bool canRemoveFolder;

    /// <summary>返回（主页与新页面按原路线滑回）。</summary>
    public event Action? CloseRequested;

    private void Close() => CloseRequested?.Invoke();

    partial void OnSelectedFolderChanged(GameFolderItem? value)
    {
        EditableName = value?.DisplayName ?? "";
        FolderPath = value?.Path ?? "";
        CanRemoveFolder = value is { IsDefault: false };
        LoadVersions();
    }

    /// <summary>重建文件夹列表；保持选中项（按 Id 对齐）。</summary>
    public void RefreshFolders()
    {
        var keepId = SelectedFolder?.Id ?? "default";
        Folders.Clear();
        foreach (var f in GameFolderStore.GetFolders(_settings))
            Folders.Add(f);

        SelectedFolder = Folders.FirstOrDefault(f => f.Id == keepId) ?? Folders[0];
    }

    private void LoadVersions()
    {
        Versions.Clear();
        var folder = SelectedFolder;
        if (folder is null) return;

        var path = folder.Path;
        Task.Run(() => FolderVersionScanner.Scan(path)).ContinueWith(t =>
        {
            AppServices.Post(() =>
            {
                // 扫描期间用户可能已切走文件夹
                if (SelectedFolder is null || SelectedFolder.Path != path) return;
                if (t.IsFaulted)
                {
                    AppServices.Toasts.Error("扫描版本列表失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误"));
                    return;
                }
                foreach (var info in t.Result)
                    Versions.Add(new FolderVersionRow(info, _home.SelectedVersionId ?? ""));
            });
        });
    }

    private void RefreshCurrentFlags()
    {
        var current = _home.SelectedVersionId ?? "";
        foreach (var row in Versions)
            row.UpdateCurrent(current);
    }

    // ============================ 命令 ============================

    [RelayCommand]
    private void Back() => Close();

    [RelayCommand]
    private void ApplyName()
    {
        if (SelectedFolder is null) return;
        var name = EditableName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            AppServices.Toasts.Warning("文件夹名称不能为空");
            return;
        }
        try
        {
            GameFolderStore.Rename(_settings, SelectedFolder, name);
            AppServices.Toasts.Success($"已应用文件夹名称：{name}");
        }
        catch (Exception ex)
        {
            AppServices.Toasts.Error("应用文件夹名称失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedFolder is null) return;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe");
            psi.ArgumentList.Add(SelectedFolder.Path);
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppServices.Toasts.Error("打开文件夹失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private void SwitchVersion(FolderVersionRow? row)
    {
        if (row is null) return;
        _home.SelectedVersionId = row.Id;
        RefreshCurrentFlags();
        AppServices.Toasts.Success($"启动版本已切换为 {row.Id}");
        Close();
    }

    [RelayCommand]
    private void ImportOrExport() =>
        AppServices.Toasts.Info("「导入或导出」正在开发中");

    [RelayCommand]
    private void RemoveFolder()
    {
        var folder = SelectedFolder;
        if (folder is null || folder.IsDefault) return;

        AppServices.ShowModalConfirm?.Invoke(new ModalConfirmRequest(
            "移除文件夹",
            $"将「{folder.DisplayName}」从列表中移除？\n\n文件夹内的所有文件都会保留在磁盘上，之后可以重新导入。",
            "移除", Destructive: false, CountdownSeconds: 0,
            OnConfirm: () =>
            {
                try
                {
                    GameFolderStore.Remove(_settings, folder.Id);
                    AppServices.Toasts.Success($"已移除「{folder.DisplayName}」");
                }
                catch (Exception ex)
                {
                    AppServices.Toasts.Error("移除文件夹失败：" + ex.Message);
                }
                SwitchToDefault();
            }));
    }

    [RelayCommand]
    private void DeleteFolder()
    {
        var folder = SelectedFolder;
        if (folder is null || folder.IsDefault) return;

        // 第一次确认
        AppServices.ShowModalConfirm?.Invoke(new ModalConfirmRequest(
            "删除文件夹",
            $"将「{folder.DisplayName}」从磁盘上永久删除？\n\n文件夹内的版本、存档、资源包都会被清除，此操作无法撤销。",
            "删除", Destructive: true, CountdownSeconds: 0,
            OnConfirm: () =>
            {
                // 第二次确认：5 秒倒计时
                AppServices.ShowModalConfirm?.Invoke(new ModalConfirmRequest(
                    "再次确认删除",
                    $"真的要永久删除「{folder.DisplayName}」吗？\n\n倒计时结束后才能点击删除按钮。",
                    "永久删除", Destructive: true, CountdownSeconds: 5,
                    OnConfirm: DoDeleteFolder));
            }));
    }

    private void DoDeleteFolder()
    {
        var folder = SelectedFolder;
        if (folder is null || folder.IsDefault) return;
        var path = folder.Path;
        var name = folder.DisplayName;

        Task.Run(() =>
        {
            try
            {
                Directory.Delete(path, recursive: true);
                AppServices.Post(() =>
                {
                    AppServices.Toasts.Success($"已删除文件夹「{name}」");
                    GameFolderStore.Remove(_settings, folder.Id);
                    SwitchToDefault();
                });
            }
            catch (Exception ex)
            {
                AppServices.Post(() =>
                    AppServices.Toasts.Error("删除文件夹失败：" + ex.Message));
            }
        });
    }

    private void SwitchToDefault()
    {
        RefreshFolders(); // OnSelectedFolderChanged 会重载版本列表
    }
}
