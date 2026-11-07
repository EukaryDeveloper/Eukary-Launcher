using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>
/// 主页：选版本 → 填用户名 → 点开始游戏。
/// M1 只做离线模式，微软登录留给 M2，但 LaunchOptions 的字段已经按正版预留好。
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly GameContext _context;
    private readonly LogService _log = LogService.Instance;

    private CancellationTokenSource? _cts;
    private Process? _gameProcess;

    /// <summary>本次启动是否真的把启动器窗口藏起来了（决定游戏退出时要不要恢复）。</summary>
    private bool _launcherHidden;

    public HomeViewModel(SettingsService settings)
    {
        _settings = settings;
        _context = AppServices.Context;

        _context.InstalledVersionsChanged += () =>
        {
            AppServices.Post(SyncInstalledVersions);
        };

        PlayerName = _settings.Current.PlayerName;
        MaxMemoryMb = _settings.Current.MaxMemoryMb;
        SelectedVersionId = _settings.Current.LastVersionId;
        SyncInstalledVersions();

        if (!InstalledVersions.Contains(SelectedVersionId))
            SelectedVersionId = InstalledVersions.FirstOrDefault() ?? "";
    }

    public ObservableCollection<string> InstalledVersions { get; } = new();

    public IReadOnlyList<int> MemoryOptions { get; } = new[] { 1024, 2048, 3072, 4096, 6144, 8192 };

    [ObservableProperty]
    private string playerName;

    [ObservableProperty]
    private int maxMemoryMb;

    [ObservableProperty]
    private string? selectedVersionId;

    /// <summary>开始按钮第二行的小字：本体版本号 + 加载器，如「1.20.4-Fabric 0.16.9」。</summary>
    [ObservableProperty]
    private string launchSubText = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isGameRunning;

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string progressText = "";

    [ObservableProperty]
    private string? currentStage;

    [ObservableProperty]
    private string javaStatusText = "检测中…";

    public bool CanLaunch => !IsBusy && !IsGameRunning && !string.IsNullOrWhiteSpace(SelectedVersionId);

    public override async Task OnNavigatedAsync()
    {
        await RefreshJavaStatusAsync();
    }

    /// <summary>
    /// 等游戏窗口真的出现，再按「启动器可见性」的档位处理启动器窗口。
    /// 游戏没起来（崩溃 / 窗口一直没出现）就什么都不做，避免把启动器白关掉。
    /// </summary>
    private async Task WaitForGameWindowThenApplyAsync(Process process, LauncherVisibility visibility)
    {
        try
        {
            var appeared = await GameLauncher.WaitForMainWindowAsync(process).ConfigureAwait(false);
            if (!appeared)
            {
                _log.Warn("游戏窗口一直没出现，启动器保持原样");
                return;
            }

            var title = "";
            try { title = process.MainWindowTitle; } catch { /* 拿不到标题不影响 */ }

            AppServices.Post(() =>
            {
                _log.Info($"游戏窗口已出现（{title}），按设置处理启动器窗口：{visibility}");
                if (visibility is LauncherVisibility.HideAndCloseOnExit
                    or LauncherVisibility.HideAndRestoreOnExit)
                    _launcherHidden = true;

                AppServices.ApplyLauncherVisibility?.Invoke(visibility);
            });
        }
        catch (Exception ex)
        {
            _log.Warn("等待游戏窗口时出错：" + ex.Message);
        }
    }

    private void SyncInstalledVersions()
    {
        var previous = SelectedVersionId;
        InstalledVersions.Clear();
        foreach (var id in _context.InstalledVersions)
            InstalledVersions.Add(id);

        if (previous is { Length: > 0 } && InstalledVersions.Contains(previous))
            SelectedVersionId = previous;
        else
            SelectedVersionId = InstalledVersions.FirstOrDefault() ?? "";

        // 列表刷新后若选中项没变（不触发 OnSelectedVersionIdChanged），也补一次副标题刷新
        RefreshLaunchSubText();
    }

    /// <summary>
    /// 扫描当前游戏文件夹里选中版本的 json，取本体版本号与加载器拼副标题。
    /// 例：1.20.4-Fabric 0.16.9；原版只显示版本号。
    /// </summary>
    private void RefreshLaunchSubText()
    {
        var id = SelectedVersionId;
        if (string.IsNullOrWhiteSpace(id))
        {
            LaunchSubText = "";
            return;
        }

        var root = _context.Paths.Root;
        Task.Run(() => FolderVersionScanner.Scan(root).FirstOrDefault(v => v.Id == id))
            .ContinueWith(t =>
            {
                AppServices.Post(() =>
                {
                    var info = t.IsFaulted ? null : t.Result;
                    var text = info is null
                        ? id
                        : info.LoaderText == "原版"
                            ? info.GameVersion
                            : $"{info.GameVersion}-{info.LoaderText}";

                    // 期间用户又切了版本，旧结果作废
                    if (SelectedVersionId == id)
                        LaunchSubText = text;
                });
            });
    }

    partial void OnPlayerNameChanged(string value)
    {
        _settings.Current.PlayerName = value;
        _settings.Save();
    }

    partial void OnMaxMemoryMbChanged(int value)
    {
        _settings.Current.MaxMemoryMb = value;
        _settings.Save();
    }

    partial void OnSelectedVersionIdChanged(string? value)
    {
        _settings.Current.LastVersionId = value ?? "";
        _settings.Save();
        OnPropertyChanged(nameof(CanLaunch));
        LaunchCommand.NotifyCanExecuteChanged();
        RefreshLaunchSubText();
    }

    partial void OnIsBusyChanged(bool value) => LaunchCommand.NotifyCanExecuteChanged();
    partial void OnIsGameRunningChanged(bool value) => LaunchCommand.NotifyCanExecuteChanged();

    private async Task RefreshJavaStatusAsync()
    {
        JavaStatusText = await Task.Run(() =>
        {
            var managed = _context.Java.ListManaged();
            var system = JavaManager.FindSystemJava();
            if (managed.Count > 0)
                return $"已托管 {string.Join(" / ", managed.Select(m => $"Java {m.MajorVersion}"))}" +
                       (system is null ? "" : $"　系统 {system.MajorVersion}");
            return system is null ? "未检测到 Java，启动时会自动下载" : $"使用系统 Java {system.MajorVersion}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedVersionId))
        {
            StatusText = "请先选择一个版本";
            return;
        }

        if (!IsValidPlayerName(PlayerName))
        {
            StatusText = "玩家名不合法：只能包含字母、数字和下划线，长度 3–16";
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsGameRunning = false;
        StatusText = "准备中…";
        ProgressValue = 0;

        try
        {
            // 1. 确保版本装好了
            var version = await EnsureVersionAsync(SelectedVersionId, _cts.Token);
            if (version is null)
            {
                StatusText = "版本安装失败，请到「下载」页重试";
                return;
            }

            // 2. 版本隔离：装的时候记了旗标；老版本没旗标，第一次启动按当前全局设置补记
            var isolated = VersionIsolationStore.Get(_context.Paths, version.Id);
            if (isolated is null)
            {
                isolated = IsolationPolicy.ShouldIsolate(_settings.Current.VersionIsolation, version);
                VersionIsolationStore.Set(_context.Paths, version.Id, isolated.Value);
            }
            var gameDirOverride = isolated.Value ? _context.Paths.VersionDir(version.Id) : null;
            if (gameDirOverride is not null)
                _log.Info($"版本 {version.Id} 已隔离，游戏目录：{gameDirOverride}");

            // 3. 把该游戏版本的模组搬到游戏真正会读的 mods 目录
            var gameVersion = LoaderDetector.DetectGameVersion(version.Id, version) ?? version.Id;
            var deployed = ModDeployer.Deploy(_context.Paths, gameVersion, AppServices.ModIndex, gameDirOverride);
            if (deployed > 0)
                _log.Info($"已把 {gameVersion} 的 {deployed} 个模组同步到 {(gameDirOverride ?? _context.Paths.Root)}\\mods");

            // 4. 确保有 Java
            var java = await EnsureJavaAsync(version, _cts.Token);
            if (java is null)
            {
                StatusText = "没有可用的 Java 运行时";
                return;
            }

            // 5. 拼参数并启动
            var options = OfflineAccount.CreateLaunchOptions(
                _context.Paths.Root,
                version.Id,
                PlayerName,
                java.ExecutablePath,
                MaxMemoryMb)
                with
                {
                    WindowWidth = _settings.Current.WindowWidth,
                    WindowHeight = _settings.Current.WindowHeight,
                    UseCustomResolution = _settings.Current.UseCustomResolution,
                    ExtraJvmArgs = _settings.Current.ExtraJvmArgs,
                    Visibility = _settings.Current.LauncherVisibility,
                    EffectiveGameDirectory = gameDirOverride,
                };

            Directory.CreateDirectory(gameDirOverride ?? _context.Paths.Root);

            var launcher = new GameLauncher();
            var visibility = LauncherVisibility.Unchanged; // 在 options 拼好后赋值，退出回调按本次启动的设置收尾
            launcher.OutputReceived += line => AppServices.Post(() => _log.GameOutput(line));
            launcher.Exited += code =>
            {
                AppServices.Post(() =>
                {
                    IsGameRunning = false;
                    IsBusy = false;
                    StatusText = code == 0 ? "游戏已退出" : $"游戏异常退出（代码 {code}）";
                    ProgressValue = 0;
                    ProgressText = "";
                    _log.Info($"游戏进程结束，退出码 {code}");

                    // 只有真的隐藏过启动器，退出时才需要恢复/关闭；
                    // 游戏没起来（窗口还没弹出就崩了）的情况下不能顺手把启动器关掉
                    if (visibility == LauncherVisibility.HideAndCloseOnExit && _launcherHidden)
                        AppServices.OnGameExited?.Invoke(true);
                    else if (visibility == LauncherVisibility.HideAndRestoreOnExit && _launcherHidden)
                        AppServices.OnGameExited?.Invoke(false);
                    _launcherHidden = false;
                });
            };

            var process = launcher.Start(version, _context.Paths, options);
            _gameProcess = process;

            // 启动动作与退出收尾都以本次启动时捕获的设置为准，中途改设置不影响这局
            visibility = options.Visibility;

            IsGameRunning = true;
            StatusText = $"游戏运行中 · {version.Id} · Java {java.MajorVersion}";
            ProgressValue = 100;
            ProgressText = "已启动";
            _log.Info($"启动 {version.Id}：{launcher.BuildCommandPreview(version, _context.Paths, options)}");

            AppServices.NavigateToConsole?.Invoke();

            var launchVisibility = visibility;
            if (launchVisibility != LauncherVisibility.Unchanged)
            {
                // “游戏启动后”以游戏窗口真的弹出来为准，不是点下开始按钮的那一刻
                _ = WaitForGameWindowThenApplyAsync(process, launchVisibility);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
            _log.Warn("启动流程被取消");
        }
        catch (Exception ex)
        {
            StatusText = "启动失败：" + ex.Message;
            _log.Error("启动失败：" + ex);
        }
        finally
        {
            if (!IsGameRunning) IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "正在取消…";
    }

    /// <summary>版本设置：每个版本独立的内存 / 窗口 / JVM 参数，覆盖「全局设置」。页面还没做，先给个入口。</summary>
    [RelayCommand]
    private void OpenVersionSettings() =>
        AppServices.Toasts.Info("版本设置开发中：将支持按版本覆盖全局设置（内存、窗口、JVM 参数）");

    private async Task<VersionJson?> EnsureVersionAsync(string versionId, CancellationToken ct)
    {
        if (_context.IsInstalled(versionId))
        {
            var local = _context.LoadInstalledVersion(versionId);
            if (local is not null)
            {
                StatusText = $"{versionId} 已安装，检查完整性…";
                ProgressValue = 100;
                ProgressText = "已就绪";
                return local;
            }
        }

        StatusText = $"正在安装 {versionId}…";
        var manifest = await _context.Installer.FetchManifestAsync(false, ct);
        var entry = manifest.Versions.FirstOrDefault(v => v.Id == versionId);
        if (entry is null)
        {
            _log.Error($"版本清单里找不到 {versionId}");
            return null;
        }

        var progress = new Progress<InstallProgress>(p =>
        {
            CurrentStage = p.Stage.ToString();
            ProgressText = p.Message;
            ProgressValue = p.TotalFiles > 0
                ? Math.Clamp(p.CompletedFiles * 100.0 / p.TotalFiles, 0, 100)
                : 0;
            StatusText = p.Stage switch
            {
                InstallStage.Done => $"{versionId} 安装完成",
                _ => $"安装中：{p.Message}",
            };
        });

        var installed = await _context.Installer.InstallAsync(entry, progress, ct);
        _context.RefreshInstalled();
        _log.Info($"{versionId} 安装完成");
        return installed;
    }

    private async Task<JavaRuntimeInfo?> EnsureJavaAsync(VersionJson version, CancellationToken ct)
    {
        var required = version.JavaVersion?.MajorVersion ?? 17;
        var target = JavaManager.NormalizeMajor(required);

        var progress = new Progress<FileProgress>(p =>
        {
            ProgressText = $"下载 Java {target}：{p.FileName}";
            ProgressValue = p.TotalBytes is > 0
                ? Math.Clamp(p.ReceivedBytes * 100.0 / p.TotalBytes.Value, 0, 100)
                : 0;
        });

        if (!_settings.Current.AutoManageJava)
        {
            if (_settings.Current.CustomJavaPath is { Length: > 0 } custom && File.Exists(custom))
                return new JavaRuntimeInfo
                {
                    MajorVersion = JavaManager.QueryJavaMajor(custom),
                    DisplayName = "自定义 Java",
                    ExecutablePath = custom,
                };

            return JavaManager.FindSystemJava();
        }

        StatusText = $"检查 Java {target}…";
        return await _context.Java.EnsureJavaAsync(required, progress, ct);
    }

    public static bool IsValidPlayerName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length is >= 3 and <= 16
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
