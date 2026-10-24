using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>实时日志页：直接绑定 LogService 的同一条缓冲。</summary>
public sealed partial class ConsoleViewModel : ViewModelBase
{
    private readonly LogService _log = LogService.Instance;

    public ObservableCollection<LogEntry> Entries => _log.Entries;

    [RelayCommand]
    private void Clear() => _log.Clear();

    [RelayCommand]
    private void CopyAll()
    {
        try
        {
            var text = string.Join(Environment.NewLine, _log.Entries.Select(e => e.Display));
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被占用时忽略
        }
    }
}
