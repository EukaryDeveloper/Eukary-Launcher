using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
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

    [ObservableProperty] private string gameDirectory;
    [ObservableProperty] private string mirrorId;
    [ObservableProperty] private int maxMemoryMb;
    [ObservableProperty] private int downloadThreads;
    [ObservableProperty] private bool keepLauncherOpen;
    [ObservableProperty] private bool useCustomResolution;
    [ObservableProperty] private int windowWidth;
    [ObservableProperty] private int windowHeight;
    [ObservableProperty] private string? extraJvmArgs;
    [ObservableProperty] private bool autoManageJava;
    [ObservableProperty] private string? customJavaPath;
    [ObservableProperty] private bool isDownloadingJava;
    [ObservableProperty] private double javaProgressValue;
    [ObservableProperty] private string javaStatusText = "";

    public override async Task OnNavigatedAsync()
    {
        GameDirectory = _settings.Current.GameDirectory;
        MirrorId = _settings.Current.MirrorId;
        MaxMemoryMb = _settings.Current.MaxMemoryMb;
        DownloadThreads = _settings.Current.DownloadThreads;
        KeepLauncherOpen = _settings.Current.KeepLauncherOpen;
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
    partial void OnKeepLauncherOpenChanged(bool value) { _settings.Current.KeepLauncherOpen = value; _settings.Save(); }
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
}

public sealed record MirrorOption(string Id, string DisplayName);
public sealed record JavaOption(string Key, string DisplayName, string Path);
