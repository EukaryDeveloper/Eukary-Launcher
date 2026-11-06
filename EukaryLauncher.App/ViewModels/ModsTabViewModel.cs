using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>搜索结果卡片。图标是异步补上的，先显示占位。</summary>
public sealed partial class ModCard : ObservableObject
{
    public ModCard(ModrinthProject project)
    {
        Project = project;
        var (loaders, tags) = ModrinthCategories.Split(project.Categories);
        Loaders = loaders;
        Tags = tags;
    }

    public ModrinthProject Project { get; }

    public string ProjectId => Project.ProjectId;
    public string Title => Project.Title;
    public string Description => Project.Description;
    public string IconUrl => Project.IconUrl ?? "";

    public IReadOnlyList<string> Loaders { get; }
    public IReadOnlyList<string> Tags { get; }

    public string LoadersText => Loaders.Count == 0 ? "未标注" : string.Join(" / ", Loaders);
    public string DownloadsText => $"下载 {Humanize.Count(Project.Downloads)}";
    public string UpdatedText => $"更新于 {Humanize.Date(Project.DateModified)}";

    [ObservableProperty] private BitmapImage? icon;
    [ObservableProperty] private bool isInstalled;
    [ObservableProperty] private string installedText = "";

    /// <summary>
    /// 标记已安装。一个模组可能装在好几个游戏版本上，
    /// 所以是累加而不是覆盖——列表页能直接看出它都装在哪几个版本里。
    /// </summary>
    public void MarkInstalled(string? gameVersion, string versionNumber)
    {
        IsInstalled = true;
        var label = string.IsNullOrEmpty(gameVersion) ? versionNumber : $"{gameVersion} · {versionNumber}";
        if (InstalledText.Contains(label, StringComparison.Ordinal)) return;

        InstalledText = InstalledText.Length == 0
            ? $"已安装 {label}"
            : $"{InstalledText}、{label}";
    }

    public void ClearInstalled()
    {
        IsInstalled = false;
        InstalledText = "";
    }
}

/// <summary>
/// 下载页 · 模组：Modrinth 搜索 + 筛选。点卡片进版本页。
/// 整合包 / 数据包 / 资源包 / 光影包共用同一套搜索（只是 project_type 不同），
/// 但那些 Tab 目前还是占位，等各自的安装逻辑落地再放开。
/// </summary>
public sealed partial class ModsTabViewModel : ViewModelBase
{
    private static readonly string[] LoaderOptions = { "fabric", "forge", "neoforge", "quilt" };

    private readonly GameContext _context = AppServices.Context;
    private readonly ModrinthService _modrinth;
    private readonly LogService _log = LogService.Instance;
    private readonly ToastService _toast = AppServices.Toasts;

    private int _offset;
    private bool _hasMore;
    private CancellationTokenSource? _cts;

    public ModsTabViewModel()
    {
        _modrinth = new ModrinthService(_context.Http);
    }

