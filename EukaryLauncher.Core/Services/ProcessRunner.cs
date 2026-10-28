using System.Diagnostics;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// 跑外部进程。Forge / NeoForge / OptiFine 的安装器都是 jar，只能用 Java 跑，
/// 而它们的进度只往控制台打、没有别的接口，所以输出要逐行转发出去。
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// 启动进程并等到它结束，返回退出码。
    /// 输出（stdout + stderr）逐行交给 <paramref name="onOutput"/>；
    /// 取消时杀掉整个进程树。
    /// </summary>
    public static async Task<int> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutput = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        // 参数一律走 ArgumentList，不拼字符串——路径里有空格时才不会散架
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var finished = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Relay(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            onOutput?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => Relay(e.Data);
        process.ErrorDataReceived += (_, e) => Relay(e.Data);
        process.Exited += (_, _) => finished.TrySetResult(SafeExitCode(process));

        if (!process.Start())
            throw new IOException($"无法启动进程：{psi.FileName}");

        // 关掉 stdin，免得安装器卡在等键盘输入
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程已经没了，忽略
            }
        });

        return await finished.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    /// <summary>把失败原因说清楚：退出码 + 最后几行输出（安装器真正的报错都在最后）。</summary>
    public static string DescribeFailure(string what, int exitCode, IEnumerable<string> lines)
    {
        var tail = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .TakeLast(3)
            .ToList();

        var head = $"{what}失败（退出码 {exitCode}）";
        return tail.Count == 0 ? head : $"{head}：{string.Join(" | ", tail)}";
    }
}
