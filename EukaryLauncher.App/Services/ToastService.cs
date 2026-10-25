using System.Collections.ObjectModel;

namespace EukaryLauncher.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed class ToastItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public ToastKind Kind { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    /// <summary>正在淡出。UI 据此播放离场动画，动画结束后才真正移除。</summary>
    public bool IsLeaving { get; set; }
}

/// <summary>
/// 轻量通知。安装、入队、失败原因都走这里——比弹 MessageBox 友好得多，
/// 也不会打断正在浏览的操作。
/// </summary>
public sealed class ToastService
{
    private const int DefaultLifetimeMs = 3600;

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void Show(string message, ToastKind kind = ToastKind.Info, int lifetimeMs = DefaultLifetimeMs)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var item = new ToastItem { Message = message, Kind = kind };

        AppServices.Post(() =>
        {
            // 同文案连续触发时只更新时间，避免刷屏
            var existing = Toasts.FirstOrDefault(t => t.Message == item.Message && t.Kind == item.Kind);
            if (existing is not null) return;

            Toasts.Add(item);

            // 超过 4 条时挤掉最老的一条，别糊满屏幕
            while (Toasts.Count > 4) Toasts.RemoveAt(0);

            _ = DismissLaterAsync(item, lifetimeMs);
        });
    }

    public void Info(string message) => Show(message, ToastKind.Info);
    public void Success(string message) => Show(message, ToastKind.Success);
    public void Warning(string message) => Show(message, ToastKind.Warning);
    public void Error(string message) => Show(message, ToastKind.Error);

    public void Dismiss(ToastItem item) => AppServices.Post(() =>
    {
        if (Toasts.Contains(item)) Toasts.Remove(item);
    });

    private async Task DismissLaterAsync(ToastItem item, int lifetimeMs)
    {
        try
        {
            await Task.Delay(lifetimeMs);
            item.IsLeaving = true;
            await Task.Delay(180);
            Dismiss(item);
        }
        catch
        {
            // 关窗口时序问题，忽略
        }
    }
}
