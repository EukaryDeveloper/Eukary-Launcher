using System.Diagnostics;
using System.Text.Json;
using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

// M1 冒烟测试：
//   默认           → 只跑纯逻辑断言（秒级）
//   --install <id> → 真下载一个版本（libraries + assets + jar + natives）
//   --launch <id>  → 装完再真启动，抓 90 秒日志，然后结束进程
//
// 纯逻辑断言不需要网络，CI 里可以直接跑；带网络的两条要手动触发。

var argv = Environment.GetCommandLineArgs().Skip(1).ToArray();

var failed = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : " — " + detail)}");
    if (!ok) failed++;
}

Console.WriteLine("== 1. 纯逻辑断言 ==");

// 离线 UUID 必须与 Java UUID.nameUUIDFromBytes 一致，基准值来自 Python uuid 库
Check("离线 UUID（Notch）",
    OfflineAccount.ComputeUuid("Notch") == "b50ad385-829d-3141-a216-7e7d7539ba7f",
    OfflineAccount.ComputeUuid("Notch"));
Check("离线 UUID 稳定（同名字两次结果相同）",
    OfflineAccount.ComputeUuid("Eukary") == OfflineAccount.ComputeUuid("Eukary"));
Check("离线 UUID 是版本 3",
    OfflineAccount.ComputeUuid("Eukary")[14] == '3',
    OfflineAccount.ComputeUuid("Eukary"));

// rules 求值
Check("空规则默认放行", RuleEvaluator.IsAllowed(null));
Check("windows 规则在 Windows 上放行",
    RuleEvaluator.IsAllowed(new List<Rule>
    {
        new() { Action = "allow", Os = new OsCondition { Name = "windows" } },
    }));
Check("linux 规则在 Windows 上拒绝",
    !RuleEvaluator.IsAllowed(new List<Rule>
    {
        new() { Action = "allow", Os = new OsCondition { Name = "linux" } },
    }));
Check("disallow 覆盖前面的 allow",
    !RuleEvaluator.IsAllowed(new List<Rule>
    {
        new() { Action = "allow" },
        new() { Action = "disallow", Os = new OsCondition { Name = "windows" } },
    }));
Check("x86_64 等于当前 x64",
    RuleEvaluator.IsAllowed(new List<Rule>
    {
        new() { Action = "allow", Os = new OsCondition { Name = "windows", Arch = "x86_64" } },
    }));

// maven 路径
Check("maven 坐标转路径",
    VersionInstaller.MavenPathFromName("org.lwjgl:lwjgl:3.3.2") == "org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2.jar",
    VersionInstaller.MavenPathFromName("org.lwjgl:lwjgl:3.3.2"));
Check("maven 坐标带 classifier",
    VersionInstaller.MavenPathFromName("org.lwjgl:lwjgl:3.3.2", "natives-windows")
    == "org/lwjgl/lwjgl/3.3.2/lwjgl-3.3.2-natives-windows.jar");

// 参数拆词
Check("额外 JVM 参数支持引号路径",
    string.Join("|", ArgumentBuilder.SplitCommandLine("-XX:+UseG1GC \"C:\\my path\\x.dll\" -D test"))
    == "-XX:+UseG1GC|C:\\my path\\x.dll|-D|test");

// Java 版本解析
Check("解析 java -version 输出（21）",
    JavaManager.ParseMajorFromVersionOutput("openjdk version \"21.0.9\" 2025-10-21") == 21);
Check("解析 java -version 输出（1.8）",
    JavaManager.ParseMajorFromVersionOutput("java version \"1.8.0_402\"") == 8);
Check("Java 版本归一（8/17/21）",
    JavaManager.NormalizeMajor(8) == 8 && JavaManager.NormalizeMajor(17) == 17 && JavaManager.NormalizeMajor(21) == 21);

