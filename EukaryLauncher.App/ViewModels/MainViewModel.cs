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
        CurrentViewModel = Home;
        _ = Home.OnNavigatedAsync();
    }

    public HomeViewModel Home { get; }
    public DownloadsViewModel Download { get; }
    public ConsoleViewModel Console { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }

    [ObservableProperty]
    private NavPage currentPage = NavPage.Home;

    [ObservableProperty]
    private ViewModelBase? currentViewModel;

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
        _ = vm.OnNavigatedAsync();
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
