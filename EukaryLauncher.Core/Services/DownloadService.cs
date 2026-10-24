using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace EukaryLauncher.Core.Services;

/// <summary>单个文件的下载进度。</summary>
public readonly record struct FileProgress(string FileName, long ReceivedBytes, long? TotalBytes);

/// <summary>整批下载的聚合进度。</summary>
public readonly record struct BatchProgress(
    int CompletedFiles,
    int TotalFiles,
    long ReceivedBytes,
    long? TotalBytes,
    string? CurrentFileName,
    double BytesPerSecond);

/// <summary>一个下载任务。</summary>
public sealed record DownloadTask(string Url, string DestinationPath, string? ExpectedSha1 = null, long? ExpectedSize = null)
{
    /// <summary>进度条上显示的名字。</summary>
    public string DisplayName { get; init; } = Path.GetFileName(DestinationPath);

    /// <summary>
    /// 备用地址。镜像上偶尔会有缺失或 403 的条目（比如 BMCLAPI 缺 oshi-core），
    /// 主源重试耗尽后依次改试这些地址。
    /// </summary>
    public IReadOnlyList<string> FallbackUrls { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 多线程下载器：SHA1 校验、失败重试（指数退避）、断点续传、进度回调。
/// </summary>
public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _http;
    private readonly int _maxRetries;

    public DownloadService(HttpClient? http = null, int maxRetries = 3)
    {
        _http = http ?? HttpClientFactory.Create();
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// 并发下载一批文件。任意一个失败都会抛出（重试耗尽后）。
    /// </summary>
    public async Task DownloadBatchAsync(
        IEnumerable<DownloadTask> tasks,
        int maxParallelism,
        IProgress<BatchProgress>? progress = null,
        CancellationToken ct = default)
    {
        var all = tasks.ToList();
        var completed = 0;
        long receivedTotal = 0;
        long? totalBytes = all.All(t => t.ExpectedSize.HasValue)
            ? all.Sum(t => t.ExpectedSize!.Value)
            : null;
        var sw = Stopwatch.StartNew();
        var gate = new object();
        string? currentFile = null;

        void Report()
        {
            int done;
            lock (gate) { done = completed; }
            var recv = Interlocked.Read(ref receivedTotal);
            var elapsed = sw.Elapsed.TotalSeconds;
            string? name;
            lock (gate) { name = currentFile; }
            progress?.Report(new BatchProgress(
                done, all.Count, recv, totalBytes, name,
                elapsed > 0 ? recv / elapsed : 0));
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxParallelism),
            CancellationToken = ct,
        };

        var failures = new ConcurrentBag<KeyValuePair<string, Exception>>();

        await Parallel.ForEachAsync(all, options, async (task, token) =>
        {
            long lastReported = 0;

            var fileProgress = new Progress<FileProgress>(p =>
            {
                long delta;
                lock (gate)
                {
                    delta = p.ReceivedBytes - lastReported;
                    lastReported = p.ReceivedBytes;
                    currentFile = p.FileName;
                }
                if (delta > 0) Interlocked.Add(ref receivedTotal, delta);
                Report();
            });

            try
            {
                await DownloadFileAsync(task, fileProgress, token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单个文件失败不拖垮整批（资产文件上千个，坏一个就中止太脆弱）
                failures.Add(new(task.DisplayName, ex));
            }

            lock (gate)
            {
                completed++;
                currentFile = task.DisplayName;
            }
            Report();
        });

        if (!failures.IsEmpty)
        {
            throw new AggregateException(
                $"{failures.Count} 个文件下载失败",
                failures.Select(f => new IOException($"{f.Key}: {f.Value.Message}", f.Value)));
        }
    }

    /// <summary>
    /// 下载单个文件到目标路径。下载到 &lt;path&gt;.part，完成后校验 SHA1 再改名。
    /// </summary>
    public async Task DownloadFileAsync(
        DownloadTask task,
        IProgress<FileProgress>? progress = null,
        CancellationToken ct = default)
    {
        var dest = task.DestinationPath;
        var part = dest + ".part";

        var directory = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // 已存在且哈希正确 → 跳过
        if (File.Exists(dest) && await IsHashValidAsync(dest, task.ExpectedSha1, ct))
        {
            progress?.Report(new FileProgress(task.DisplayName, task.ExpectedSize ?? 0, task.ExpectedSize));
            return;
        }

        // 主源 + 备用源依次尝试；每个源内部做指数退避重试
        var urls = new List<string>(task.FallbackUrls.Count + 1) { task.Url };
        urls.AddRange(task.FallbackUrls);

        Exception? lastError = null;

        foreach (var url in urls)
        {
            for (var attempt = 0; attempt <= _maxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadOnceAsync(url, part, task.DisplayName, task.ExpectedSize, progress, ct);

                    if (await IsHashValidAsync(part, task.ExpectedSha1, ct))
                    {
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(part, dest, overwrite: true);
                        return;
                    }

                    // 哈希不对 → 删掉重下
                    TryDelete(part);
                    lastError = new IOException($"文件 {task.DisplayName} 校验失败（SHA1 不匹配）。");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 注意：网络异常不要删 .part——这正是断点续传存在的意义。
                    // 只在哈希校验失败时才删（见上面）。
                    lastError = ex;
                }

                if (attempt < _maxRetries)
                {
                    // 指数退避：1s / 2s / 4s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { throw; }
                }
            }
        }

        throw new IOException($"下载失败：{task.DisplayName}（已尝试 {urls.Count} 个源）", lastError);
    }

    private async Task DownloadOnceAsync(
        string url,
        string partPath,
        string displayName,
        long? expectedSize,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            existing = 0; // 服务端不支持续传，重头下
        }
        else if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            // 续传成功，保留已有内容
        }
        else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            existing = 0;
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        var total = response.Content.Headers.ContentLength;
        var totalOverall = total.HasValue ? existing + total.Value : (expectedSize ?? existing);

        var mode = existing > 0 ? FileMode.Open : FileMode.Create;
        await using var file = new FileStream(partPath, mode, FileAccess.ReadWrite, FileShare.None);
        if (existing > 0) file.Seek(existing, SeekOrigin.Begin);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long written = existing;
        long lastReport = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (written - lastReport >= 256 * 1024)
            {
                lastReport = written;
                progress?.Report(new FileProgress(displayName, written, totalOverall));
            }
        }

        await file.FlushAsync(ct);
        progress?.Report(new FileProgress(displayName, written, totalOverall));
    }

    private static async Task<bool> IsHashValidAsync(string path, string? expectedSha1, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1)) return true;
        if (!File.Exists(path)) return false;

        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
            var hash = await SHA1.HashDataAsync(fs, ct);
            return Convert.ToHexString(hash).Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略清理失败 */ }
    }

    public void Dispose() => _http.Dispose();
}

public static class HttpClientFactory
{
    public const string UserAgent = "EukaryLauncher/0.1.0 (+https://github.com/eukary-launcher/EukaryLauncher)";

    public static HttpClient Create()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            // 卡死连接最多挂 5 分钟。别设太长——一旦停滞，靠断点续传重连比干等快得多。
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }
}
