using System.Windows;
using System.Windows.Controls;

namespace EukaryLauncher.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnVersionSelectClick(object sender, RoutedEventArgs e)
    {
        VersionSelectPopup.IsOpen = true;
    }

    private void OnVersionListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        VersionSelectPopup.IsOpen = false;
    }
}
