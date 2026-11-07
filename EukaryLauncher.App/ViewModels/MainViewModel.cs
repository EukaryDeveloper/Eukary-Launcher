using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.ViewModels;

public enum NavPage
{
    Home,
    Download,
    Console,
    Settings,
    About,
}

/// <summary>主窗口 VM：负责左侧导航与页面切换。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public MainViewModel(SettingsService settings)
    {
        _settings = settings;
        Home = new HomeViewModel(_settings);
        Download = new DownloadsViewModel();
        Console = new ConsoleViewModel();
        Settings = new SettingsViewModel(_settings);
        About = new AboutViewModel();
        VersionSelect = new VersionSelectViewModel(_settings, Home);
        CurrentViewModel = Home;
        _ = Home.OnNavigatedAsync();
    }

    public HomeViewModel Home { get; }
    public DownloadsViewModel Download { get; }
    public ConsoleViewModel Console { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }
    public VersionSelectViewModel VersionSelect { get; }

    [ObservableProperty]
    private NavPage currentPage = NavPage.Home;

    [ObservableProperty]
    private ViewModelBase? currentViewModel;

    /// <summary>
    /// 切页后要做的数据加载，等主窗口把转场动画放完再执行。
    /// 不这样做的话，OnNavigatedAsync 里同步的那段（读版本目录、重建列表、探测 Java）
    /// 会跟动画抢同一个 UI 线程，正好把起步那几帧挤掉 —— 看上去就是一跳页先顿一下。
    /// </summary>
    public Action? PendingActivation { get; private set; }

    /// <summary>取走待执行的加载动作（主窗口在动画落定后调用）。</summary>
    public Action? TakePendingActivation()
    {
        var pending = PendingActivation;
        PendingActivation = null;
        return pending;
    }

    partial void OnCurrentPageChanged(NavPage value)
    {
        ViewModelBase vm = value switch
        {
            NavPage.Home => Home,
            NavPage.Download => Download,
            NavPage.Console => Console,
            NavPage.Settings => Settings,
            _ => About,
        };
        CurrentViewModel = vm;
        PendingActivation = () => _ = vm.OnNavigatedAsync();
    }

    public IReadOnlyList<NavItem> NavItems { get; } = new[]
    {
        new NavItem(NavPage.Home, "主页", "\uE10F"),
        new NavItem(NavPage.Download, "下载", "\uE896"),
        new NavItem(NavPage.Console, "日志", "\uE756"),
        new NavItem(NavPage.Settings, "设置", "\uE713"),
        new NavItem(NavPage.About, "关于", "\uE946"),
    };

    [RelayCommand]
    private void Navigate(NavPage page) => CurrentPage = page;
}

public sealed record NavItem(NavPage Page, string Title, string Glyph);
