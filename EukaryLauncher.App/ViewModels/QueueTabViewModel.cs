using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>
/// 下载队列：所有安装任务都在这里排队，一个跑完再跑下一个。
/// 成功的卡片自动销毁，失败的卡片留在原地，写明原因并允许点一下重新排队。
/// </summary>
public sealed partial class QueueTabViewModel : ViewModelBase
{
    private readonly DownloadManager _queue = AppServices.Queue;

    public QueueTabViewModel()
    {
        _queue.Changed += () =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(SummaryText));
        };
    }

    public System.Collections.ObjectModel.ObservableCollection<DownloadJob> Jobs => _queue.Jobs;

    public bool IsEmpty => Jobs.Count == 0;

    public string SummaryText
    {
        get
        {
            if (Jobs.Count == 0) return "暂无任务";
            var running = Jobs.Count(j => j.Status == JobStatus.Running);
            var pending = Jobs.Count(j => j.Status == JobStatus.Pending);
            var failed = Jobs.Count(j => j.Status == JobStatus.Failed);
            var parts = new List<string>();
            if (running > 0) parts.Add($"进行中 {running}");
            if (pending > 0) parts.Add($"等待 {pending}");
            if (failed > 0) parts.Add($"失败 {failed}");
            return string.Join(" · ", parts);
        }
    }

    [RelayCommand]
    private void Retry(DownloadJob? job)
    {
        if (job is null) return;
        _queue.Retry(job);
    }

    [RelayCommand]
    private void ClearFinished()
    {
        _queue.ClearFinished();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
