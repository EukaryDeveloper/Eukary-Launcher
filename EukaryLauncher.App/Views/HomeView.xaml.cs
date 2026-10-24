using System.Windows.Controls;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();

        // 日志追加时自动滚到底
        LogService.Instance.Entries.CollectionChanged += (_, _) =>
        {
            if (LogScroller is null) return;
            Dispatcher.BeginInvoke(() => LogScroller.ScrollToBottom(), System.Windows.Threading.DispatcherPriority.Background);
        };
    }
}
