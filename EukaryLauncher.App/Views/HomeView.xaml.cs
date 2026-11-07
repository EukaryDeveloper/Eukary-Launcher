using System.Windows;
using System.Windows.Controls;
using EukaryLauncher.App.Services;

namespace EukaryLauncher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnVersionSelectClick(object sender, RoutedEventArgs e)
    {
        // MainWindow 打开版本选择覆盖层（四向转场动画）
        AppServices.OpenVersionSelect?.Invoke();
    }
}
