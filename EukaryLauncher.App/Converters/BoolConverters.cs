using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EukaryLauncher.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>字符串为空 / 列表为空时隐藏——卡片上「可选信息」那一类元素用它。</summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        null => Visibility.Collapsed,
        string s => string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible,
        int count => count == 0 ? Visibility.Collapsed : Visibility.Visible,
        System.Collections.ICollection list => list.Count == 0 ? Visibility.Collapsed : Visibility.Visible,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>按枚举值选刷子：加载器状态 / 任务状态的颜色都走它，UI 里不写死色值。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object? True { get; set; }
    public object? False { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value, parameter) switch
        {
            (null, _) => False ?? DependencyProperty.UnsetValue,
            (_, null) => (value is true ? True : False) ?? DependencyProperty.UnsetValue,
            _ => (string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
                ? True
                : False) ?? DependencyProperty.UnsetValue,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
