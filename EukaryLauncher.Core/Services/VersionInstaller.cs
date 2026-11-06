using System.IO.Compression;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

public enum InstallStage
{
    VersionJson,
    ClientJar,
    Libraries,
    Natives,
    AssetIndex,
    Assets,
    Logging,
    Done,
}

public readonly record struct InstallProgress(
    InstallStage Stage,
    string Message,
    int CompletedFiles,
    int TotalFiles,
    long ReceivedBytes,
    long? TotalBytes,
    double BytesPerSecond);

/// <summary>
/// 把一个版本完整装好：版本 JSON → 主 jar → libraries → natives → 资产索引 → 资产 → 日志配置。
/// </summary>
public sealed class VersionInstaller
{
    private readonly GamePaths _paths;
    private readonly IMirrorService _mirror;
    private readonly IMirrorService _fallback;
    private readonly DownloadService _downloader;
    private readonly HttpClient _http;

    public VersionInstaller(GamePaths paths, IMirrorService mirror, HttpClient? http = null)
    {
        _paths = paths;
        _mirror = mirror;
        // 镜像偶尔会缺条目或返回 403（BMCLAPI 上的 oshi-core 就是一例），
        // 官方源必须留着兜底——这也是开发指南里"保留官方源兜底"的直接原因。
        _fallback = MirrorFactory.OppositeOf(mirror.Id);
        _http = http ?? HttpClientFactory.Create();
        _downloader = new DownloadService(_http);
    }

    /// <summary>库文件并发数（可从设置注入）。</summary>
    public int LibraryParallelism { get; set; } = 8;

    /// <summary>资产文件并发数。小文件多，线程可以更多。</summary>
    public int AssetParallelism { get; set; } = 16;

    /// <summary>从版本清单里取一个版本对应的清单项。</summary>
    public async Task<VersionManifest> FetchManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var cachePath = Path.Combine(GamePaths.AppDataRoot, "version_manifest_v2.json");
        var cacheFile = new FileInfo(cachePath);

        if (!forceRefresh && cacheFile.Exists && DateTime.UtcNow - cacheFile.LastWriteTimeUtc < TimeSpan.FromHours(6))
        {
            try
            {
                var cached = await File.ReadAllTextAsync(cachePath, ct);
                var parsed = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(cached, JsonOptions.Default);
                if (parsed is { Versions.Count: > 0 }) return parsed;
            }
            catch { /* 缓存坏了就重新下 */ }
        }

        var url = _mirror.GetVersionManifestUrl();
        var json = await _http.GetStringAsync(url, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, json, ct);

