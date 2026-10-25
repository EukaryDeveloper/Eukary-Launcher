namespace EukaryLauncher.App.Services;

/// <summary>界面上的数字与日期显示，统一在这里格式化。</summary>
public static class Humanize
{
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    /// <summary>Modrinth 的下载量：10.2M / 833.4K。</summary>
    public static string Count(int count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
        >= 1_000 => $"{count / 1_000.0:0.#}K",
        _ => count.ToString(),
    };

    public static string Date(DateTimeOffset time)
    {
        var local = time.LocalDateTime;
        var days = (DateTime.Now - local).TotalDays;
        return days switch
        {
            < 1 => "今天",
            < 2 => "昨天",
            < 30 => $"{(int)days} 天前",
            < 365 => $"{(int)(days / 30)} 个月前",
            _ => $"{(int)(days / 365)} 年前",
        };
    }
}