    public ObservableCollection<ModCard> Results { get; } = new();

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string? selectedLoader;
    [ObservableProperty] private string selectedSource = "Modrinth";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasSearched;
    [ObservableProperty] private string statusText = "输入名称或关键词开始搜索";

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanLoadMore));

    public IReadOnlyList<string> GameVersions { get; private set; } = new[] { "全部" };
    public IReadOnlyList<string> LoaderList { get; } = new[] { "全部", "Fabric", "Forge", "NeoForge", "Quilt" };
    public IReadOnlyList<string> Sources { get; } = new[] { "Modrinth" };

    /// <summary>「来源」右侧的通道单选：正式版 / 快照版。与详情页的版本筛选条共用 ModSearchOptions。</summary>
    [ObservableProperty] private bool isReleaseChannel = true;
    [ObservableProperty] private bool isSnapshotChannel;

    private bool _suppressFilterSearch;

    partial void OnIsReleaseChannelChanged(bool value)
    {
        if (value) SwitchChannel(ModReleaseChannel.Release);
    }

    partial void OnIsSnapshotChannelChanged(bool value)
    {
        if (value) SwitchChannel(ModReleaseChannel.Snapshot);
    }

    private void SwitchChannel(ModReleaseChannel channel)
    {
        if (ModSearchOptions.Channel == channel && GameVersions.Count > 1) return;
        ModSearchOptions.Channel = channel;
        _ = OnChannelChangedAsync();
    }

    private async Task OnChannelChangedAsync()
    {
        try
        {
            await McVersionCatalog.EnsureLoadedAsync(_context);
        }
        catch (Exception ex)
        {
            _log.Error("版本清单拉取失败（模组页）：" + ex.Message);
        }

        GameVersions = BuildVersionList();
        OnPropertyChanged(nameof(GameVersions));

        // 原来选中的版本在新通道里未必存在，重置为「全部」；
        // 程序化赋值不该触发防抖搜索，下面统一刷一次
        _suppressFilterSearch = true;
        VersionFilter = "全部";
        _suppressFilterSearch = false;

        if (HasSearched) await DoSearchAsync(reset: true);
    }

    private IReadOnlyList<string> BuildVersionList()
    {
        var versions = ModSearchOptions.Channel == ModReleaseChannel.Snapshot
            ? McVersionCatalog.Snapshots
            : McVersionCatalog.Releases;
        return new[] { "全部" }.Concat(versions).ToList();
    }

    /// <summary>版本筛选：下拉可选、也可以直接手输版本号，「全部」或空 = 不过滤。</summary>
    [ObservableProperty] private string versionFilter = "全部";

    private CancellationTokenSource? _versionDebounceCts;

    /// <summary>手输版本号时防抖 500ms，停顿了才重新搜索。</summary>
    partial void OnVersionFilterChanged(string value)
    {
        if (!HasSearched || _suppressFilterSearch) return;

        _versionDebounceCts?.Cancel();
        _versionDebounceCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(_versionDebounceCts.Token);
    }

    private async Task DebouncedSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await DoSearchAsync(reset: true);
    }

    [ObservableProperty] private int selectedLoaderIndex;
    partial void OnSelectedLoaderIndexChanged(int value) => _ = HasSearched ? DoSearchAsync(reset: true) : Task.CompletedTask;

    [ObservableProperty]
    private ModDetailViewModel? detail;

    partial void OnDetailChanged(ModDetailViewModel? value) => OnPropertyChanged(nameof(Current));

    /// <summary>给外层容器用：详情页打开时显示详情页，否则显示列表。</summary>
    public object Current => Detail ?? (object)this;

    private string CurrentLoaderSlug =>
        SelectedLoaderIndex > 0 && SelectedLoaderIndex <= LoaderOptions.Length
            ? LoaderOptions[SelectedLoaderIndex - 1]
            : "";

    private string CurrentGameVersion
    {
        get
        {
            var text = (VersionFilter ?? "").Trim();
            return text.Length == 0 || string.Equals(text, "全部", StringComparison.Ordinal) ? "" : text;
        }
    }

    public override async Task OnNavigatedAsync()
    {
        if (GameVersions.Count <= 1) await LoadGameVersionsAsync();

        // 启动后第一次打开模组页就先搜一轮，别让用户面对一块空白
        if (!HasSearched) await DoSearchAsync(reset: true);
    }

    private async Task LoadGameVersionsAsync()
    {
        try
        {
            await McVersionCatalog.EnsureLoadedAsync(_context);
            GameVersions = BuildVersionList();
            OnPropertyChanged(nameof(GameVersions));
        }
        catch (Exception ex)
        {
            _log.Error("版本清单拉取失败（模组页）：" + ex.Message);
        }
    }

    [RelayCommand]
    private Task SearchAsync() => DoSearchAsync(reset: true);

    [RelayCommand]
    private Task LoadMoreAsync() => DoSearchAsync(reset: false);

    private async Task DoSearchAsync(bool reset)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        if (reset) _offset = 0;

        IsLoading = true;
        HasSearched = true;
        StatusText = "搜索中…";

        try
        {
            var response = await _modrinth.SearchAsync(new ModrinthSearchRequest(
                Query: SearchText,
                ProjectType: "mod",
                Loader: CurrentLoaderSlug,
                GameVersion: CurrentGameVersion,
                Limit: 20,
                Offset: _offset), ct);

            if (reset) Results.Clear();

            foreach (var project in response.Hits)
                Results.Add(new ModCard(project));

            _offset = response.Offset + response.Hits.Count;
            _hasMore = _offset < response.TotalHits;
            OnPropertyChanged(nameof(CanLoadMore));

            StatusText = response.TotalHits == 0
                ? "没有找到匹配的模组"
                : $"共 {response.TotalHits} 个结果，已显示 {Results.Count} 个";

            SyncInstalledMarks();
            _ = LoadIconsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // 用户改了筛选条件，忽略这一轮
        }
        catch (Exception ex)
        {
            StatusText = "搜索失败：" + ex.Message;
            _toast.Error("模组搜索失败：" + ex.Message);
            _log.Error("模组搜索失败：" + ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public bool CanLoadMore => _hasMore && !IsLoading;

    [RelayCommand]
    private void Open(ModCard? card)
    {
        if (card is null) return;

        var detail = new ModDetailViewModel(card.Project);
        detail.BackRequested += () => Detail = null;
        Detail = detail;
        _ = detail.InitializeAsync();
    }

    /// <summary>
    /// 刷新列表里的「已安装」标记。
    /// 筛了某个游戏版本就只认它；没筛就把所有游戏版本上的记录都列出来。
    /// </summary>
    public void SyncInstalledMarks()
    {
        var filter = CurrentGameVersion;

        foreach (var card in Results)
        {
            card.ClearInstalled();

            foreach (var (gameVersion, entries) in AppServices.ModIndex.All())
            {
                if (filter.Length > 0 && !string.Equals(gameVersion, filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entries.TryGetValue(card.ProjectId, out var entry))
                    card.MarkInstalled(filter.Length > 0 ? null : gameVersion, entry.VersionNumber);
            }
        }
    }

    private async Task LoadIconsAsync(CancellationToken ct)
    {
        var cards = Results.Where(c => c.Icon is null && c.IconUrl.Length > 0).ToList();

        foreach (var card in cards)
        {
            if (ct.IsCancellationRequested) return;

            var bytes = await IconCache.Instance.GetAsync(card.IconUrl, _context.Http, ct);
            if (bytes is null || bytes.Length == 0) continue;

            try
            {
                var image = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                card.Icon = image;
            }
            catch
            {
                // 图标坏了不影响列表
            }
        }
    }
}
