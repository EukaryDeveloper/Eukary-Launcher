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

    public double OverallProgress => Steps.Count == 0 ? 0 : Steps.Average(s => s.Progress);
    public int OverallPercent => (int)Math.Round(OverallProgress);
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Cancelled;

    partial void OnStatusChanged(JobStatus value)
    {
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
/// 串行下载队列。任务按加入顺序排，同一时刻只跑一个，
/// 成功就销毁卡片并弹通知，失败则保留卡片并写明原因，点一下可以重新排队。
/// </summary>
public sealed class DownloadManager
{
    private readonly LogService _log = LogService.Instance;
    private readonly ToastService _toast;

    private bool _pumping;
    private CancellationTokenSource? _cts;

    public DownloadManager(ToastService toast) => _toast = toast;

    public ObservableCollection<DownloadJob> Jobs { get; } = new();

    public event Action? Changed;

    public bool HasPending => Jobs.Any(j => j.Status == JobStatus.Pending);
    public bool HasRunning => Jobs.Any(j => j.Status == JobStatus.Running);

    public void Enqueue(DownloadJob job)
    {
        AppServices.Post(() =>
        {
            Jobs.Add(job);
            Changed?.Invoke();
        });
        _ = PumpAsync();
    }

    /// <summary>失败/取消的卡片点一下，重新排队。</summary>
    public void Retry(DownloadJob job)
    {
        if (!job.CanRetry) return;

        AppServices.Post(() =>
        {
            foreach (var step in job.Steps) step.Reset();
            job.ErrorMessage = null;
            job.CurrentText = "";
            job.Status = JobStatus.Pending;
            Changed?.Invoke();
        });

        _ = PumpAsync();
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

    private async Task PumpAsync()
    {
        if (_pumping) return;
        _pumping = true;

        try
        {
            while (true)
            {
                var job = await GetNextPendingAsync();
                if (job is null) break;

                _cts = new CancellationTokenSource();
                await RunAsync(job, _cts.Token);
            }
        }
        finally
        {
            _pumping = false;
            Changed?.Invoke();
        }
    }

    private async Task<DownloadJob?> GetNextPendingAsync()
    {
        var tcs = new TaskCompletionSource<DownloadJob?>();
        AppServices.Post(() =>
            tcs.SetResult(Jobs.FirstOrDefault(j => j.Status == JobStatus.Pending)));
        return await tcs.Task;
    }

    private async Task RunAsync(DownloadJob job, CancellationToken ct)
    {
        AppServices.Post(() =>
        {
            job.Status = JobStatus.Running;
            job.ErrorMessage = null;
            Changed?.Invoke();
        });

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
            AppServices.Post(() =>
            {
                job.Status = JobStatus.Cancelled;
                job.ErrorMessage = "已取消";
                Changed?.Invoke();
            });
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
    }

    /// <summary>
    /// 把 VersionInstaller 的阶段进度映射到卡片上的各个子步骤。
    /// 用户要看的是「每个资源分别到哪一步了」，所以按 stage 分片而不是只给一个总数。
    /// </summary>
    public static IProgress<InstallProgress> RouteByStage(DownloadJob job)
    {
        var last = new double[8];

        return new Progress<InstallProgress>(p =>
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
        return new Progress<InstallProgress>(p =>
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
