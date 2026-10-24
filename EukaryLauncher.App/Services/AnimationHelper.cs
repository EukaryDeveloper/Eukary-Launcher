using Microsoft.Win32;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 设计图纸要求：所有动效尊重系统"关闭动画"的无障碍设置。
/// WPF 没有直接暴露这个开关，读注册表 MinAnimate。
/// </summary>
public static class AnimationHelper
{
    private static readonly Lazy<bool> _enabled = new(ReadSystemSetting);

    public static bool IsEnabled => _enabled.Value;

    private static bool ReadSystemSetting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics");
            var value = key?.GetValue("MinAnimate")?.ToString();
            // "0" 表示用户在系统里关掉了动画
            return value != "0";
        }
        catch
        {
            return true;
        }
    }
}
