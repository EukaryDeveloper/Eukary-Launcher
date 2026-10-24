using System.Windows;
using System.Windows.Media.Animation;

namespace EukaryLauncher.App.Animation;

/// <summary>
/// 设计图纸规定全站统一使用 cubic-bezier(0.2, 0.8, 0.2, 1)。
/// WPF 内置缓动函数没有这条曲线，这里自己实现一个，保证和图纸一致。
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty ControlPoint1Property =
        DependencyProperty.Register(nameof(ControlPoint1), typeof(Point), typeof(CubicBezierEase),
            new PropertyMetadata(new Point(0.2, 0.8)));

    public static readonly DependencyProperty ControlPoint2Property =
        DependencyProperty.Register(nameof(ControlPoint2), typeof(Point), typeof(CubicBezierEase),
            new PropertyMetadata(new Point(0.2, 1.0)));

    public Point ControlPoint1
    {
        get => (Point)GetValue(ControlPoint1Property);
        set => SetValue(ControlPoint1Property, value);
    }

    public Point ControlPoint2
    {
        get => (Point)GetValue(ControlPoint2Property);
        set => SetValue(ControlPoint2Property, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;

        // 先解出参数 t 使得 x(t) = normalizedTime（牛顿迭代，最多 8 次）
        var t = normalizedTime;
        for (var i = 0; i < 8; i++)
        {
            var dx = Bezier(t, ControlPoint1.X, ControlPoint2.X) - normalizedTime;
            if (Math.Abs(dx) < 1e-6) break;

            var slope = Slope(t, ControlPoint1.X, ControlPoint2.X);
            if (Math.Abs(slope) < 1e-6) break;

            t -= dx / slope;
        }

        t = Math.Clamp(t, 0, 1);
        return Bezier(t, ControlPoint1.Y, ControlPoint2.Y);
    }

    /// <summary>三次贝塞尔分量，起点 (0,0) 终点 (1,1)。</summary>
    private static double Bezier(double t, double p1, double p2)
    {
        var mt = 1 - t;
        return 3 * mt * mt * t * p1 + 3 * mt * t * t * p2 + t * t * t;
    }

    /// <summary>dx/dt。</summary>
    private static double Slope(double t, double p1, double p2)
    {
        var mt = 1 - t;
        return 3 * mt * mt * p1 + 6 * mt * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();
}
