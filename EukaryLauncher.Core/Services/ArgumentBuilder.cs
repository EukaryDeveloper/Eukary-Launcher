using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// 把版本 JSON 拼成真实的 JVM / 游戏参数。
/// 1.13+ 用 arguments 数组；1.12 及更早用 minecraftArguments 字符串，两条路都要支持。
/// 所有参数以数组形式返回，交给 ProcessStartInfo.ArgumentList，绝不拼字符串加引号
/// （中文路径和空格会让人怀疑人生）。
/// </summary>
public static class ArgumentBuilder
{
    public const string LauncherName = "EukaryLauncher";
    public const string LauncherVersion = "0.1.0";

    /// <summary>classpath 分隔符：Windows 是 ;，其他平台是 :</summary>
    public static string ClassPathSeparator => OperatingSystem.IsWindows() ? ";" : ":";

    /// <summary>收集所有需要在 classpath 里的 jar。</summary>
    public static IReadOnlyList<string> BuildClassPath(VersionJson version, GamePaths paths, string versionId)
    {
        var entries = new List<string>();

        foreach (var lib in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules)) continue;

            var artifact = lib.Downloads?.Artifact;
            if (artifact is null) continue;

            var mavenPath = artifact.Path ?? VersionInstaller.MavenPathFromName(lib.Name);
            var full = Path.Combine(paths.LibrariesDir, mavenPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) entries.Add(Path.GetFullPath(full));
        }

        var jar = paths.VersionJarPath(versionId);
        if (File.Exists(jar)) entries.Add(Path.GetFullPath(jar));

        return entries;
    }

    public static IReadOnlyList<string> BuildJvmArguments(
        VersionJson version,
        GamePaths paths,
        LaunchOptions options,
        string classPath,
        string? logConfigPath)
    {
        var args = new List<string>
        {
            $"-Xmx{options.MaxMemoryMb}M",
            $"-Djava.library.path={paths.NativesDir(options.VersionId)}",
            $"-Dminecraft.launcher.brand={LauncherName}",
            $"-Dminecraft.launcher.version={LauncherVersion}",
        };

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${natives_directory}"] = paths.NativesDir(options.VersionId),
            ["${launcher_name}"] = LauncherName,
            ["${launcher_version}"] = LauncherVersion,
            ["${classpath}"] = classPath,
            ["${classpath_separator}"] = ClassPathSeparator,
            ["${library_directory}"] = paths.LibrariesDir,
        };

        if (version.Arguments?.Jvm is { Count: > 0 } jvmArgs)
        {
            foreach (var entry in jvmArgs)
            {
                if (!RuleEvaluator.IsAllowed(entry.Rules,
                        hasCustomResolution: options.UseCustomResolution))
                    continue;

                foreach (var value in entry.Values)
                    args.Add(ApplyReplacements(value, replacements));
            }
        }
        else
        {
            // 老版本：补上 vanilla 默认需要的最小集
            args.Add($"-Dminecraft.launcher.brand={LauncherName}");
            args.Add("-cp");
            args.Add(classPath);
        }

        // log4j 配置（1.17+ 缺了这个游戏日志会刷屏）
        if (logConfigPath is not null && version.Logging?.Client?.Argument is { } logArg)
        {
            args.Add(logArg.Replace("{path}", logConfigPath, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(options.ExtraJvmArgs))
        {
            foreach (var extra in SplitCommandLine(options.ExtraJvmArgs))
                args.Add(extra);
        }

        return args;
    }

    public static IReadOnlyList<string> BuildGameArguments(
        VersionJson version,
        GamePaths paths,
        LaunchOptions options)
    {
        var replacements = BuildGameReplacements(version, paths, options);

        // 1.13+：arguments.game 数组
        if (version.Arguments?.Game is { Count: > 0 } gameArgs)
        {
            var result = new List<string>();
            foreach (var entry in gameArgs)
            {
                if (!RuleEvaluator.IsAllowed(entry.Rules,
                        hasCustomResolution: options.UseCustomResolution))
                    continue;

                foreach (var value in entry.Values)
                    result.Add(ApplyReplacements(value, replacements));
            }
            return result;
        }

        // 1.12-：minecraftArguments 字符串
        if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            return version.MinecraftArguments
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => ApplyReplacements(t, replacements))
                .ToList();
        }

        return Array.Empty<string>();
    }

    private static Dictionary<string, string> BuildGameReplacements(
        VersionJson version, GamePaths paths, LaunchOptions options) => new(StringComparer.Ordinal)
    {
        ["${auth_player_name}"] = options.PlayerName,
        ["${auth_uuid}"] = options.PlayerUuid,
        ["${auth_access_token}"] = options.AccessToken,
        ["${auth_session}"] = options.AccessToken,
        ["${user_type}"] = options.UserType,
        ["${user_properties}"] = "{}",
        ["${version_name}"] = options.VersionId,
        ["${version_type}"] = version.Type ?? "release",
        ["${game_directory}"] = paths.Root,
        ["${assets_root}"] = paths.AssetsDir,
        ["${game_assets}"] = paths.AssetsDir,
        ["${assets_index_name}"] = version.Assets ?? version.AssetIndex?.Id ?? "",
        ["${resolution_width}"] = options.WindowWidth.ToString(),
        ["${resolution_height}"] = options.WindowHeight.ToString(),
        ["${launcher_name}"] = LauncherName,
        ["${launcher_version}"] = LauncherVersion,
    };

    private static string ApplyReplacements(string template, IReadOnlyDictionary<string, string> map)
    {
        if (!template.Contains("${", StringComparison.Ordinal)) return template;

        var result = template;
        foreach (var (token, value) in map)
            result = result.Replace(token, value, StringComparison.Ordinal);

        return result;
    }

    /// <summary>把用户写的额外 JVM 参数拆开（支持引号包裹含空格的路径）。</summary>
    public static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0) yield return current.ToString();
    }
}
