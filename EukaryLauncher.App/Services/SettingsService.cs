using System.Text.Json;
using System.Text.Json.Serialization;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>可持久化的启动器设置。</summary>
public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = GamePaths.DefaultRoot;
    public string MirrorId { get; set; } = "bmclapi";
    public string PlayerName { get; set; } = "Player";
    public int MaxMemoryMb { get; set; } = 2048;
    public string LastVersionId { get; set; } = "";
    public string? CustomJavaPath { get; set; }
    public bool AutoManageJava { get; set; } = true;
    public int DownloadThreads { get; set; } = 8;

    /// <summary>游戏启动后启动器窗口的行为。</summary>
    public LauncherVisibility LauncherVisibility { get; set; } = LauncherVisibility.Unchanged;

    /// <summary>版本隔离策略：决定新安装的版本是否用 versions/&lt;id&gt;/ 作独立游戏目录。</summary>
    public VersionIsolation VersionIsolation { get; set; } = VersionIsolation.All;

    /// <summary>旧版本的开关字段，仅用于迁移：false 曾表示“游戏启动后关闭启动器”。</summary>
    public bool KeepLauncherOpen { get; set; } = true;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool UseCustomResolution { get; set; } = false;
    public string? ExtraJvmArgs { get; set; }

    /// <summary>默认文件夹的显示名称（版本选择页可改，不动磁盘路径）。</summary>
    public string DefaultFolderName { get; set; } = "默认文件夹";

    /// <summary>额外的游戏文件夹（版本选择页多文件夹支持；默认文件夹不在此列）。</summary>
    public List<ExtraGameFolder> ExtraFolders { get; set; } = new();
}

/// <summary>版本选择页「文件夹列表」里的一个额外游戏文件夹。</summary>
public sealed class ExtraGameFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public LauncherSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(GamePaths.SettingsPath))
            {
                var text = File.ReadAllText(GamePaths.SettingsPath);
                Current = JsonSerializer.Deserialize<LauncherSettings>(text, Options) ?? new LauncherSettings();

                // 迁移：旧版只有“游戏启动后保持启动器运行”开关，false 即“启动后关闭”
                if (!Current.KeepLauncherOpen)
                {
                    Current.LauncherVisibility = LauncherVisibility.CloseImmediately;
                    Current.KeepLauncherOpen = true;
                    Save();
                }
                return;
            }
        }
        catch
        {
            // 配置文件损坏就退回默认值
        }

        // 第一次启动：按物理内存给一个合理的默认值
        Current = new LauncherSettings
        {
            MaxMemoryMb = RecommendMemoryMb(),
        };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GamePaths.SettingsPath)!);
            File.WriteAllText(GamePaths.SettingsPath, JsonSerializer.Serialize(Current, Options));
        }
        catch
        {
            // 设置保存失败不阻塞使用
        }
    }

    public static int RecommendMemoryMb()
    {
        try
        {
            if (NativeMethods.GlobalMemoryStatusEx(out var totalBytes) && totalBytes > 0)
            {
                var totalMb = (long)(totalBytes / 1024 / 1024);
                return totalMb switch
                {
                    >= 32768 => 8192,
                    >= 16384 => 4096,
                    >= 8192 => 3072,
                    _ => 2048,
                };
            }
        }
        catch
        {
            // 拿不到物理内存就用保守值
        }

        return 2048;
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public void Init() => dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatusEx));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    public static bool GlobalMemoryStatusEx(out ulong totalPhysBytes)
    {
        var status = new MemoryStatusEx();
        status.Init();
        var ok = GlobalMemoryStatusEx(ref status);
        totalPhysBytes = ok ? status.ullTotalPhys : 0;
        return ok;
    }
}
