using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly GameContext _context;
    private readonly LogService _log = LogService.Instance;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _context = AppServices.Context;
    }

    public ObservableCollection<MirrorOption> Mirrors { get; } = new(
        MirrorFactory.All.Select(m => new MirrorOption(m.Id, m.DisplayName)));

    public ObservableCollection<int> MemoryOptions { get; } = new() { 1024, 2048, 3072, 4096, 6144, 8192 };

    public ObservableCollection<JavaOption> JavaOptions { get; } = new();

    /// <summary>「启动器可见性」下拉框的选项，顺序即界面显示顺序。</summary>
    public ObservableCollection<VisibilityOption> VisibilityOptions { get; } = new(
    [
        new VisibilityOption(LauncherVisibility.CloseImmediately, "游戏启动后立即关闭"),
        new VisibilityOption(LauncherVisibility.HideAndCloseOnExit, "游戏启动后隐藏，游戏退出后自动关闭"),
        new VisibilityOption(LauncherVisibility.HideAndRestoreOnExit, "游戏启动后隐藏，游戏退出后重新打开"),
        new VisibilityOption(LauncherVisibility.Minimize, "游戏启动后最小化"),
        new VisibilityOption(LauncherVisibility.Unchanged, "游戏启动后仍保持不变"),
    ]);

    /// <summary>「版本隔离设定」下拉框的选项（PCL2 同款五档），默认隔离所有版本。</summary>
    public ObservableCollection<IsolationOption> IsolationOptions { get; } = new(
    [
        new IsolationOption(VersionIsolation.Off, "关闭"),
        new IsolationOption(VersionIsolation.ModdedOnly, "隔离可安装 Mod 的版本"),
        new IsolationOption(VersionIsolation.SnapshotsOnly, "隔离非正式版"),
        new IsolationOption(VersionIsolation.ModdedAndSnapshots, "隔离可安装 Mod 的版本与非正式版"),
        new IsolationOption(VersionIsolation.All, "隔离所有版本"),
    ]);

    [ObservableProperty] private string gameDirectory = string.Empty;
    [ObservableProperty] private string mirrorId = string.Empty;
    [ObservableProperty] private int maxMemoryMb;
    [ObservableProperty] private int downloadThreads;
    [ObservableProperty] private LauncherVisibility launcherVisibility = LauncherVisibility.Unchanged;
    [ObservableProperty] private VersionIsolation versionIsolation = VersionIsolation.All;
    [ObservableProperty] private bool useCustomResolution;
    [ObservableProperty] private int windowWidth;
    [ObservableProperty] private int windowHeight;
    [ObservableProperty] private string? extraJvmArgs;
    [ObservableProperty] private bool autoManageJava;
    [ObservableProperty] private string? customJavaPath;
    [ObservableProperty] private bool isDownloadingJava;
    [ObservableProperty] private double javaProgressValue;
    [ObservableProperty] private string javaStatusText = "";

    // 「额外 JVM 参数」编辑弹窗：打开时复制当前值进编辑副本，确认才写回
    [ObservableProperty] private bool isJvmDialogOpen;
    [ObservableProperty] private string jvmDialogText = "";

    [RelayCommand]
    private void OpenJvmDialog()
    {
        JvmDialogText = ExtraJvmArgs ?? "";
        IsJvmDialogOpen = true;
    }

    [RelayCommand]
    private void ConfirmJvmDialog()
    {
        ExtraJvmArgs = JvmDialogText;
        IsJvmDialogOpen = false;
    }

    [RelayCommand]
    private void CancelJvmDialog() => IsJvmDialogOpen = false;

    public override async Task OnNavigatedAsync()
    {
        GameDirectory = _settings.Current.GameDirectory;
        MirrorId = _settings.Current.MirrorId;
        MaxMemoryMb = _settings.Current.MaxMemoryMb;
        DownloadThreads = _settings.Current.DownloadThreads;
        LauncherVisibility = _settings.Current.LauncherVisibility;
        VersionIsolation = _settings.Current.VersionIsolation;
        UseCustomResolution = _settings.Current.UseCustomResolution;
        WindowWidth = _settings.Current.WindowWidth;
        WindowHeight = _settings.Current.WindowHeight;
        ExtraJvmArgs = _settings.Current.ExtraJvmArgs ?? "";
        AutoManageJava = _settings.Current.AutoManageJava;
        CustomJavaPath = _settings.Current.CustomJavaPath;

        await RefreshJavaOptionsAsync();
    }

    private async Task RefreshJavaOptionsAsync()
    {
        var options = await Task.Run(() =>
        {
            var list = new List<JavaOption>();
            foreach (var m in _context.Java.ListManaged())
                list.Add(new JavaOption("managed:" + m.ExecutablePath, m.ToString(), m.ExecutablePath));

            var system = JavaManager.FindSystemJava();
            if (system is not null)
                list.Add(new JavaOption("system:" + system.ExecutablePath, system.ToString(), system.ExecutablePath));

            if (CustomJavaPath is { Length: > 0 } custom && File.Exists(custom))
                list.Add(new JavaOption("custom:" + custom, "自定义：" + custom, custom));

            return list;
        });

        JavaOptions.Clear();
        foreach (var o in options) JavaOptions.Add(o);

        JavaStatusText = options.Count == 0
            ? "没有检测到任何 Java，点下面的按钮下载"
            : $"检测到 {options.Count} 个 Java 运行时";
    }

    partial void OnGameDirectoryChanged(string value)
    {
        _settings.Current.GameDirectory = value;
        _settings.Save();
        _context.Rebuild();
    }

    partial void OnMirrorIdChanged(string value)
    {
        _settings.Current.MirrorId = value;
        _settings.Save();
        _context.Rebuild();
    }

    partial void OnMaxMemoryMbChanged(int value) { _settings.Current.MaxMemoryMb = value; _settings.Save(); }
    partial void OnDownloadThreadsChanged(int value)
    {
        _settings.Current.DownloadThreads = Math.Clamp(value, 1, 32);
        _settings.Save();
    }
    partial void OnLauncherVisibilityChanged(LauncherVisibility value)
    {
        _settings.Current.LauncherVisibility = value;
        _settings.Save();
    }

    partial void OnVersionIsolationChanged(VersionIsolation value)
    {
        // 进页面时的初始化赋的值和已保存值一致；只有用户真的换选项才弹窗
        if (value == _settings.Current.VersionIsolation) return;

        _settings.Current.VersionIsolation = value;
        _settings.Save();

        AppServices.ShowModalConfirm?.Invoke(new ModalConfirmRequest(
            "版本隔离选项已修改",
            "本设置仅会对之后新安装的版本生效。\n如果要修改已安装的版本的隔离方式，请在它的版本独立设置中调整。",
            "确定",
            Destructive: false,
            CountdownSeconds: 0,
            OnConfirm: () => { },
            HideCancel: true));
    }
    partial void OnUseCustomResolutionChanged(bool value) { _settings.Current.UseCustomResolution = value; _settings.Save(); }
    partial void OnWindowWidthChanged(int value) { _settings.Current.WindowWidth = value; _settings.Save(); }
    partial void OnWindowHeightChanged(int value) { _settings.Current.WindowHeight = value; _settings.Save(); }
    partial void OnExtraJvmArgsChanged(string? value) { _settings.Current.ExtraJvmArgs = value; _settings.Save(); }
    partial void OnAutoManageJavaChanged(bool value)
    {
        _settings.Current.AutoManageJava = value;
        _settings.Save();
    }
    partial void OnCustomJavaPathChanged(string? value)
    {
        _settings.Current.CustomJavaPath = value;
        _settings.Save();
    }

    [RelayCommand]
    private void BrowseGameDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 .minecraft 游戏目录",
            DefaultDirectory = Directory.Exists(GameDirectory) ? GameDirectory : GamePaths.DefaultRoot,
        };

        if (dialog.ShowDialog() == true)
            GameDirectory = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseJava()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件|java.exe;javaw.exe|所有文件|*.*",
        };

        if (dialog.ShowDialog() == true)
            CustomJavaPath = dialog.FileName;
    }

    [RelayCommand]
    private async Task DownloadJavaAsync()
    {
        if (IsDownloadingJava) return;

        IsDownloadingJava = true;
        JavaProgressValue = 0;
        JavaStatusText = "准备下载 Java 21…";

        var progress = new Progress<FileProgress>(p =>
        {
            JavaStatusText = $"下载中：{p.FileName}";
            JavaProgressValue = p.TotalBytes is > 0
                ? Math.Clamp(p.ReceivedBytes * 100.0 / p.TotalBytes.Value, 0, 100)
                : 0;
        });

        try
        {
            var info = await _context.Java.DownloadJreAsync(21, progress);
            JavaStatusText = $"已安装 {info}";
            JavaProgressValue = 100;
            _log.Info($"Java 下载完成：{info.ExecutablePath}");
            await RefreshJavaOptionsAsync();
        }
        catch (Exception ex)
        {
            JavaStatusText = "下载失败：" + ex.Message;
            _log.Error("Java 下载失败：" + ex);
        }
        finally
        {
            IsDownloadingJava = false;
        }
    }

    [RelayCommand]
    private void OpenRuntimeFolder()
    {
        try
        {
            Directory.CreateDirectory(GamePaths.RuntimeRoot);
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { GamePaths.RuntimeRoot },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error("打开 runtime 目录失败：" + ex.Message);
        }
    }
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(GamePaths.AppDataRoot);
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { GamePaths.AppDataRoot },
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error("打开数据目录失败：" + ex.Message);
        }
    }
}

public sealed record MirrorOption(string Id, string DisplayName)
{
    // ComboBox 选中框在部分模板路径下会退回 ToString()，重写保证显示名称
    public override string ToString() => DisplayName;
}
public sealed record JavaOption(string Key, string DisplayName, string Path);

public sealed record VisibilityOption(LauncherVisibility Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record IsolationOption(VersionIsolation Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
