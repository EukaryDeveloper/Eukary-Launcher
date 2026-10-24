using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Services;

/// <summary>Adoptium API 返回的一个构建产物。</summary>
internal sealed class AdoptiumAsset
{
    [JsonPropertyName("binary")] public AdoptiumBinary? Binary { get; set; }
    [JsonPropertyName("release_name")] public string? ReleaseName { get; set; }
    [JsonPropertyName("version_data")] public AdoptiumVersionData? VersionData { get; set; }
}

internal sealed class AdoptiumBinary
{
    [JsonPropertyName("architecture")] public string? Architecture { get; set; }
    [JsonPropertyName("image_type")] public string? ImageType { get; set; }
    [JsonPropertyName("os")] public string? Os { get; set; }
    [JsonPropertyName("package")] public AdoptiumPackage? Package { get; set; }
    [JsonPropertyName("scm_ref")] public string? ScmRef { get; set; }
}

internal sealed class AdoptiumPackage
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("checksum")] public string? Checksum { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
}

internal sealed class AdoptiumVersionData
{
    [JsonPropertyName("major")] public int Major { get; set; }
    [JsonPropertyName("semver")] public string? Semver { get; set; }
}

public sealed class JavaRuntimeInfo
{
    public required int MajorVersion { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }

    public override string ToString() => $"Java {MajorVersion} · {DisplayName}";
}

/// <summary>
/// Java 运行时管理：不要假设用户装了 Java。
/// 优先使用启动器自己 runtime/ 目录下的 JRE，找不到再回退系统 java。
/// </summary>
public sealed class JavaManager
{
    private readonly string _runtimeRoot;
    private readonly HttpClient _http;

    public JavaManager(string runtimeRoot, HttpClient? http = null)
    {
        _runtimeRoot = runtimeRoot;
        _http = http ?? HttpClientFactory.Create();
    }

    public string RuntimeRoot => _runtimeRoot;

    /// <summary>把版本 JSON 要求的 majorVersion 归一到我们要下载的 JDK 主版本。</summary>
    public static int NormalizeMajor(int required) => required switch
    {
        <= 8 => 8,
        <= 17 => 17,
        _ => 21,
    };