        return System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions.Default)
               ?? new VersionManifest();
    }

    public async Task<VersionJson> FetchVersionJsonAsync(
        VersionManifestEntry entry,
        CancellationToken ct = default)
    {
        var dest = _paths.VersionJsonPath(entry.Id);
        var url = _mirror.GetVersionJsonUrl(entry.Id, entry.Url);
        var fallbackUrl = _fallback.GetVersionJsonUrl(entry.Id, entry.Url);

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        string json;
        try
        {
            json = await _http.GetStringAsync(url, ct);
        }
        catch (Exception) when (!string.Equals(url, fallbackUrl, StringComparison.Ordinal))
        {
            json = await _http.GetStringAsync(fallbackUrl, ct);
        }

        await File.WriteAllTextAsync(dest, json, ct);

        return System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json, JsonOptions.Default)
               ?? throw new InvalidOperationException($"版本 {entry.Id} 的 JSON 解析失败。");
    }

    /// <summary>按版本 id 安装（内部先查清单拿到 URL）。</summary>
    public async Task<VersionJson> InstallAsync(
        string versionId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await FetchManifestAsync(false, ct);
        var entry = manifest.Versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"版本清单里找不到 {versionId}。");
        return await InstallAsync(entry, progress, ct);
    }

    /// <summary>
    /// 完整安装一个版本。返回合并后的版本 JSON。
    /// </summary>
    public async Task<VersionJson> InstallAsync(
        VersionManifestEntry entry,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        void Report(InstallStage stage, string msg, int done = 0, int total = 0, long recv = 0, long? totalBytes = null, double speed = 0)
            => progress?.Report(new InstallProgress(stage, msg, done, total, recv, totalBytes, speed));

        // 版本 JSON 和主 jar 是最先落盘的两样东西。装到一半失败时若不清理，
        // ListInstalled()（按 json + jar 判定）会把半成品当成已安装——
        // 重启启动器后它照样出现在版本列表里，但依赖库残缺，一启动就崩。
        // 回滚仅针对本次新装的版本；开始前就已完整的（重装/修复）不动。
        var wasComplete = File.Exists(_paths.VersionJsonPath(entry.Id))
                       && File.Exists(_paths.VersionJarPath(entry.Id));

        try
        {
            return await InstallAsyncCore(entry, progress, ct, Report);
        }
        catch
        {
            if (!wasComplete) TryDeleteVersionDir(entry.Id);
            throw;
        }
    }

    private async Task<VersionJson> InstallAsyncCore(
        VersionManifestEntry entry,
        IProgress<InstallProgress>? progress,
        CancellationToken ct,
        Action<InstallStage, string, int, int, long, long?, double> report)
    {
        // 1. 版本 JSON + inheritsFrom 链
        report(InstallStage.VersionJson, $"解析版本 {entry.Id}", 0, 0, 0, null, 0);
        var version = await FetchVersionJsonAsync(entry, ct);

        var parentCache = new Dictionary<string, VersionJson>(StringComparer.Ordinal);
        version = VersionMerger.Merge(version, parentId =>
        {
            if (parentCache.TryGetValue(parentId, out var cached)) return cached;
            var path = _paths.VersionJsonPath(parentId);
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var parsed = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(text, JsonOptions.Default);
                parentCache[parentId] = parsed!;
                return parsed;
            }
            // 父版本不在本地，从清单里找它的 URL 下载
            var manifest = FetchManifestAsync(false, ct).GetAwaiter().GetResult();
            var parentEntry = manifest.Versions.FirstOrDefault(v => v.Id == parentId);
            if (parentEntry is null) return null;
            var downloaded = FetchVersionJsonAsync(parentEntry, ct).GetAwaiter().GetResult();
            parentCache[parentId] = downloaded;
            return downloaded;
        });

        return await InstallCoreAsync(version, entry.Id, progress, ct, report);
    }

    /// <summary>把一份版本 JSON 落到 versions/&lt;versionId&gt;/&lt;versionId&gt;.json。</summary>
    public async Task SaveVersionJsonAsync(string versionId, VersionJson version, CancellationToken ct = default)
    {
        var dest = _paths.VersionJsonPath(versionId);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var json = System.Text.Json.JsonSerializer.Serialize(version, JsonOptions.Default);
        await File.WriteAllTextAsync(dest, json, ct);
    }

    /// <summary>
    /// 按给定的（已合并的）版本 JSON 安装到 versionId 目录。
    /// 官方清单走 <see cref="InstallAsync(EukaryLauncher.Core.Models.VersionManifestEntry,System.IProgress{EukaryLauncher.Core.Services.InstallProgress}?,System.Threading.CancellationToken)"/>，
    /// Fabric / Forge 这类自带版本 JSON 的加载器直接调这里。
    /// </summary>
    public async Task<VersionJson> InstallCoreAsync(
        VersionJson version,
        string versionId,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default,
        Action<InstallStage, string, int, int, long, long?, double>? reporter = null)
    {
        void Report(InstallStage stage, string msg, int done = 0, int total = 0, long recv = 0, long? totalBytes = null, double speed = 0)
        {
            progress?.Report(new InstallProgress(stage, msg, done, total, recv, totalBytes, speed));
            reporter?.Invoke(stage, msg, done, total, recv, totalBytes, speed);
        }


        // 2. 主 jar。加载器自带的版本 JSON（Fabric 之类）没有 client 下载项，跳过即可——
        //    它会继承原版的主 jar，由调用方负责把 jar 放到位。
        if (version.ClientDownload is { } client && !string.IsNullOrEmpty(client.Url))
        {
            Report(InstallStage.ClientJar, $"下载 {versionId}.jar");
            var jarUrl = _mirror.GetClientJarUrl(versionId, client.Url);
            await _downloader.DownloadFileAsync(
                new DownloadTask(jarUrl, _paths.VersionJarPath(versionId), client.Sha1, client.Size)
                {
                    DisplayName = $"{versionId}.jar",
                    FallbackUrls = new[] { _fallback.GetClientJarUrl(versionId, client.Url) },
                },
                new Progress<FileProgress>(p => Report(InstallStage.ClientJar, $"下载 {p.FileName}",
                    0, 1, p.ReceivedBytes, p.TotalBytes)),
                ct);
        }

        // 3. libraries
        var libs = version.Libraries
            .Where(l => RuleEvaluator.IsAllowed(l.Rules))
            .ToList();

        var libTasks = new List<DownloadTask>();
        foreach (var lib in libs)
        {
            var artifact = lib.Downloads?.Artifact;
            if (artifact is { } a && !string.IsNullOrEmpty(a.Url))
            {
                // Mojang 官方格式：downloads.artifact 里带完整 URL 与校验值
                var mavenPath = a.Path ?? MavenPathFromName(lib.Name);
                libTasks.Add(new DownloadTask(
                    _mirror.GetLibraryUrl(a.Url, mavenPath),
                    Path.Combine(_paths.LibrariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar)),
                    a.Sha1, a.Size)
                {
                    DisplayName = Path.GetFileName(mavenPath),
                    FallbackUrls = new[] { _fallback.GetLibraryUrl(a.Url, mavenPath) },
                });
            }
            else if (lib.Downloads?.Classifiers is not null)
            {
                // natives-only 库（旧版的 lwjgl-platform / jinput-platform 就是这类）：
                // 官方上游只有带 classifier 的 jar，根本没有裸 artifact，
                // 走下面的 maven 坐标兜底必然 404、还会把整批安装拖死。
                // 它真正需要的是循环末尾单独处理的 natives jar，这里跳过即可。
            }
            else
            {
                // Fabric / Forge 的 maven 坐标格式：只有 name + 仓库基址，按坐标拼 URL。
                // 这种来源大多没有 SHA1，只能靠 HTTP 成功与否兜底。
                var mavenPath = MavenPathFromName(lib.Name);
                var baseUrl = string.IsNullOrWhiteSpace(lib.Url)
                    ? "https://libraries.minecraft.net/"
                    : lib.Url;
                if (!baseUrl.EndsWith('/')) baseUrl += '/';
                var url = baseUrl + mavenPath;

                libTasks.Add(new DownloadTask(
                    _mirror.GetLibraryUrl(url, mavenPath),
                    Path.Combine(_paths.LibrariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar)),
                    lib.Sha1, null)
                {
                    DisplayName = Path.GetFileName(mavenPath),
                    FallbackUrls = new[] { url },
                });
            }

            // natives jar
            if (lib.Natives is not null &&
                lib.Natives.TryGetValue(RuleEvaluator.CurrentOsName, out var classifier) &&
                lib.Downloads?.Classifiers is not null &&
                lib.Downloads.Classifiers.TryGetValue(classifier, out var nativeInfo) &&
                !string.IsNullOrEmpty(nativeInfo.Url))
            {
                var nativePath = nativeInfo.Path ?? MavenPathFromName(lib.Name, classifier);
                libTasks.Add(new DownloadTask(
                    _mirror.GetLibraryUrl(nativeInfo.Url, nativePath),
                    Path.Combine(_paths.LibrariesDir, nativePath.Replace('/', Path.DirectorySeparatorChar)),
                    nativeInfo.Sha1, nativeInfo.Size)
                {
                    DisplayName = Path.GetFileName(nativePath),
                    FallbackUrls = new[] { _fallback.GetLibraryUrl(nativeInfo.Url, nativePath) },
                });
            }
        }

        Report(InstallStage.Libraries, $"下载依赖库（0 / {libTasks.Count}）", 0, libTasks.Count);
        await _downloader.DownloadBatchAsync(libTasks, LibraryParallelism,
            new Progress<BatchProgress>(p => Report(InstallStage.Libraries,
                $"下载依赖库（{p.CompletedFiles} / {p.TotalFiles}）",
                p.CompletedFiles, p.TotalFiles, p.ReceivedBytes, p.TotalBytes, p.BytesPerSecond)),
            ct);

        // 4. 解压 natives
        Report(InstallStage.Natives, "解压本地库");
        ExtractNatives(version, versionId);

        // 5. 资产索引 + 资产
        if (version.AssetIndex is { } index && !string.IsNullOrEmpty(index.Id))
        {
            Report(InstallStage.AssetIndex, $"下载资产索引 {index.Id}");
            var indexPath = Path.Combine(_paths.AssetIndexesDir, index.Id + ".json");
            var indexUrl = _mirror.GetAssetIndexUrl(index.Id, index.Url!);
            await _downloader.DownloadFileAsync(
                new DownloadTask(indexUrl, indexPath, index.Sha1, index.Size)
                {
                    DisplayName = $"{index.Id}.json",
                    FallbackUrls = new[] { _fallback.GetAssetIndexUrl(index.Id, index.Url!) },
                },
                null, ct);

            var indexText = await File.ReadAllTextAsync(indexPath, ct);
            var assetIndex = System.Text.Json.JsonSerializer.Deserialize<AssetIndexFile>(indexText, JsonOptions.Default)
                             ?? new AssetIndexFile();

            Report(InstallStage.Assets, $"下载资源文件（0 / {assetIndex.Objects.Count}）", 0, assetIndex.Objects.Count);
            var assetTasks = assetIndex.Objects.Values
                .DistinctBy(o => o.Hash)
                .Select(o =>
                {
                    var official = $"https://resources.download.minecraft.net/{o.Hash[..2]}/{o.Hash}";
                    return new DownloadTask(
                        _mirror.GetAssetObjectUrl(o.Hash, official),
                        Path.Combine(_paths.AssetObjectsDir, o.Hash[..2], o.Hash),
                        o.Hash, o.Size)
                    {
                        DisplayName = o.Hash[..8] + "…",
                        FallbackUrls = new[] { _fallback.GetAssetObjectUrl(o.Hash, official) },
                    };
                })
                .ToList();

            await _downloader.DownloadBatchAsync(assetTasks, AssetParallelism,
                new Progress<BatchProgress>(p => Report(InstallStage.Assets,
                    $"下载资源文件（{p.CompletedFiles} / {p.TotalFiles}）",
                    p.CompletedFiles, p.TotalFiles, p.ReceivedBytes, p.TotalBytes, p.BytesPerSecond)),
                ct);

            // 旧版本（assets virtual）需要把 objects 复制一份到 resources 目录
            if (assetIndex.Virtual || assetIndex.MapToResources)
                LegacyMapResources(assetIndex);
        }

        // 6. log4j 配置（1.17+ 没有它游戏日志会刷屏）
        if (version.Logging?.Client?.File is { } logFile && !string.IsNullOrEmpty(logFile.Url))
        {
            Report(InstallStage.Logging, "下载日志配置");
            var logPath = Path.Combine(_paths.LogConfigsDir, logFile.Path ?? (logFile.Url.GetHashCode() + ".xml"));
            if (logFile.Path is null)
                logPath = Path.Combine(_paths.LogConfigsDir, Path.GetFileName(new Uri(logFile.Url).LocalPath));
            await _downloader.DownloadFileAsync(
                new DownloadTask(logFile.Url, logPath, logFile.Sha1, logFile.Size)
                { DisplayName = Path.GetFileName(logPath) },
                null, ct);
        }

        Report(InstallStage.Done, $"{versionId} 安装完成", 1, 1, 1, 1);
        return version;
    }

    private void ExtractNatives(VersionJson version, string versionId)
    {
        var nativesDir = _paths.NativesDir(versionId);
        Directory.CreateDirectory(nativesDir);

        foreach (var lib in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;
            if (lib.Natives is null) continue;
            if (!lib.Natives.TryGetValue(RuleEvaluator.CurrentOsName, out var classifier)) continue;
            if (lib.Downloads?.Classifiers is null ||
                !lib.Downloads.Classifiers.TryGetValue(classifier, out var nativeInfo)) continue;

            var path = nativeInfo.Path ?? MavenPathFromName(lib.Name, classifier);
            var full = Path.Combine(_paths.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            var exclude = lib.Extract?.Exclude ?? new List<string>();
            using var zip = ZipFile.OpenRead(full);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/')) continue;
                if (exclude.Any(e => entry.FullName.StartsWith(e.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                    continue;

                var dest = Path.Combine(nativesDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try { entry.ExtractToFile(dest, overwrite: true); }
                catch { /* 同名冲突忽略 */ }
            }
        }
    }

    private void LegacyMapResources(AssetIndexFile index)
    {
        var resourcesDir = Path.Combine(_paths.Root, "resources");
        foreach (var (name, obj) in index.Objects)
        {
            var src = Path.Combine(_paths.AssetObjectsDir, obj.Hash[..2], obj.Hash);
            if (!File.Exists(src)) continue;
            var dest = Path.Combine(resourcesDir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try { File.Copy(src, dest, overwrite: true); } catch { /* 忽略 */ }
        }
    }

    /// <summary>org.lwjgl:lwjgl:3.3.2 → org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2.jar</summary>
    public static string MavenPathFromName(string name, string? classifier = null)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return name.Replace('.', '/');

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifierSuffix = classifier is { Length: > 0 } ? $"-{classifier}" : string.Empty;

        var fileName = parts.Length > 3 && parts[3] is { Length: > 0 } extension
            ? $"{artifact}-{version}{classifierSuffix}.{extension}"
            : $"{artifact}-{version}{classifierSuffix}.jar";

        return $"{group}/{artifact}/{version}/{fileName}";
    }

    /// <summary>列出本地已安装的版本 id。</summary>
    public IReadOnlyList<string> ListInstalled()
    {
        if (!Directory.Exists(_paths.VersionsDir)) return Array.Empty<string>();
        return Directory.GetDirectories(_paths.VersionsDir)
            .Select(d => Path.GetFileName(d))
            .Where(id => File.Exists(_paths.VersionJarPath(id)))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>尽力删除一个版本目录（回滚残缺安装用）。删不掉也不抛。</summary>
    private void TryDeleteVersionDir(string versionId)
    {
        try
        {
            var dir = _paths.VersionDir(versionId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 文件被占用之类的情况：留着也不影响判定，下次重装会覆盖
        }
    }
}