// inheritsFrom 深度合并：子版本必须继承父版本的 libraries
var parent = new VersionJson
{
    Id = "1.20.4",
    MainClass = "net.minecraft.client.main.Main",
    Libraries = new()
    {
        new() { Name = "com.mojang:minecraft:1.20.4" },
        new() { Name = "org.lwjgl:lwjgl:3.3.2" },
    },
    Arguments = new ArgumentsSpec
    {
        Jvm = { new ArgumentEntry { Values = { "-cp", "${classpath}" } } },
        Game =
        {
            new ArgumentEntry { Values = { "--username", "${auth_player_name}" } },
            new ArgumentEntry { Values = { "--version", "${version_name}" } },
            new ArgumentEntry { Values = { "--gameDir", "${game_directory}" } },
            new ArgumentEntry
            {
                Rules = new List<Rule>
                {
                    new() { Action = "allow", Features = new Dictionary<string, bool> { ["has_custom_resolution"] = true } },
                },
                Values = new List<string> { "--width", "${resolution_width}" },
            },
        },
    },
};
var child = new VersionJson
{
    Id = "fabric-loader-0.15.7-1.20.4",
    InheritsFrom = "1.20.4",
    Libraries = new() { new() { Name = "net.fabricmc:fabric-loader:0.15.7" } },
};
var merged = VersionMerger.Merge(child, id => id == "1.20.4" ? parent : null);
Check("inheritsFrom 合并主类", merged.MainClass == "net.minecraft.client.main.Main");
Check("inheritsFrom 合并父版本库",
    merged.Libraries.Count == 3 && merged.Libraries.Any(l => l.Name == "org.lwjgl:lwjgl:3.3.2"),
    $"{merged.Libraries.Count} 个库");
Check("inheritsFrom 合并 JVM 参数", merged.Arguments?.Jvm.Count == 1);

// 参数 token 替换
var paths = new GamePaths(Path.Combine(Path.GetTempPath(), "ekl-smoke-game"));
var options = OfflineAccount.CreateLaunchOptions(paths.Root, "1.20.4", "Eukary", "java", 2048);
var gameArgs = ArgumentBuilder.BuildGameArguments(merged, paths, options);
Check("游戏参数替换 ${auth_player_name}", gameArgs.Contains("Eukary"), string.Join(" ", gameArgs));
Check("游戏参数替换 ${version_name}", gameArgs.Contains("1.20.4"));
Check("游戏参数替换 ${game_directory}", gameArgs.Contains(paths.Root));
Check("has_custom_resolution=false 时跳过该参数", !gameArgs.Contains("--width"));
Check("游戏参数不含未替换的 token", !gameArgs.Any(a => a.Contains("${")));

// 1.12 及更早：minecraftArguments 字符串
var legacy = new VersionJson
{
    Id = "1.12.2",
    MainClass = "net.minecraft.client.main.Main",
    MinecraftArguments = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory}",
};
var legacyArgs = ArgumentBuilder.BuildGameArguments(legacy, paths, options);
Check("旧版 minecraftArguments 拆分正确",
    legacyArgs.SequenceEqual(new[] { "--username", "Eukary", "--version", "1.20.4", "--gameDir", paths.Root }),
    string.Join(" ", legacyArgs));

Console.WriteLine();

// ---------------- 网络部分 ----------------
var installIdx = Array.IndexOf(argv, "--install");
var launchIdx = Array.IndexOf(argv, "--launch");

if (installIdx < 0 && launchIdx < 0)
{
    Console.WriteLine(failed == 0
        ? "✅ 纯逻辑断言全部通过。加 --install 1.20.4 或 --launch 1.20.4 跑真实下载/启动。"
        : $"❌ {failed} 项断言失败。");
    return failed == 0 ? 0 : 1;
}

var mode = installIdx >= 0 ? "install" : "launch";
var versionId = argv[(installIdx >= 0 ? installIdx : launchIdx) + 1];

var gameRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "EukaryLauncher", ".minecraft");

var gamePaths = new GamePaths(gameRoot);
var mirror = MirrorFactory.Create("bmclapi");
var installer = new VersionInstaller(gamePaths, mirror);
var javaManager = new JavaManager(GamePaths.RuntimeRoot);

var sw = Stopwatch.StartNew();
Console.WriteLine($"== 2. 安装 {versionId}（镜像：{mirror.DisplayName}）==");
Console.WriteLine($"   游戏目录：{gameRoot}");

var version = await installer.InstallAsync(versionId, new Progress<InstallProgress>(p =>
{
    var pct = p.TotalFiles > 0 ? p.CompletedFiles * 100.0 / p.TotalFiles : 0;
    Console.Write($"\r   [{p.Stage,-11}] {p.Message,-46} {pct,5:0.0}%   ");
}));

Console.WriteLine($"\n   完成，用时 {sw.Elapsed.TotalSeconds:0.0}s");
Console.WriteLine($"   mainClass = {version.MainClass}");
Console.WriteLine($"   libraries = {version.Libraries.Count}");
Console.WriteLine($"   assetIndex = {version.AssetIndex?.Id}");
Console.WriteLine($"   javaVersion = {version.JavaVersion?.MajorVersion}");

Check("版本 JSON 已落盘", File.Exists(gamePaths.VersionJsonPath(versionId)));
Check("主 jar 已下载", File.Exists(gamePaths.VersionJarPath(versionId)));