    /// <summary>已托管的 Java 列表。</summary>
    public IReadOnlyList<JavaRuntimeInfo> ListManaged()
    {
        var result = new List<JavaRuntimeInfo>();
        if (!Directory.Exists(_runtimeRoot)) return result;

        foreach (var dir in Directory.GetDirectories(_runtimeRoot))
        {
            var name = Path.GetFileName(dir);
            if (!name.StartsWith("java-", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(name["java-".Length..], out var major)) continue;

            var exe = FindExecutable(dir);
            if (exe is null) continue;

            result.Add(new JavaRuntimeInfo
            {
                MajorVersion = major,
                DisplayName = $"启动器自带 Java {major}",
                ExecutablePath = exe,
            });
        }

        return result.OrderBy(x => x.MajorVersion).ToList();
    }

    /// <summary>找到一个可用的 Java：优先托管的，其次系统 PATH 里的。</summary>
    public JavaRuntimeInfo? FindSuitable(int requiredMajor)
    {
        var target = NormalizeMajor(requiredMajor);

        var managed = ListManaged();
        var exact = managed.FirstOrDefault(x => x.MajorVersion == target);
        if (exact is not null) return exact;

        // Java 向下兼容：21 能跑要求 17 的版本
        var higher = managed.Where(x => x.MajorVersion >= target).OrderBy(x => x.MajorVersion).FirstOrDefault();
        if (higher is not null) return higher;

        return FindSystemJava();
    }

    /// <summary>在 PATH / 常见安装目录里找系统 java.exe。</summary>
    public static JavaRuntimeInfo? FindSystemJava()
    {
        var candidates = new List<string>();

        var path = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim('"');
            try { candidates.Add(Path.Combine(trimmed, "java.exe")); }
            catch { /* 忽略非法路径 */ }
        }

        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome)
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(programFiles))
            {
                foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "BellSoft", "Zulu", "GraalVM" })
                {
                    var vendorDir = Path.Combine(programFiles, vendor);
                    if (!Directory.Exists(vendorDir)) continue;
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(vendorDir))
                            candidates.Add(Path.Combine(dir, "bin", "java.exe"));
                    }
                    catch { /* 忽略不可读目录 */ }
                }
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var major = QueryJavaMajor(candidate);
                if (major > 0)
                {
                    return new JavaRuntimeInfo
                    {
                        MajorVersion = major,
                        DisplayName = $"系统 Java {major}",
                        ExecutablePath = candidate,
                    };
                }
            }
            catch { /* 忽略探测失败 */ }
        }

        return null;
    }

    /// <summary>跑 java -version 解析主版本号。</summary>
    public static int QueryJavaMajor(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-version");

            using var p = Process.Start(psi);
            if (p is null) return 0;
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);

            return ParseMajorFromVersionOutput(output);
        }
        catch
        {
            return 0;
        }
    }

    public static int ParseMajorFromVersionOutput(string output)
    {
        // OpenJDK 输出形如：openjdk version "21.0.9" 2025-10-21
        // Java 8 及更早：java version "1.8.0_402"
        var quoteStart = output.IndexOf('"');
        var quoteEnd = output.IndexOf('"', quoteStart + 1);
        if (quoteStart < 0 || quoteEnd <= quoteStart) return 0;

        var version = output[(quoteStart + 1)..quoteEnd];
        var parts = version.Split('.');
        if (parts.Length == 0) return 0;

        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacy))
            return legacy; // 1.8 → 8

        return int.TryParse(parts[0], out var modern) ? modern : 0;
    }

    /// <summary>
    /// 确保有满足要求的 Java，没有就从 Adoptium 下载一个 JRE 解压到 runtime/。
    /// </summary>
    public async Task<JavaRuntimeInfo> EnsureJavaAsync(
        int requiredMajor,
        IProgress<FileProgress>? progress = null,
        CancellationToken ct = default)
    {
        var existing = FindSuitable(requiredMajor);
        if (existing is not null && existing.MajorVersion >= NormalizeMajor(requiredMajor))
            return existing;

        var target = NormalizeMajor(requiredMajor);
        return await DownloadJreAsync(target, progress, ct);
    }

    public async Task<JavaRuntimeInfo> DownloadJreAsync(
        int major,
        IProgress<FileProgress>? progress = null,
        CancellationToken ct = default)
    {
        var osName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "mac" : "linux";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => osName == "mac" ? "aarch64" : "aarch64",
            Architecture.Arm => "arm",
            _ => "x64",
        };

        if (osName != "windows")
            throw new PlatformNotSupportedException("M1 阶段仅支持 Windows 的自动 Java 下载，其他平台请手动指定 java 路径。");

        var apiUrl =
            $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot" +
            $"?architecture={arch}&image_type=jre&os={osName}&vendor=eclipse";

        using var response = await _http.GetAsync(apiUrl, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);

        var assets = JsonSerializer.Deserialize<List<AdoptiumAsset>>(json, JsonOptions.Default);
        var asset = assets?.FirstOrDefault(a => a.Binary?.Package?.Link is { Length: > 0 });
        if (asset?.Binary?.Package?.Link is not { Length: > 0 } link)
            throw new InvalidOperationException($"Adoptium 上没有找到 Java {major} 的 Windows JRE 包。");

        var fileName = asset.Binary!.Package!.Name ?? Path.GetFileName(new Uri(link).LocalPath);

        // 临时文件名必须可复现，断了下次接着下（上次 99.9% 被掐掉重来 40 分钟，太浪费）
        var tmpZip = Path.Combine(Path.GetTempPath(), $"ekl-jre-{major}-{fileName}.zip");

        // Adoptium 的包在 GitHub Release 上，国内直连不稳；清华 Adoptium 镜像兜底
        var mirrorLink = $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{major}/jre/{arch}/{osName}/{fileName}";

        var downloader = new DownloadService(_http);
        await downloader.DownloadFileAsync(
            new DownloadTask(link, tmpZip, null, asset.Binary.Package.Size)
            {
                DisplayName = fileName,
                FallbackUrls = new[] { mirrorLink },
            },
            progress,
            ct);

        // Adoptium 的 package.checksum 是 SHA-256，不是 SHA-1，不能塞给 DownloadTask.ExpectedSha1
        if (!string.IsNullOrWhiteSpace(asset.Binary.Package.Checksum))
        {
            await using var fs = new FileStream(tmpZip, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(asset.Binary.Package.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(tmpZip);
                throw new IOException($"Java {major} 的压缩包校验失败（SHA-256 不匹配）。");
            }
        }

        var targetDir = Path.Combine(_runtimeRoot, $"java-{major}");
        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, recursive: true); } catch { /* 残留目录，覆盖即可 */ }
        }
        Directory.CreateDirectory(targetDir);

        ZipFile.ExtractToDirectory(tmpZip, targetDir, overwriteFiles: true);
        try { File.Delete(tmpZip); } catch { /* 忽略临时文件清理失败 */ }

        var exe = FindExecutable(targetDir)
                  ?? throw new IOException($"解压 Java {major} 后找不到 java.exe。");

        return new JavaRuntimeInfo
        {
            MajorVersion = major,
            DisplayName = $"启动器自带 Java {major}",
            ExecutablePath = exe,
        };
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
    }

    /// <summary>在解压目录里递归找 java.exe / javaw.exe（Adoptium zip 有一层根目录）。</summary>
    public static string? FindExecutable(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return null;

        var direct = Path.Combine(rootDir, "bin", "java.exe");
        if (File.Exists(direct)) return direct;

        try
        {
            foreach (var sub in Directory.GetDirectories(rootDir))
            {
                var found = FindExecutable(sub);
                if (found is not null) return found;
            }
        }
        catch { /* 忽略不可读目录 */ }

        return null;
    }
}
