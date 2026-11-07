using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

public enum JobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Paused,
}

/// <summary>任务里的一个子步骤（下载依赖库、下载资源文件、下载某个模组文件……）。</summary>
public sealed partial class DownloadStep : ObservableObject
{
    public DownloadStep(string name) => Name = name;

    public string Name { get; }

    [ObservableProperty] private JobStatus status = JobStatus.Pending;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string detail = "";

    public string StatusText => Status switch
    {
        JobStatus.Pending => "排队中",
        JobStatus.Running => $"进行中 {(int)Math.Round(Progress)}%",
        JobStatus.Succeeded => "完成",
        JobStatus.Failed => "失败",
        JobStatus.Cancelled => "已取消",
        JobStatus.Paused => "已暂停",
        _ => "",
    };

    partial void OnStatusChanged(JobStatus value) => OnPropertyChanged(nameof(StatusText));
    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(StatusText));

    public void Reset()
    {
        Status = JobStatus.Pending;
        Progress = 0;
        Detail = "";
    }
}

/// <summary>队列里的一张卡片。</summary>
public sealed partial class DownloadJob : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Kind { get; init; } = "";

    /// <summary>真正的执行体。由 DownloadJobs 工厂注入，队列只负责调度。</summary>
    public Func<DownloadJob, CancellationToken, Task>? Runner { get; set; }

    public ObservableCollection<DownloadStep> Steps { get; } = new();

    [ObservableProperty] private JobStatus status = JobStatus.Pending;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string currentText = "";

    /// <summary>暂停时由队列置位，RunAsync 收到取消异常后按它区分「已暂停」和「已取消」。</summary>
    [ObservableProperty] private bool pauseRequested;

    public double OverallProgress => Steps.Count == 0 ? 0 : Steps.Average(s => s.Progress);
    public int OverallPercent => (int)Math.Round(OverallProgress);
    public bool CanPause => Status == JobStatus.Running;
    public bool CanCancel => Status is JobStatus.Pending or JobStatus.Running;
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Paused;

    partial void OnStatusChanged(JobStatus value)
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(StatusText));
    }

    public string StatusText => Status switch
    {
        JobStatus.Pending => "等待中",
        JobStatus.Running => $"安装中 {OverallPercent}%",
        JobStatus.Succeeded => "已完成",
        JobStatus.Failed => "失败",
        JobStatus.Cancelled => "已取消",
        JobStatus.Paused => "已暂停",
        _ => "",
    };

    /// <summary>建立步骤并挂上进度联动，让卡片底部的总进度条能跟着走。</summary>
    public DownloadStep AddStep(string name)
    {
        var step = new DownloadStep(name);
        step.PropertyChanged += (_, _) => OnPropertyChanged(nameof(OverallProgress));
        Steps.Add(step);
        OnPropertyChanged(nameof(OverallProgress));
        return step;
    }

    public DownloadStep Step(int index)
    {
        while (Steps.Count <= index) AddStep($"步骤 {Steps.Count + 1}");
        return Steps[index];
    }

    internal void Report(int index, double progress, string? detail, JobStatus status)
    {
        var step = Step(index);
        AppServices.Post(() =>
        {
            if (detail is not null) step.Detail = detail;
            step.Progress = Math.Clamp(progress, 0, 100);
            step.Status = status;
            if (detail is not null) CurrentText = detail;
        });
    }
}

/// <summary>
/// 下载队列，最多同时跑 MaxConcurrent 个任务。
/// 一个任务卡住不会拖死整个队列——空出来的槽位会继续接后面的任务。
/// 成功就销毁卡片并弹通知，失败/暂停/取消则保留卡片，点一下或按「继续」重新排队。
/// </summary>
public sealed class DownloadManager
{
    /// <summary>同时进行的任务上限。安装是重 IO 活，太多并行会互相抢磁盘。</summary>
    private const int MaxConcurrent = 3;

    private readonly LogService _log = LogService.Instance;
    private readonly ToastService _toast;

    /// <summary>每个进行中任务一个取消源，暂停和取消都靠触发它。</summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _ctsMap = new();

    public DownloadManager(ToastService toast) => _toast = toast;

    public ObservableCollection<DownloadJob> Jobs { get; } = new();

