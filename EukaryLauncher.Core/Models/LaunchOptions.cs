namespace EukaryLauncher.Core.Models;

/// <summary>
/// 一次启动需要的所有外部输入。Core 只接受这个 DTO，不认识任何 UI 类型。
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>.minecraft 根目录（游戏目录）。</summary>
    public required string GameDirectory { get; init; }

    /// <summary>版本 id，如 1.20.4。</summary>
    public required string VersionId { get; init; }

    public required string PlayerName { get; init; }

    /// <summary>带短横线的 UUID，离线模式由用户名推导。</summary>
    public required string PlayerUuid { get; init; }

    /// <summary>微软 token 或离线模式的假 token。</summary>
    public required string AccessToken { get; init; }

    /// <summary>msa（微软）/ legacy（离线）/ mojang。</summary>
    public string UserType { get; init; } = "legacy";

    /// <summary>最大内存（MB）。</summary>
    public int MaxMemoryMb { get; init; } = 2048;

    /// <summary>javaw.exe / java.exe 的完整路径。</summary>
    public required string JavaPath { get; init; }

    public int WindowWidth { get; init; } = 854;
    public int WindowHeight { get; init; } = 480;

    /// <summary>true 时使用独立窗口尺寸参数（影响 has_custom_resolution 规则）。</summary>
    public bool UseCustomResolution { get; init; }

    /// <summary>额外的 JVM 参数（用户自定义）。</summary>
    public string? ExtraJvmArgs { get; init; }

    /// <summary>启动后是否保持启动器运行。</summary>
    public bool KeepLauncherOpen { get; init; } = true;
}

/// <summary>
/// 离线账号会话。UUID 用 Java 的 UUID.nameUUIDFromBytes("OfflinePlayer:&lt;name&gt;".getBytes()) 算法，
/// 注意 .NET 的 Guid 前三个字段是小端，必须手工拼字符串，不能用 new Guid(bytes)。
/// </summary>
public static class OfflineAccount
{
    public static string ComputeUuid(string playerName)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}");
        var hash = System.Security.Cryptography.MD5.HashData(input);

        // 置为 UUID v3（RFC 4122）
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return string.Concat(
            Hex(hash[..4]), "-", Hex(hash[4..6]), "-", Hex(hash[6..8]), "-",
            Hex(hash[8..10]), "-", Hex(hash[10..16]));
    }

    private static string Hex(ReadOnlySpan<byte> bytes)
    {
        const string digits = "0123456789abcdef";
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = digits[bytes[i] >> 4];
            chars[i * 2 + 1] = digits[bytes[i] & 0x0F];
        }
        return new string(chars);
    }

    public static LaunchOptions CreateLaunchOptions(
        string gameDirectory,
        string versionId,
        string playerName,
        string javaPath,
        int maxMemoryMb) => new()
    {
        GameDirectory = gameDirectory,
        VersionId = versionId,
        PlayerName = playerName,
        PlayerUuid = ComputeUuid(playerName),
        AccessToken = Guid.NewGuid().ToString("N"),
        UserType = "legacy",
        JavaPath = javaPath,
        MaxMemoryMb = maxMemoryMb,
    };
}
