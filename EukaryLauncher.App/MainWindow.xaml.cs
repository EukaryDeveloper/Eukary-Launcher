using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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

        // 版本选择页里点了「下载 Minecraft」：落到下载页的「原版游戏」，覆盖层随之下收
        _vm.VersionSelect.GoDownloadRequested += () => Dispatcher.BeginInvoke(() =>
        {
            _vm.Download.OpenVanillaTab();
            _vm.CurrentPage = NavPage.Download;
            CloseVersionSelectUi();
        });

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
        Loaded += (_, _) =>
        {
            PageHost.SizeChanged += OnPageHostSizeChanged;
            PlayPageFade();
            StartStallProbe();
        };
        SourceInitialized += OnSourceInitialized;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage)) return;
        PlayPagePush();
        SchedulePendingActivation();
    }

    // ==================== 页面数据加载延后 ====================
    // 动画期间不碰磁盘/网络/列表重建：这些同步开销会和逐帧滚动抢 UI 线程。
    // 一律等轨道落定后再跑，用户看到的只是「页面先滑到位，内容再填上」。

    private readonly Queue<Action> _afterPush = new();

    private void SchedulePendingActivation()
    {
        var activation = _vm.TakePendingActivation();
        if (activation is null) return;

        // 转场动画正在跑：排队，落定后统一执行
        if (_pushFrameHooked)
        {
            _afterPush.Enqueue(activation);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, activation);
    }

    private void RunAfterPush()
    {
        if (_afterPush.Count == 0) return;
        var batch = _afterPush.ToArray();
        _afterPush.Clear();

        // 不在帧回调里直接跑：那会把加载开销算进当帧
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (var action in batch) action();
        }));
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

    // ==================== 排障：UI 线程卡顿探针 ====================
    // EKL_PUSH_TRACE=1 时启用：后台线程每 4ms 往 UI 线程塞一个空委托量往返耗时，
    // 一次推动结束时把统计写进 %TEMP%\ekl-push-trace.txt（tools/ui_push_trace.py 会读）。
    // 比看 Rendering 回调间隔可靠：一次滚动会触发若干次 Rendering，
    // 光看间隔分不清「掉帧」和「多跑了几次渲染」，而往返耗时长 = UI 线程被占住了。

    private static bool PushTrace => Environment.GetEnvironmentVariable("EKL_PUSH_TRACE") == "1";

    private readonly System.Text.StringBuilder _pushTrace = new();

    private void TracePush(string line)
    {
        if (PushTrace) _pushTrace.AppendLine(line);
    }

    /// <summary>把累积的排障数据一次性落盘。</summary>
    private void FlushPushTrace()
    {
        if (!PushTrace || _pushTrace.Length == 0) return;
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ekl-push-trace.txt");
            System.IO.File.AppendAllText(path, _pushTrace.ToString());
        }
        catch
        {
            // 排障用，写不进去就算了
        }
        _pushTrace.Clear();
    }

    // ---- UI 线程卡顿探针：后台每 4ms 往 UI 线程塞一个空委托，量往返耗时 ----
    // 比看 Rendering 回调间隔可靠：WPF 的一次滚动会额外触发若干次 Rendering，
    // 光看间隔分不清「掉帧」和「多跑了几次渲染」，而往返耗时长 = UI 线程被占住了。

    private bool _stallProbeOn;
    private readonly List<double> _stallSamples = new();
    private readonly List<double> _stallAt = new();

    private async void StartStallProbe()
    {
        if (!PushTrace || _stallProbeOn) return;
        _stallProbeOn = true;
        try
        {
            while (_stallProbeOn)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
                clock.Stop();
                _stallSamples.Add(clock.Elapsed.TotalMilliseconds);
                _stallAt.Add(_pushFrameHooked ? _pagePushClock.Elapsed.TotalMilliseconds : -1);
                await Task.Delay(4);
            }
        }
        catch
        {
            _stallProbeOn = false;
        }
    }

    // ==================== 主页面切换：竖直轨道推动 ====================
    // 五个页面按侧栏顺序自上而下排在一条轨道上，切换时整条轨道滚动：
    //   去更下面的页（主页→下载）→ 轨道上移，原页向上退、新页从下进；
    //   去更上面的页（设置→日志）→ 轨道下移，方向正好相反。
    // 位移是「页数差 × 页高」，但时长固定，所以跨多页会快速掠过中间页面而不是变慢。
    //
    // 实现走 ScrollViewer.ScrollToVerticalOffset（逐帧驱动），不能用 TranslateTransform：
    // 本机渲染线程对平移的大元素不重绘（脏区域 bug），滚动路径才有可靠的呈现。

    /// <summary>页面在轨道上的物理顺序，必须与侧栏 NavItems 一致。</summary>
    private static readonly NavPage[] PageOrder =
    {
        NavPage.Home, NavPage.Download, NavPage.Console, NavPage.Settings, NavPage.About,
    };

    private int _pageIndex;
    private readonly System.Diagnostics.Stopwatch _pagePushClock = new();
    private double _pushFrom, _pushTo;
    private bool _pushFrameHooked;
    private double _lastPushMs = -1000;

    /// <summary>一次推动的总时长，与跨几页无关。</summary>
    private const int PagePushMs = 380;

    private void PlayPagePush()
    {
        if (PageHost is null || PageScroller is null) return;

        var target = Array.IndexOf(PageOrder, _vm.CurrentPage);
        if (target < 0) target = 0;
        var from = _pageIndex;
        _pageIndex = target;

        var height = PageHost.ActualHeight;

        // 系统关动效 / 布局还没量出高度：直接落位
        if (!AnimationHelper.IsEnabled || height <= 0)
        {
            StopPagePush();
            PageScroller.ScrollToVerticalOffset(target * Math.Max(height, 0));
            return;
        }

        // 目标页此前一直在视口外，WPF 没给它排过版也没光栅过。直接开滚的话，
        // 这份开销要算在「新页面第一次露面」的那一帧里（实测 15~40ms，等于顿一帧）。
        // 先让轨道露出 2px 再退回来：这一帧就把排版和首轮光栅做完了，
        // 用户看不见（2px），但动画第一帧就只剩增量绘制。
        var direction = Math.Sign(target - from);
        if (direction != 0 && !_pushFrameHooked && !_peeking)
        {
            var baseOffset = from * height;
            var peek = baseOffset + direction * 2;
            if (peek >= 0)
            {
                _pendingTarget = target;
                _peekRestore = baseOffset;
                _peekClock.Restart();
                _peeking = true;
                PageScroller.ScrollToVerticalOffset(peek);
                System.Windows.Media.CompositionTarget.Rendering += OnPeekFrame;
                return;
            }
        }

        StartPush(target, height);
    }

    // ---- 露 2px 预热：等这一帧真的渲出来（约 20ms）再把轨道收回原位 ----

    private bool _peeking;
    private int _pendingTarget;
    private double _peekRestore;
    private readonly System.Diagnostics.Stopwatch _peekClock = new();

    private void OnPeekFrame(object? sender, EventArgs e)
    {
        // 只等一帧：20ms 已覆盖一次完整合成
        if (_peekClock.Elapsed.TotalMilliseconds < 20) return;

        System.Windows.Media.CompositionTarget.Rendering -= OnPeekFrame;
        _peeking = false;
        if (PageScroller is null) return;

        // 预热期间用户又点了别的页：这次预热作废，别把它拉回去
        if (_pushFrameHooked || _pendingTarget != _pageIndex) return;

        PageScroller.ScrollToVerticalOffset(_peekRestore);
        StartPush(_pendingTarget, PageHost.ActualHeight);
    }

    private void StartPush(int target, double height)
    {
        _pageIndex = target;

        if (PushTrace) { _stallSamples.Clear(); _stallAt.Clear(); }

        // 从当前滚动位置滑到新位置，连点也不会跳
        _pushFrom = PageScroller.VerticalOffset;
        _pushTo = target * height;
        _pagePushClock.Restart();
        _lastPushMs = -1000;

        // 用渲染帧驱动而不是 DispatcherTimer：后者默认 Background 优先级，
        // 滚动这些重帧会把 tick 饿死，等积压渲染完才跑一次，
        // 按真实时间算进度就变成「冻结 160ms 然后一帧跳到 80%」——就是所谓的卡顿。
        if (!_pushFrameHooked)
        {
            System.Windows.Media.CompositionTarget.Rendering += OnPagePushFrame;
            _pushFrameHooked = true;
        }
    }

    /// <summary>每个渲染帧前更新一次偏移，位移与屏幕刷新严格对齐。</summary>
    private void OnPagePushFrame(object? sender, EventArgs e)
    {
        if (PageScroller is null) { StopPagePush(); return; }

        var elapsed = _pagePushClock.Elapsed.TotalMilliseconds;

        // WPF 偶尔在同一帧里连发两次 Rendering（间隔 1~2ms），
        // 两次都推进会让某帧位移翻倍，看上去就是「一顿」。间隔太近的丢掉。
        if (elapsed < PagePushMs && elapsed - _lastPushMs < 8.0) return;
        _lastPushMs = elapsed;

        var t = Math.Min(1.0, elapsed / PagePushMs);

        // 期间窗口尺寸变了：终点按最新页高重算
        var to = PageHost.ActualHeight > 0 ? _pageIndex * PageHost.ActualHeight : _pushTo;
        var offset = _pushFrom + (to - _pushFrom) * PagePushEase.Ease(t);

        PageScroller.ScrollToVerticalOffset(offset);

        if (t >= 1)
        {
            StopPagePush();
            PageScroller.ScrollToVerticalOffset(to);
        }
    }

    private void StopPagePush()
    {
        if (PushTrace && _stallSamples.Count > 0)
        {
            var sorted = _stallSamples.OrderBy(x => x).ToList();
            var p95 = sorted[(int)((sorted.Count - 1) * 0.95)];
            var worst = _stallSamples.IndexOf(sorted[^1]);
            TracePush($"stall n={sorted.Count} max={sorted[^1]:F1}ms@{_stallAt[worst]:F0}ms " +
                      $"p95={p95:F1}ms over16={sorted.Count(x => x > 16.7)}");
            _stallSamples.Clear();
            _stallAt.Clear();
        }

        FlushPushTrace();

        if (_peeking)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnPeekFrame;
            _peeking = false;
        }

        if (!_pushFrameHooked) return;
        System.Windows.Media.CompositionTarget.Rendering -= OnPagePushFrame;
        _pushFrameHooked = false;
        RunAfterPush();
    }

    /// <summary>窗口尺寸变了：按新页高把轨道校正回当前页（推动中由帧回调自行收敛）。</summary>
    private void OnPageHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PageScroller is null) return;

        if (_pushFrameHooked) return;
        var at = _pageIndex * e.NewSize.Height;
        PageScroller.ScrollToVerticalOffset(at);
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
        FadeTo(VersionSelectOverlay, 0, 1, 300);

        // ② 旧页面退场：侧栏完全滑出，页面只退一小段并淡出（视差 + 淡化，不硬推整屏）
        SlideTo(MainSidebarTf, -MainSidebarWidth, OpenMs);
        FadeTo(MainSidebar, 1, 0, 240);
        SlideTo(PageHostTf, PageSlideOffset, OpenMs);
        FadeTo(PageHost, 1, 0, 240);

        // ③ 新页面进场：侧栏与信息流同起同止，不做错峰（错峰看着像两块各走各的）
        SlideTo(VersionSelectView.SidebarTransform, 0, OpenMs,
                onCompleted: () => _versionSelectAnimating = false);
        SlideTo(VersionSelectView.ContentTransform, 0, OpenMs);
    }

    private void CloseVersionSelectUi()
    {
        if (VersionSelectOverlay.Visibility != Visibility.Visible || _versionSelectAnimating) return;

        _versionSelectAnimating = true;

        // 全程对称回退：新页面滑出、覆盖层淡出、旧页面淡回，三者同起同止
        SlideTo(VersionSelectView.SidebarTransform, -MainSidebarWidth, CloseMs);
        SlideTo(VersionSelectView.ContentTransform, PageSlideOffset, CloseMs);
        FadeTo(VersionSelectOverlay, 1, 0, CloseMs, onCompleted: () =>
        {
            VersionSelectOverlay.Visibility = Visibility.Collapsed;
            VersionSelectOverlay.BeginAnimation(OpacityProperty, null);
            VersionSelectOverlay.Opacity = 0;
            _versionSelectAnimating = false;
        });

        SlideTo(MainSidebarTf, 0, CloseMs);
        FadeTo(MainSidebar, 0, 1, 280);
        SlideTo(PageHostTf, 0, CloseMs);
        FadeTo(PageHost, 0, 1, 280, delayMs: 40);
    }

    private const double MainSidebarWidth = 180;

    /// <summary>开页 / 关页总时长。侧栏与信息流共用同一个值，保证同起同止。</summary>
    private const int OpenMs = 360;
    private const int CloseMs = 340;

    /// <summary>旧页面退场位移。只退一小段再淡出，比把整屏推出窗口柔和得多。</summary>
    private const double PageSlideOffset = 220;

    /// <summary>
    /// 页面级转场的缓动：cubic-bezier(0.2, 0, 0, 1)（Material「强调」曲线）。
    /// 起步柔和、中段有力、收尾绵长，大面积位移才不显得急。
    /// </summary>
    private static System.Windows.Media.Animation.EasingFunctionBase PageEase => new Animation.CubicBezierEase
    {
        ControlPoint1 = new Point(0.2, 0),
        ControlPoint2 = new Point(0, 1),
    };

    /// <summary>
    /// 页面推动专用的缓动：cubic-bezier(0.25, 0.1, 0.2, 1)。
    /// 不用 PageEase 那条：它的速度峰值挤在最前面（前 20% 时间就跑掉 50% 位移），
    /// 之后一段长尾慢慢挪，长位移下容易看成「一冲一顿」。
    /// 这条把速度峰值挪到 20%~30% 之间、衰减更平缓，逐帧位移变化更均匀。
    /// </summary>
    private static System.Windows.Media.Animation.EasingFunctionBase PagePushEase => new Animation.CubicBezierEase
    {
        ControlPoint1 = new Point(0.25, 0.1),
        ControlPoint2 = new Point(0.2, 1),
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

        // 关窗前解掉渲染帧回调，避免关窗过程中还在驱动轨道
        _afterPush.Clear();
        StopPagePush();

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