    public event Action? Changed;

    public void Enqueue(DownloadJob job)
    {
        AppServices.Post(() =>
        {
            Jobs.Add(job);
            Changed?.Invoke();
        });
        _ = PumpAsync();
    }

    /// <summary>失败/取消/暂停的卡片点一下，重新排队（下载续传靠磁盘上的 .part，进度条会重新爬）。</summary>
    public void Retry(DownloadJob job)
    {
        if (!job.CanRetry) return;

        AppServices.Post(() =>
        {
            foreach (var step in job.Steps) step.Reset();
            job.ErrorMessage = null;
            job.CurrentText = "";
            job.PauseRequested = false;
            job.Status = JobStatus.Pending;
            Changed?.Invoke();
        });

        _ = PumpAsync();
    }

    /// <summary>
    /// 取消任务。排队中的直接移除；进行中的触发取消令牌，
    /// Runner 以 OperationCanceledException 收场后再移除卡片并弹通知。
    /// </summary>
    public void Cancel(DownloadJob job)
    {
        if (job.Status == JobStatus.Pending)
        {
            AppServices.Post(() =>
            {
                Jobs.Remove(job);
                Changed?.Invoke();
            });
            _toast.Info($"已取消「{job.Title}」");
            return;
        }
        if (job.Status != JobStatus.Running) return;

        AppServices.Post(() =>
        {
            job.PauseRequested = false;
            if (_ctsMap.TryGetValue(job.Id, out var cts)) cts.Cancel();
        });
    }

    /// <summary>暂停任务：和取消一样走取消令牌，但卡片状态记为「已暂停」，内容留在原地等继续。</summary>
    public void Pause(DownloadJob job)
    {
        if (job.Status != JobStatus.Running) return;

        AppServices.Post(() =>
        {
            job.PauseRequested = true;
            if (_ctsMap.TryGetValue(job.Id, out var cts)) cts.Cancel();
        });
    }

    /// <summary>把已经完成的任务卡片清掉。</summary>
    public void ClearFinished()
    {
        AppServices.Post(() =>
        {
            for (var i = Jobs.Count - 1; i >= 0; i--)
                if (Jobs[i].Status == JobStatus.Succeeded)
                    Jobs.RemoveAt(i);
            Changed?.Invoke();
        });
    }

    /// <summary>有空槽就认领一个排队中的任务开跑，认领和标记 Running 在同一次 UI 线程投递里完成（多个泵并发也不会重复认领）。</summary>
    private async Task PumpAsync()
    {
        while (true)
        {
            var job = await ClaimNextAsync();
            if (job is null) break;

            var cts = new CancellationTokenSource();
            _ctsMap[job.Id] = cts;
            _ = RunAsync(job, cts.Token);
        }
    }

    private Task<DownloadJob?> ClaimNextAsync()
    {
        var tcs = new TaskCompletionSource<DownloadJob?>();
        AppServices.Post(() =>
        {
            var running = Jobs.Count(j => j.Status == JobStatus.Running);
            var job = running >= MaxConcurrent
                ? null
                : Jobs.FirstOrDefault(j => j.Status == JobStatus.Pending);
            if (job is not null) job.Status = JobStatus.Running;
            tcs.SetResult(job);
        });
        return tcs.Task;
    }

