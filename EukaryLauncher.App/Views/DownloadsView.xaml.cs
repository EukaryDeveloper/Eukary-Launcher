using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EukaryLauncher.App.Animation;
using EukaryLauncher.App.Services;
using EukaryLauncher.App.ViewModels;

namespace EukaryLauncher.App.Views;

/// <summary>
/// 下载页容器：顶部 Tab + 下方内容区。
/// Tab 内容用两层交叉过渡（新页滑入淡入、旧页反向滑出淡出），绿色下划线是一根
/// 共享滑条，切换时从旧 tab 滑到新 tab；文字颜色渐变在 TabItem 模板里做。
/// </summary>
public partial class DownloadsView : UserControl
{
    private DownloadsViewModel? _vm;

    /// <summary>正在显示的层。</summary>
    private ContentControl _front;

    /// <summary>备用层：下一个要进来的页放这里，切完和前台对调。</summary>
    private ContentControl _standby;

    private int _lastIndex;

    /// <summary>一次 Tab 内容过渡的时长。</summary>
    private const int TabSwitchMs = 260;

    public DownloadsView()
    {
        InitializeComponent();
        _front = TabLayerOver;
        _standby = TabLayerUnder;

        DataContextChanged += (_, _) => HookViewModel();
        Loaded += (_, _) =>
        {
            HookViewModel();

            // 窗口宽度变化时 tab 位置会整体平移，滑条直接跟过去（不做动画）
            TabList.SizeChanged += (_, _) => PlaceUnderline(animated: false);
            PlaceUnderline(animated: false);
        };
    }

    private void HookViewModel()
    {
        if (ReferenceEquals(_vm, DataContext)) return;

        if (_vm is not null) _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as DownloadsViewModel;
        if (_vm is null) return;

        _lastIndex = _vm.SelectedTabIndex;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        if (_front.Content is null)
            _front.Content = _vm.CurrentContent;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadsViewModel.CurrentContent) || _vm is null) return;

        var content = _vm.CurrentContent;
        if (ReferenceEquals(content, _front.Content)) return;

        SwitchContent(content, _vm.SelectedTabIndex);
        PlaceUnderline(animated: true);
    }

    // ==================== Tab 内容交叉过渡 ====================

    private void SwitchContent(object content, int index)
    {
        var direction = Math.Sign(index - _lastIndex);
        _lastIndex = index;
        _transitionId++;

        // 上一次转场可能还在跑：先把两层归位，再开始新的
        FinishTransition();

        // 首次显示 / 系统关动效：直接换内容
        if (_front.Content is null || !AnimationHelper.IsEnabled)
        {
            _front.Content = content;
            return;
        }

        // 同一 Tab 内换深度（进加载器选择、进模组详情）没有左右方向，只做交叉淡化
        var slide = direction == 0 ? 0 : 28;

        _standby.Content = content;
        _standby.Visibility = Visibility.Visible;
        _standby.Opacity = 0;
        TransformOf(_standby).X = direction >= 0 ? slide : -slide;
        _front.Opacity = 1;
        TransformOf(_front).X = 0;

        var duration = new Duration(TimeSpan.FromMilliseconds(TabSwitchMs));
        var ease = new CubicBezierEase();

        _standby.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        TransformOf(_standby).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(TransformOf(_standby).X, 0, duration) { EasingFunction = ease });
        _front.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = ease });
        TransformOf(_front).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, direction >= 0 ? -slide : slide, duration) { EasingFunction = ease });

        // 旧页退完藏起来（视图实例留在层里，切回来直接复用，不用重建）
        var leaving = _front;
        var generation = _transitionId;
        var outAnimation = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
        outAnimation.Completed += (_, _) =>
        {
            // 中途被更快的点击接手的话，收层由新的转场负责，这里别动
            if (generation != _transitionId) return;
            leaving.Visibility = Visibility.Collapsed;
        };
        _front.BeginAnimation(OpacityProperty, outAnimation);

        (_front, _standby) = (_standby, _front);
    }

    /// <summary>转场代次：每开一次新转场 +1，用来识别「退场动画结束时转场已被接手」。</summary>
    private int _transitionId;

    /// <summary>把两层归位：前台完整显示，备用层收起（正在跑的动画一并停掉）。</summary>
    private void FinishTransition()
    {
        foreach (var (layer, tf) in new[] { (_front, TransformOf(_front)), (_standby, TransformOf(_standby)) })
        {
            layer.BeginAnimation(OpacityProperty, null);
            tf.BeginAnimation(TranslateTransform.XProperty, null);
        }

        _front.Opacity = 1;
        TransformOf(_front).X = 0;
        _front.Visibility = Visibility.Visible;
        _standby.Visibility = Visibility.Collapsed;
    }

    private static TranslateTransform TransformOf(ContentControl layer) => (TranslateTransform)layer.RenderTransform!;

    // ==================== 下划线滑条 ====================
    // 位置按选中项实测（TranslatePoint 相对 TabBar），宽度就是选中项宽度。

    private void PlaceUnderline(bool animated)
    {
        if (_vm is null || TabList.Items.Count == 0) return;

        var index = _vm.SelectedTabIndex;
        if (index < 0 || index >= TabList.Items.Count) return;
        if (TabList.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement item) return;

        var origin = item.TranslatePoint(new Point(0, item.ActualHeight), TabBar);
        MoveUnderline(origin.X, item.ActualWidth, animated);
    }

    private void MoveUnderline(double x, double width, bool animated)
    {
        if (!animated || !AnimationHelper.IsEnabled)
        {
            TabUnderline.BeginAnimation(WidthProperty, null);
            TabUnderlineTf.BeginAnimation(TranslateTransform.XProperty, null);
            TabUnderline.Width = width;
            TabUnderlineTf.X = x;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(TabSwitchMs));
        var ease = new CubicBezierEase();
        TabUnderline.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        TabUnderlineTf.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = ease });
    }
}