var classpath = ArgumentBuilder.BuildClassPath(version, gamePaths, versionId);
Check("classpath 非空", classpath.Count > 0, $"{classpath.Count} 个条目");
Check("classpath 里所有文件都存在", classpath.All(File.Exists),
    classpath.FirstOrDefault(f => !File.Exists(f)) ?? "全部存在");

// 现代 MC（1.19+）把 natives jar 直接放进 classpath，不再用 natives classifier 字段；
// 老版本才需要解压。两种组织方式都算正确。
var hasNativeClassifierLibs = version.Libraries.Any(l =>
    l.Natives is not null && l.Natives.ContainsKey(RuleEvaluator.CurrentOsName)
    && RuleEvaluator.IsAllowed(l.Rules));
var nativesPopulated = Directory.Exists(gamePaths.NativesDir(versionId))
                       && Directory.EnumerateFileSystemEntries(gamePaths.NativesDir(versionId)).Any();
Check("natives 处理正确（老版本解压 / 新版本直接进 classpath）",
    !hasNativeClassifierLibs || nativesPopulated,
    hasNativeClassifierLibs ? $"需解压，实际 {nativesPopulated}" : "现代格式，无需解压");
Check("本机 natives jar 在 classpath 里",
    classpath.Any(p => p.Contains("natives-" + RuleEvaluator.CurrentOsName, StringComparison.OrdinalIgnoreCase)),
    classpath.FirstOrDefault(p => p.Contains("natives-", StringComparison.OrdinalIgnoreCase)) ?? "未找到");
Check("主类不为空", !string.IsNullOrWhiteSpace(version.MainClass));

Check("assets 目录已建立", Directory.Exists(gamePaths.AssetObjectsDir));

if (mode == "install")
{
    Console.WriteLine();
    Console.WriteLine(failed == 0 ? "✅ 安装流程通过。" : $"❌ {failed} 项失败。");
    return failed == 0 ? 0 : 1;
}

// ---------------- 真启动 ----------------
Console.WriteLine();
Console.WriteLine("== 3. 确保 Java 运行时 ==");
var required = version.JavaVersion?.MajorVersion ?? 17;
var java = await javaManager.EnsureJavaAsync(required, new Progress<FileProgress>(p =>
{
    Console.Write($"\r   下载 Java：{p.FileName,-40} {(p.TotalBytes is > 0 ? p.ReceivedBytes * 100.0 / p.TotalBytes.Value : 0),5:0.0}%   ");
}));
Console.WriteLine($"\n   使用 {java} → {java.ExecutablePath}");

Console.WriteLine();
Console.WriteLine($"== 4. 启动 {versionId}（抓 90 秒日志后结束进程）==");

var launchOptions = OfflineAccount.CreateLaunchOptions(
    gameRoot, versionId, "EukaryTester", java.ExecutablePath, 2048);

var launcher = new GameLauncher();
var lines = new List<string>();
var exited = new TaskCompletionSource<int>();

launcher.OutputReceived += line =>
{
    lines.Add(line);
    Console.WriteLine("   " + line);
};
launcher.Exited += code => exited.TrySetResult(code);

var proc = launcher.Start(version, gamePaths, launchOptions);
Console.WriteLine($"   进程 PID = {proc.Id}");

await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(90)));

if (!exited.Task.IsCompleted)
{
    Console.WriteLine("   90 秒到了，主动结束进程（正常情况游戏会一直运行）。");
    try { proc.Kill(true); } catch { /* 已退出 */ }
}
else
{
    Console.WriteLine($"   进程自行退出，代码 {exited.Task.Result}");
}

var all = string.Join("\n", lines);
Check("进程确实起来了", proc.Id > 0);
Check("日志里有 Minecraft 启动痕迹",
    all.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)
    || all.Contains("LWJGL", StringComparison.OrdinalIgnoreCase)
    || all.Contains("Fabric", StringComparison.OrdinalIgnoreCase));
Check("没有 ClassNotFoundException",
    !all.Contains("ClassNotFoundException", StringComparison.OrdinalIgnoreCase));
Check("没有 NoClassDefFoundError",
    !all.Contains("NoClassDefFoundError", StringComparison.OrdinalIgnoreCase));
Check("没有 'no lwjgl in java.library.path'",
    !all.Contains("no lwjgl in java.library.path", StringComparison.OrdinalIgnoreCase));

Console.WriteLine();
Console.WriteLine(failed == 0 ? "✅ M1 端到端通过。" : $"❌ {failed} 项失败。");
return failed == 0 ? 0 : 1;