    private async Task RunAsync(DownloadJob job, CancellationToken ct)
    {
        var paused = false;
        try
        {
            if (job.Runner is null) throw new InvalidOperationException("任务没有可执行的内容。");
            await job.Runner(job, ct);

            foreach (var step in job.Steps) job.Report(job.Steps.IndexOf(step), 100, null, JobStatus.Succeeded);

            AppServices.Post(() =>
            {
                job.Status = JobStatus.Succeeded;
                // 安装完成 → 销毁卡片
                Jobs.Remove(job);
                Changed?.Invoke();
            });

            _log.Info($"{job.Kind}{job.Title} 安装完成");
            _toast.Success($"{job.Title} 安装完成");
        }
        catch (OperationCanceledException)
        {
            paused = job.PauseRequested;
            AppServices.Post(() =>
            {
                job.PauseRequested = false;
                // 卡住的步骤按暂停/取消收尾，别留着「进行中 x%」的假象
                foreach (var step in job.Steps)
                    if (step.Status == JobStatus.Running)
                        step.Status = paused ? JobStatus.Paused : JobStatus.Cancelled;

                if (paused)
                {
                    job.ErrorMessage = "已暂停";
                    job.CurrentText = "已暂停";
                    job.Status = JobStatus.Paused;
                }
                else
                {
                    // 取消即出队，卡片不再保留
                    Jobs.Remove(job);
                }
                Changed?.Invoke();
            });

            if (paused)
                _log.Info($"{job.Kind}{job.Title} 已暂停");
            else
            {
                _log.Info($"{job.Kind}{job.Title} 已取消");
                _toast.Info($"已取消「{job.Title}」");
            }
        }
        catch (Exception ex)
        {
            var reason = Flatten(ex);
            AppServices.Post(() =>
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = reason;
                job.CurrentText = "安装失败";
                Changed?.Invoke();
            });

            _log.Error($"{job.Kind}{job.Title} 安装失败：{ex}");
            _toast.Error($"{job.Title} 安装失败：{reason}");
        }
        finally
        {
            _ctsMap.Remove(job.Id);
            // 空出来的槽位接着拉下一个任务
            _ = PumpAsync();
        }
    }

    /// <summary>
    /// 在后台线程（Runner）里创建进度回调时用它。
    /// Progress&lt;T&gt; 在没有 SynchronizationContext 的线程上创建会同步回调，
    /// 回调里改 UI 绑定的集合/属性就成了跨线程操作，必须显式切回 UI 线程。
    /// </summary>
    public static IProgress<T> UiProgress<T>(Action<T> handler)
    {
        return new Progress<T>(p => AppServices.Post(() => handler(p)));
    }

    /// <summary>
    /// 把 VersionInstaller 的阶段进度映射到卡片上的各个子步骤。
    /// 用户要看的是「每个资源分别到哪一步了」，所以按 stage 分片而不是只给一个总数。
    /// </summary>
    public static IProgress<InstallProgress> RouteByStage(DownloadJob job)
    {
        var last = new double[8];

        return UiProgress<InstallProgress>(p =>
        {
            var idx = (int)p.Stage;
            if (idx > (int)InstallStage.Logging) idx = (int)InstallStage.Logging;

            var pct = p.TotalFiles > 0
                ? p.CompletedFiles * 100.0 / p.TotalFiles
                : p.TotalBytes is > 0
                    ? p.ReceivedBytes * 100.0 / p.TotalBytes.Value
                    : p.Stage == InstallStage.Done ? 100 : 45;

            pct = Math.Clamp(pct, 0, 100);

            for (var i = 0; i < idx; i++)
            {
                if (last[i] >= 100) continue;
                last[i] = 100;
                job.Report(i, 100, null, JobStatus.Succeeded);
            }

            // 每 1% 才刷一次，避免上千个资源文件把 UI 线程刷爆
            if (Math.Abs(last[idx] - pct) >= 1 || pct >= 100 || !string.IsNullOrEmpty(p.Message))
            {
                last[idx] = pct;
                job.Report(idx, pct, p.Message, JobStatus.Running);
            }
        });
    }

    /// <summary>整包只有一件事（比如下载单个模组文件、下载加载器依赖库）时用它。</summary>
    public static IProgress<InstallProgress> RouteToSingleStep(DownloadJob job, int index)
    {
        var last = 0d;
        return UiProgress<InstallProgress>(p =>
        {
            var pct = p.TotalFiles > 0
                ? p.CompletedFiles * 100.0 / p.TotalFiles
                : p.TotalBytes is > 0
                    ? p.ReceivedBytes * 100.0 / p.TotalBytes.Value
                    : 45;
            pct = Math.Clamp(pct, 0, 100);
            if (Math.Abs(last - pct) < 1 && pct < 100) return;
            last = pct;
            job.Report(index, pct, p.Message, JobStatus.Running);
        });
    }

    private static string Flatten(Exception ex)
    {
        if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
        {
            var first = agg.InnerExceptions[0];
            var extra = agg.InnerExceptions.Count > 1 ? $"（另有 {agg.InnerExceptions.Count - 1} 个文件失败）" : "";
            return $"{first.Message}{extra}";
        }
        return ex.Message;
    }
}
