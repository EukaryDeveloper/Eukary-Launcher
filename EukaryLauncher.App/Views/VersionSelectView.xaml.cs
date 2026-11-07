using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EukaryLauncher.App.ViewModels;

namespace EukaryLauncher.App.Views;

public partial class VersionSelectView : UserControl
{
    public VersionSelectView()
    {
        InitializeComponent();
    }

    public VersionSelectViewModel? ViewModel => DataContext as VersionSelectViewModel;

    /// <summary>新侧边栏滑入/滑出用的 X 变换（MainWindow 动画编排用）。</summary>
    public TranslateTransform SidebarTransform => SidebarTf;

    /// <summary>右侧信息流滑入/滑出用的 X 变换。</summary>
    public TranslateTransform ContentTransform => ContentTf;

    /// <summary>摆到「已关闭」的初始位置：侧边栏在窗口左侧外，信息流在右侧外。</summary>
    public void SnapClosed(double sidebarWidth, double contentOffset)
    {
        SidebarTf.BeginAnimation(TranslateTransform.XProperty, null);
        ContentTf.BeginAnimation(TranslateTransform.XProperty, null);
        SidebarTf.X = -sidebarWidth;
        ContentTf.X = contentOffset;
    }

    /// <summary>摆到「已打开」的位置（关闭动画结束时归位）。</summary>
    public void SnapOpen()
    {
        SidebarTf.BeginAnimation(TranslateTransform.XProperty, null);
        ContentTf.BeginAnimation(TranslateTransform.XProperty, null);
        SidebarTf.X = 0;
        ContentTf.X = 0;
    }
}
