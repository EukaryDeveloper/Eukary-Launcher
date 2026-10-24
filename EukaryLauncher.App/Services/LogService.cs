using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Message)
{
    public string LevelText => Level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };

    public string Display => $"[{Time:HH:mm:ss}] {LevelText} {Message}";
}

/// <summary>
/// 极简日志：内存环形缓冲（给界面绑定）+ 落盘文件。
/// 没有引入 Serilog，启动器现阶段不需要结构化日志那套重量级能力。
/// </summary>
public sealed class LogService
{
    private const int MaxEntries = 2000;

    private readonly ObservableCollection<LogEntry> _entries = new();
    private readonly object _gate = new();
    private StreamWriter? _file;

    public static LogService Instance { get; } = new();

    public ObservableCollection<LogEntry> Entries => _entries;

    private LogService()
    {
        try
        {
            var path = Path.Combine(GamePaths.LogsRoot, $"launcher-{DateTime.Now:yyyyMMdd}.log");
            _file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true,
            };
        }
        catch
        {
            _file = null;
        }
    }

    public void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);

        lock (_gate)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }

        try { _file?.WriteLine(entry.Display); } catch { /* 日志写失败不能影响主流程 */ }
    }

    public void Debug(string m) => Add(LogLevel.Debug, m);
    public void Info(string m) => Add(LogLevel.Info, m);
    public void Warn(string m) => Add(LogLevel.Warn, m);
    public void Error(string m) => Add(LogLevel.Error, m);

    /// <summary>游戏进程输出直接进同一条流，标记为 Debug 避免污染等级统计。</summary>
    public void GameOutput(string line) => Add(LogLevel.Debug, line);

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
