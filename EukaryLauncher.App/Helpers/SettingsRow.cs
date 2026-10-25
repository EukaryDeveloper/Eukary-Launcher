using System.Windows;

namespace EukaryLauncher.App.Helpers;

/// <summary>
/// 信息流行（SettingsRow*）的附加参数：悬停高亮块的圆角。
/// 行是贴边填充的，只有贴合分组卡片圆角的那一侧才带圆角，
/// 具体（顶行 / 中间行 / 底行 / 单行组）由样式决定。
/// </summary>
public static class SettingsRow
{
    public static readonly DependencyProperty CornerProperty =
        DependencyProperty.RegisterAttached("Corner", typeof(CornerRadius), typeof(SettingsRow),
            new FrameworkPropertyMetadata(new CornerRadius(0)));

    public static void SetCorner(DependencyObject d, CornerRadius value) => d.SetValue(CornerProperty, value);
    public static CornerRadius GetCorner(DependencyObject d) => (CornerRadius)d.GetValue(CornerProperty);
}
