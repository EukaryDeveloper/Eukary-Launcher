using System.Windows.Threading;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 极简的服务定位器。没有引入 DI 容器——目前只有几个单例，
/// 等 M8 做插件系统时再换成 Microsoft.Extensions.DependencyInjection 也不迟。
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; private set; } = null!;
    public static GameContext Context { get; private set; } = null!;
    public static ToastService Toasts { get; } = new();
    public static DownloadManager Queue { get; private set; } = null!;
    public static ModIndexService ModIndex { get; private set; } = null!;

    /// <summary>把日志/状态更新切回 UI 线程。</summary>
    public static Dispatcher? UiDispatcher { get; set; }

    /// <summary>
    /// 异步切回 UI 线程。游戏进程的输出回调在后台线程上，
    /// 用 BeginInvoke 而不是 Invoke，避免 UI 正忙时后台线程被卡住。
    /// </summary>
    public static void Post(Action action)
    {
        if (UiDispatcher is null)
        {
            action();
            return;
        }
        UiDispatcher.BeginInvoke(action);
    }

    /// <summary>启动游戏后自动跳到日志页。</summary>
    public static Action? NavigateToConsole { get; set; }

    /// <summary>游戏启动后按设置调整主窗口（隐藏 / 最小化 / 关闭），由 MainWindow 实现。</summary>
    public static Action<LauncherVisibility>? ApplyLauncherVisibility { get; set; }

    /// <summary>游戏退出后的收尾：true 关闭启动器，false 重新显示主窗口。仅窗口被隐藏时需要。</summary>
    public static Action<bool>? OnGameExited { get; set; }

    public static void Initialize()
    {
        Settings = new SettingsService();
        Context = new GameContext(Settings);
        ModIndex = new ModIndexService();
        Queue = new DownloadManager(Toasts);
    }
}
