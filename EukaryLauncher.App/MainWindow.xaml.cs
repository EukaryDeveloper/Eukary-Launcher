using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using EukaryLauncher.App.Services;
using EukaryLauncher.App.ViewModels;
using EukaryLauncher.Core.Models;
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

        AppServices.ApplyLauncherVisibility = value =>
        {
            switch (value)
            {
                case LauncherVisibility.CloseImmediately:
                    Close();
                    break;
                case LauncherVisibility.HideAndCloseOnExit:
                case LauncherVisibility.HideAndRestoreOnExit:
                    Hide();
                    break;
                case LauncherVisibility.Minimize:
                    WindowState = WindowState.Minimized;
                    break;
            }
        };

        AppServices.OnGameExited = closeLauncher =>
        {
            if (closeLauncher)
            {
                Close();
            }
            else
            {
                Show();
                Activate();
            }
        };

        // 页面切换时播放 200ms 淡入
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // 版本选择页：开页 / 关页（返回与切换共用一条回程动画）
        AppServices.OpenVersionSelect = OpenVersionSelectUi;
        _vm.VersionSelect.CloseRequested += () => Dispatcher.BeginInvoke(CloseVersionSelectUi);

        // 模态确认对话框
        AppServices.ShowModalConfirm = ShowConfirmModal;

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
        SourceInitialized += OnSourceInitialized;
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

    // ==================== 版本选择页：四向转场动画 ====================
    // 打开：主侧栏向左撤出 / 主页按钮向右撤出 / 新侧栏从左拉入 / 版本列表从右拉入
    // 关闭（返回与切换共用）：完全按原路线反向

    private bool _versionSelectAnimating;

    private void OpenVersionSelectUi()
    {
        if (VersionSelectOverlay.Visibility == Visibility.Visible || _versionSelectAnimating) return;

        _vm.VersionSelect.RefreshFolders();
        _versionSelectAnimating = true;

        // 起点：旧页面归位且不透明，新页面停在外侧且覆盖层全透明
        BeginSlide(MainSidebarTf, 0);
        BeginSlide(PageHostTf, 0);
        MainSidebar.BeginAnimation(OpacityProperty, null);
        MainSidebar.Opacity = 1;
        PageHost.BeginAnimation(OpacityProperty, null);
        PageHost.Opacity = 1;
        VersionSelectView.SnapClosed(MainSidebarWidth, PageSlideOffset);
        VersionSelectOverlay.BeginAnimation(OpacityProperty, null);
        VersionSelectOverlay.Opacity = 0;
        VersionSelectOverlay.Visibility = Visibility.Visible;

        // ① 覆盖层与旧页面交叉淡化：新页面不是"啪"地盖上来，而是慢慢浮出
        FadeTo(VersionSelectOverlay, 0, 1, 260);

        // ② 旧页面退场：侧栏完全滑出，页面只退一小段并淡出（视差 + 淡化，不硬推整屏）
        SlideTo(MainSidebarTf, -MainSidebarWidth, 340);
        FadeTo(MainSidebar, 1, 0, 220);
        SlideTo(PageHostTf, PageSlideOffset, 380);
        FadeTo(PageHost, 1, 0, 240);

        // ③ 新页面进场：侧栏先到，信息流晚 70ms 跟上，形成层叠感
        SlideTo(VersionSelectView.SidebarTransform, 0, 340, onCompleted: () => _versionSelectAnimating = false);
        SlideTo(VersionSelectView.ContentTransform, 0, 400, delayMs: 70);
    }

    private void CloseVersionSelectUi()
    {
        if (VersionSelectOverlay.Visibility != Visibility.Visible || _versionSelectAnimating) return;

        _versionSelectAnimating = true;

        // 新页面先退（反向），旧页面稍晚淡回，避免两块内容同起同落
        SlideTo(VersionSelectView.SidebarTransform, -MainSidebarWidth, 320);
        SlideTo(VersionSelectView.ContentTransform, PageSlideOffset, 340);
        FadeTo(VersionSelectOverlay, 1, 0, 300, delayMs: 60, onCompleted: () =>
        {
            VersionSelectOverlay.Visibility = Visibility.Collapsed;
            VersionSelectOverlay.BeginAnimation(OpacityProperty, null);
            VersionSelectOverlay.Opacity = 0;
            _versionSelectAnimating = false;
        });

        SlideTo(MainSidebarTf, 0, 380, delayMs: 40);
        FadeTo(MainSidebar, 0, 1, 280, delayMs: 40);
        SlideTo(PageHostTf, 0, 380, delayMs: 40);
        FadeTo(PageHost, 0, 1, 280, delayMs: 40);
    }

    private const double MainSidebarWidth = 180;

    /// <summary>旧页面退场位移。只退一小段再淡出，比把整屏推出窗口柔和得多。</summary>
    private const double PageSlideOffset = 220;

    /// <summary>
    /// 页面级转场的缓动：cubic-bezier(0.32, 0.72, 0, 1)。
    /// 比全站通用的 (0.2, 0.8, 0.2, 1) 起步更缓、收尾拖得更长，大面积位移才不显得急。
    /// </summary>
    private static System.Windows.Media.Animation.EasingFunctionBase PageEase => new Animation.CubicBezierEase
    {
        ControlPoint1 = new Point(0.32, 0.72),
        ControlPoint2 = new Point(0, 1),
    };

    /// <summary>X 平滑动画；无动画环境（系统关动效）直接落到终点。</summary>
    private void SlideTo(TranslateTransform tf, double to, int ms = 300, int delayMs = 0, Action? onCompleted = null)
    {
        if (!AnimationHelper.IsEnabled)
        {
            tf.X = to;
            onCompleted?.Invoke();
            return;
        }

        var anim = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms)))
        {
            EasingFunction = PageEase,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        if (onCompleted is not null)
            anim.Completed += (_, _) => onCompleted();
        tf.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>不透明度动画（两端都显式给值，不依赖当前值）。</summary>
    private void FadeTo(UIElement element, double from, double to, int ms = 240, int delayMs = 0,
        Action? onCompleted = null)
    {
        if (!AnimationHelper.IsEnabled)
        {
            element.Opacity = to;
            onCompleted?.Invoke();
            return;
        }

        var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)))
        {
            EasingFunction = PageEase,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        };
        if (onCompleted is not null)
            anim.Completed += (_, _) => onCompleted();
        element.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>停掉正在进行的动画并直接落位（SnapXxx 之前调用）。</summary>
    private static void BeginSlide(TranslateTransform tf, double x)
    {
        tf.BeginAnimation(TranslateTransform.XProperty, null);
        tf.X = x;
    }

    // ==================== 通用模态确认对话框 ====================

    private ModalConfirmRequest? _confirmRequest;
    private System.Windows.Threading.DispatcherTimer? _confirmCountdownTimer;
    private int _confirmSecondsLeft;

    private void ShowConfirmModal(ModalConfirmRequest request)
    {
        _confirmRequest = request;
        ConfirmTitle.Text = request.Title;
        ConfirmMessage.Text = request.Message;
        ConfirmActionButton.Style = (Style)FindResource(request.Destructive ? "DangerButton" : "PrimaryButton");
        ConfirmHost.Visibility = Visibility.Visible;
        ConfirmHost.BeginAnimation(OpacityProperty, null);

        if (AnimationHelper.IsEnabled)
        {
            var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)))
            {
                EasingFunction = new Animation.CubicBezierEase(),
            };
            ConfirmHost.BeginAnimation(OpacityProperty, fade);
        }

        StartConfirmCountdown(request);
    }

    private void StartConfirmCountdown(ModalConfirmRequest request)
    {
        _confirmCountdownTimer?.Stop();
        _confirmCountdownTimer = null;
        _confirmSecondsLeft = request.CountdownSeconds;
        UpdateConfirmButton();

        if (_confirmSecondsLeft <= 0)
        {
            ConfirmActionButton.IsEnabled = true;
            return;
        }

        ConfirmActionButton.IsEnabled = false;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            _confirmSecondsLeft--;
            UpdateConfirmButton();
            if (_confirmSecondsLeft <= 0)
            {
                timer.Stop();
                ConfirmActionButton.IsEnabled = true;
            }
        };
        _confirmCountdownTimer = timer;
        timer.Start();
    }

    private void UpdateConfirmButton()
    {
        var text = _confirmRequest?.ConfirmText ?? "确认";
        ConfirmActionButton.Content = _confirmSecondsLeft > 0 ? $"{text}（{_confirmSecondsLeft}）" : text;
    }

    private void OnConfirmActionClick(object sender, RoutedEventArgs e)
    {
        var request = _confirmRequest;
        HideConfirmModal();
        request?.OnConfirm();
    }

    private void OnConfirmCancelClick(object sender, RoutedEventArgs e) => HideConfirmModal();

    private void HideConfirmModal()
    {
        _confirmCountdownTimer?.Stop();
        _confirmCountdownTimer = null;
        _confirmRequest = null;
        ConfirmHost.Visibility = Visibility.Collapsed;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
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

    /// <summary>
    /// 自绘标题栏（WindowStyle=None + WindowChrome）的窗口最大化时，系统给的最大尺寸是
    /// 整块屏幕而不是工作区，窗口底部会钻到任务栏底下。这里接管 WM_GETMINMAXINFO，
    /// 把最大化尺寸改成「窗口所在显示器」的工作区大小——任务栏在上下左右、
    /// 多显示器、任务栏自动隐藏都能自动跟上。
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MonitorInfo { CbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        // MINMAXINFO 与工作区都是屏幕物理像素，直接换算，DPI 由 WPF 自己处理
        var work = info.RcWork;
        var full = info.RcMonitor;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.PtMaxPosition.X = work.Left - full.Left;
        mmi.PtMaxPosition.Y = work.Top - full.Top;
        mmi.PtMaxSize.X = work.Right - work.Left;
        mmi.PtMaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(mmi, lParam, false);

        // 不截断消息：WPF 自己还要读这个结构
        handled = false;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Int32Point PtReserved;
        public Int32Point PtMaxSize;
        public Int32Point PtMaxPosition;
        public Int32Point PtMinTrackSize;
        public Int32Point PtMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Int32Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Int32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public Int32Rect RcMonitor;
        public Int32Rect RcWork;
        public uint DwFlags;
    }
}
