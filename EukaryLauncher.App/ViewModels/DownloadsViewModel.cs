using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>尚未实现的 Tab 的统一占位内容。</summary>
public sealed class PlaceholderViewModel(string title, string text)
{
    public string Title { get; } = title;
    public string Text { get; } = text;
}

/// <summary>
/// 下载页容器：顶部 Top Tabs 切换，下面按当前 Tab 显示对应的子页面。
/// 原版游戏里还能再进一层加载器选择页，模组里能再进一层版本页——
/// 这两层都用一个可空的「子视图」表示，返回就置空。
/// </summary>
public sealed partial class DownloadsViewModel : ViewModelBase
{
    private readonly ToastService _toast = AppServices.Toasts;

    public VanillaTabViewModel Vanilla { get; } = new();
    public ModsTabViewModel Mods { get; } = new();
    public QueueTabViewModel Queue { get; } = new();

    private readonly PlaceholderViewModel[] _placeholders =
    {
        new("整合包", "整合包（Modpack）的导入与下载功能尚在研发，先在「原版游戏」里把游戏本体装好吧。"),
        new("数据包", "数据包（Data Pack）的搜索与安装功能尚在研发，敬请期待。"),
        new("资源包", "资源包（Resource Pack）的搜索与安装功能尚在研发，敬请期待。"),
        new("光影包", "光影包（Shader Pack）的搜索与安装功能尚在研发，敬请期待。"),
    };

    public DownloadsViewModel()
    {
        Vanilla.ContinueRequested += OnContinueRequested;
        Mods.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModsTabViewModel.Current))
                OnPropertyChanged(nameof(CurrentContent));
        };
    }

    public IReadOnlyList<string> Tabs { get; } =
        new[] { "原版游戏", "模组", "整合包", "数据包", "资源包", "光影包", "下载队列" };

    [ObservableProperty] private int selectedTabIndex;

    [ObservableProperty] private LoaderSelectViewModel? loaderSelect;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentContent));
        _ = CurrentContent switch
        {
            VanillaTabViewModel vm => vm.OnNavigatedAsync(),
            ModsTabViewModel vm => vm.OnNavigatedAsync(),
            _ => Task.CompletedTask,
        };
    }

    partial void OnLoaderSelectChanged(LoaderSelectViewModel? value) => OnPropertyChanged(nameof(CurrentContent));

    public object CurrentContent => SelectedTabIndex switch
    {
        0 => LoaderSelect ?? (object)Vanilla,
        1 => Mods.Current,
        6 => Queue,
        2 => _placeholders[0],
        3 => _placeholders[1],
        4 => _placeholders[2],
        5 => _placeholders[3],
        _ => _placeholders[0],
    };

    public override async Task OnNavigatedAsync()
    {
        _ = Vanilla.OnNavigatedAsync();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void SelectTab(int index)
    {
        if (index < 0 || index >= Tabs.Count) return;
        SelectedTabIndex = index;
    }

    private async void OnContinueRequested(AvailableVersionRow row)
    {
        var loader = new LoaderSelectViewModel();
        loader.BackRequested += () => LoaderSelect = null;
        LoaderSelect = loader;

        await loader.InitializeAsync(row.Id);
    }
}
