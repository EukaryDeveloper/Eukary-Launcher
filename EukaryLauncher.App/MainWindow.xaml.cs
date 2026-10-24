using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using EukaryLauncher.App.Services;
using EukaryLauncher.App.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace EukaryLauncher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        AppServices.UiDispatcher = Dispatcher;

        _vm = new MainViewModel(AppServices.Settings);
        DataContext = _vm;

        AppServices.NavigateToConsole = () =>
        {
            _vm.CurrentPage = NavPage.Console;
        };

        // 页面切换时播放 200ms 淡入
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // 窗口启动：200ms 淡入 + Y 8→0
        if (AnimationHelper.IsEnabled)
        {
            var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = new Animation.CubicBezierEase(),
            };
            var slide = new DoubleAnimation(8, 0, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = new Animation.CubicBezierEase(),
            };
            RenderTransform = new TranslateTransform();
            BeginAnimation(OpacityProperty, fade);
            RenderTransform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        Closing += OnClosing;
        Loaded += (_, _) => PlayPageFade();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentViewModel))
            PlayPageFade();
    }

    private void PlayPageFade()
    {
        if (PageHost is null) return;

        // 系统关闭动效时直接跳过
        if (!AnimationHelper.IsEnabled) { PageHost.Opacity = 1; return; }

        PageHost.BeginAnimation(OpacityProperty, null);
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new Animation.CubicBezierEase(),
        };
        PageHost.BeginAnimation(OpacityProperty, fade);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>关闭时 150ms 淡出 + 缩放到 0.98，尊重系统"关闭动画"设置。</summary>
    private bool _isClosing;
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        e.Cancel = true;

        if (!AnimationHelper.IsEnabled)
        {
            AppServices.Settings.Save();
            Application.Current.Shutdown();
            return;
        }

        var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(150)))
        {
            EasingFunction = new Animation.CubicBezierEase(),
        };
        fade.Completed += (_, _) =>
        {
            AppServices.Settings.Save();
            Application.Current.Shutdown();
        };
        BeginAnimation(OpacityProperty, fade);

        RenderTransform = new ScaleTransform(1, 1, Width / 2, Height / 2);
        var scale = new DoubleAnimation(1, 0.98, new Duration(TimeSpan.FromMilliseconds(150)))
        {
            EasingFunction = new Animation.CubicBezierEase(),
        };
        RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }
}
