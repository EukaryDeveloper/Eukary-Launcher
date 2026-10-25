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

        // 落盘与线程无关，先写掉；写失败不能影响主流程
        try { _file?.WriteLine(entry.Display); } catch { /* 日志写失败不能影响主流程 */ }

        // Entries 绑定在日志页的 ItemsControl 上，只能在 UI 线程修改。
        // 后台线程打日志很常见（下载、删除版本、游戏输出）——跨线程 Add 会把
        // ItemsControl 的内部计数搞乱，之后 UI 一刷新就抛
        // "ItemsControl 与其项源不一致"，且会在全局异常处理器里二次引爆进程。
        var dispatcher = AppServices.UiDispatcher;
        if (dispatcher is null)
            AddToEntries(entry);            // 启动早期还没有 Dispatcher，此时 UI 未渲染，直接加
        else
            dispatcher.BeginInvoke(() => AddToEntries(entry));
    }

    private void AddToEntries(LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
    }

    public void Debug(string m) => Add(LogLevel.Debug, m);
    public void Info(string m) => Add(LogLevel.Info, m);
    public void Warn(string m) => Add(LogLevel.Warn, m);
    public void Error(string m) => Add(LogLevel.Error, m);

    /// <summary>游戏进程输出直接进同一条流，标记为 Debug 避免污染等级统计。</summary>
    public void GameOutput(string line) => Add(LogLevel.Debug, line);

    public void Clear()
    {
        var dispatcher = AppServices.UiDispatcher;
        if (dispatcher is null)
            lock (_gate) _entries.Clear();
        else
            dispatcher.BeginInvoke(() =>
            {
                lock (_gate) _entries.Clear();
            });
    }
}
