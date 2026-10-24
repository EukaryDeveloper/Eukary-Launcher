using System.Text.Json;
using System.Text.Json.Serialization;
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
    public bool KeepLauncherOpen { get; set; } = true;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public bool UseCustomResolution { get; set; } = false;
    public string? ExtraJvmArgs { get; set; }
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
