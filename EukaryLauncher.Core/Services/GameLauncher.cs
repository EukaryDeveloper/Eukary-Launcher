using System.Diagnostics;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// 真正把游戏跑起来。
/// 关键点：用 ArgumentList 数组传参（不要拼字符串），WorkingDirectory 指向游戏目录，
/// stdout/stderr 异步转发给调用方。
/// </summary>
public sealed class GameLauncher
{
    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    private Process? _process;
    private readonly object _gate = new();

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>拼好的完整命令行（用于调试展示，不用于实际启动）。</summary>
    public string BuildCommandPreview(VersionJson version, GamePaths paths, LaunchOptions options)
    {
        var java = ResolveJava(options.JavaPath);
        var classPath = string.Join(ArgumentBuilder.ClassPathSeparator,
            ArgumentBuilder.BuildClassPath(version, paths, options.VersionId));
        var jvm = ArgumentBuilder.BuildJvmArguments(version, paths, options, classPath, FindLogConfig(version, paths));
        var game = ArgumentBuilder.BuildGameArguments(version, paths, options);

        var all = new List<string> { $"\"{java}\"" };
        all.AddRange(jvm);
        all.Add(version.MainClass);
        all.AddRange(game);
        return string.Join(" ", all);
    }

    public Process Start(VersionJson version, GamePaths paths, LaunchOptions options)
    {
        var java = ResolveJava(options.JavaPath);

        var classPath = string.Join(ArgumentBuilder.ClassPathSeparator,
            ArgumentBuilder.BuildClassPath(version, paths, options.VersionId));
        var logConfig = FindLogConfig(version, paths);

        var jvmArgs = ArgumentBuilder.BuildJvmArguments(version, paths, options, classPath, logConfig);
        var gameArgs = ArgumentBuilder.BuildGameArguments(version, paths, options);

        var psi = new ProcessStartInfo(java)
        {
            WorkingDirectory = paths.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };

        psi.ArgumentList.Add("-Xmx" + options.MaxMemoryMb + "M");
        foreach (var a in jvmArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(version.MainClass);
        foreach (var a in gameArgs) psi.ArgumentList.Add(a);

        Directory.CreateDirectory(paths.Root);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        process.Exited += (_, _) => Exited?.Invoke(SafeExitCode(process));

        if (!process.Start())
            throw new InvalidOperationException("Java 进程启动失败。");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_gate) _process = process;
        return process;
    }

    public static string ResolveJava(string javaPath)
    {
        // 用户可能选了 javaw.exe（无控制台），但我们需要捕获 stdout，换成同目录的 java.exe
        var dir = Path.GetDirectoryName(javaPath);
        var name = Path.GetFileNameWithoutExtension(javaPath);
        if (string.Equals(name, "javaw", StringComparison.OrdinalIgnoreCase) && dir is { Length: > 0 })
        {
            var sibling = Path.Combine(dir, "java.exe");
            if (File.Exists(sibling)) return sibling;
        }
        return javaPath;
    }

    private static string? FindLogConfig(VersionJson version, GamePaths paths)
    {
        var log = version.Logging?.Client?.File;
        if (log is null) return null;

        var fileName = log.Path is { Length: > 0 } p
            ? Path.GetFileName(p)
            : Path.GetFileName(new Uri(log.Url ?? "").LocalPath);

        if (string.IsNullOrEmpty(fileName)) return null;

        var full = Path.Combine(paths.LogConfigsDir, fileName);
        return File.Exists(full) ? full : null;
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
