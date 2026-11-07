using EukaryLauncher.Core.Models;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>
/// 队列任务的构造工厂。每个工厂决定「卡片上要显示哪些子步骤」以及「真正要干什么」，
/// 队列本身只负责排队与状态机。
/// </summary>
public static class DownloadJobs
{
    /// <summary>与 InstallStage 前 7 项一一对应。</summary>
    private static readonly string[] VanillaSteps =
        { "版本 JSON", "主 jar", "依赖库", "本地库", "资产索引", "资源文件", "日志配置" };

    /// <summary>
    /// 安装完成时按当时的全局「版本隔离设定」给新版本记下隔离旗标。
    /// 之后改设置不影响已装版本——这就是「仅对之后新安装的版本生效」的实现方式。
    /// </summary>
    private static void MarkIsolation(GameContext ctx, string versionId, VersionJson? version)
    {
        if (version is null) return;
        var isolated = IsolationPolicy.ShouldIsolate(AppServices.Settings.Current.VersionIsolation, version);
        VersionIsolationStore.Set(ctx.Paths, versionId, isolated);
        LogService.Instance.Info($"版本 {versionId} 隔离状态：{(isolated ? "开启" : "关闭")}");
    }

    public static DownloadJob Vanilla(VersionManifestEntry entry)
    {
        var job = new DownloadJob
        {
            Kind = "原版游戏 · ",
            Title = entry.Id,
            Subtitle = $"Minecraft {entry.Id} · 本体 + 依赖库 + 资源",
        };

        foreach (var step in VanillaSteps) job.AddStep(step);

        job.Runner = async (j, ct) =>
        {
            var ctx = AppServices.Context;
            try
            {
                await ctx.Installer.InstallAsync(entry, DownloadManager.RouteByStage(j), ct);
            }
            catch
            {
                // 安装器已把残缺的版本目录回滚掉，这里刷新列表让 UI 立刻回到真实状态，
                // 不然要等重启才会发现列表里多了个启动不了的"幽灵版本"
                ctx.RefreshInstalled();
                throw;
            }
            ctx.RefreshInstalled();
            MarkIsolation(ctx, entry.Id, ctx.LoadInstalledVersion(entry.Id));
        };

        return job;
    }

    public static DownloadJob FabricLoader(string gameVersion, string loaderVersion)
    {
        var versionId = $"fabric-loader-{loaderVersion}-{gameVersion}";

        var job = new DownloadJob
        {
            Kind = "模组加载器 · ",
            Title = $"Fabric Loader {loaderVersion}",
            Subtitle = $"Minecraft {gameVersion} · {versionId}",
        };

        job.AddStep("加载器清单");
        job.AddStep("加载器依赖库");
        job.AddStep("关联原版主 jar");

        job.Runner = async (j, ct) =>
        {
            var ctx = AppServices.Context;
            var meta = new FabricMetaService(ctx.Http);

            j.Report(0, 35, "读取 Fabric 元服务", JobStatus.Running);
            var profile = await meta.GetProfileAsync(gameVersion, loaderVersion, ct)
                          ?? throw new IOException($"Fabric Loader {loaderVersion} 的版本信息获取失败。");

            await ctx.Installer.SaveVersionJsonAsync(profile.Id, profile, ct);
            j.Report(0, 100, $"版本标识 {profile.Id}", JobStatus.Succeeded);

            j.Report(1, 3, "准备依赖库", JobStatus.Running);
            await ctx.Installer.InstallCoreAsync(profile, profile.Id,
                DownloadManager.RouteToSingleStep(j, 1), ct);
            j.Report(1, 100, "依赖库就绪", JobStatus.Succeeded);

            // Fabric 的版本 JSON 不带主 jar，直接沿用已装好的原版 jar
            j.Report(2, 40, "复制原版主 jar", JobStatus.Running);
            EnsureJar(ctx, gameVersion, profile.Id);
            j.Report(2, 100, "已关联", JobStatus.Succeeded);

            ctx.RefreshInstalled();
            MarkIsolation(ctx, profile.Id, profile);
        };

        return job;
    }

    private static void EnsureJar(GameContext ctx, string vanillaVersion, string loaderVersionId)
    {
        var src = ctx.Paths.VersionJarPath(vanillaVersion);
        var dst = ctx.Paths.VersionJarPath(loaderVersionId);

        if (File.Exists(dst) || !File.Exists(src))
            throw new IOException($"缺少原版 {vanillaVersion} 的主 jar，无法完成加载器安装。");

        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
    }

    public static DownloadJob Mod(
        ModrinthProject project,
        ModrinthVersion version,
        ModrinthFile file,
        string gameVersion,
        string loader)
    {
        var job = new DownloadJob
        {
            Kind = "模组 · ",
            Title = project.Title,
            Subtitle = $"{version.Name} · Minecraft {gameVersion}"
                       + (loader.Length > 0 ? $" · {LoaderIds.DisplayName(loader)}" : ""),
        };

        job.AddStep(file.Filename);

        job.Runner = async (j, ct) =>
        {
            var ctx = AppServices.Context;
            var dir = ctx.Paths.ModsDir(gameVersion);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, file.Filename);

            var progress = DownloadManager.UiProgress<FileProgress>(p =>
            {
                var pct = p.TotalBytes is > 0
                    ? Math.Clamp(p.ReceivedBytes * 100.0 / p.TotalBytes.Value, 0, 100)
                    : 0;
                j.Report(0, pct, $"{p.FileName} · {Humanize.Bytes(p.ReceivedBytes)}", JobStatus.Running);
            });

            var downloader = new DownloadService(ctx.Http);
            await downloader.DownloadFileAsync(
                new DownloadTask(file.Url, dest, file.Sha1, file.Size)
                {
                    DisplayName = file.Filename,
                },
                progress,
                ct);

            j.Report(0, 100, $"已放入 mods/{gameVersion}/", JobStatus.Succeeded);
            AppServices.ModIndex.Record(gameVersion, project, version, file, loader);
        };

        return job;
    }

    // ====================================================================
    // Forge / NeoForge
    // 这两个没有"自己拼版本 JSON"的路可走：它们的版本 JSON 藏在安装包里，
    // 还带着一堆必须按顺序跑完的 processor。官方安装器支持命令行静默装，
    // 直接交给它，比自己复刻一套安装流程稳得多。
    // ====================================================================

    public static DownloadJob Forge(string gameVersion, string forgeVersion) =>
        InstallerBasedLoader(
            gameVersion: gameVersion,
            loaderName: "Forge",
            installerFileName: $"forge-{gameVersion}-{forgeVersion}-installer.jar",
            downloadUrl: LoaderMeta.ForgeInstallerUrl(new ForgeBuild(gameVersion, forgeVersion)),
            fallbackUrl: LoaderMeta.ForgeInstallerUrl(new ForgeBuild(gameVersion, forgeVersion), official: true),
            resultVersionId: new ForgeBuild(gameVersion, forgeVersion).VersionId,
            installArgument: "--installClient");

    public static DownloadJob NeoForge(string gameVersion, string neoForgeVersion)
    {
        var build = new NeoForgeBuild(gameVersion, neoForgeVersion,
            $"/maven/net/neoforged/neoforge/{neoForgeVersion}/neoforge-{neoForgeVersion}-installer.jar");

        return InstallerBasedLoader(
            gameVersion: gameVersion,
            loaderName: "NeoForge",
            installerFileName: $"neoforge-{neoForgeVersion}-installer.jar",
            downloadUrl: LoaderMeta.NeoForgeInstallerUrl(build),
            fallbackUrl: LoaderMeta.NeoForgeInstallerUrl(build, official: true),
            resultVersionId: build.VersionId,
            // NeoForge 用的是连字符风格的参数
            installArgument: "--install-client");
    }

    private static DownloadJob InstallerBasedLoader(
        string gameVersion,
        string loaderName,
        string installerFileName,
        string downloadUrl,
        string fallbackUrl,
        string resultVersionId,
        string installArgument)
    {
        var job = new DownloadJob
        {
            Kind = "模组加载器 · ",
            Title = $"{loaderName}（Minecraft {gameVersion}）",
            Subtitle = $"Minecraft {gameVersion} · {resultVersionId}",
        };

        job.AddStep("下载安装器");
        job.AddStep("准备 Java");
        job.AddStep("运行安装器");

        job.Runner = async (j, ct) =>
        {
            var ctx = AppServices.Context;

            var installerPath = LoaderMeta.LocalInstallerPath(installerFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

            if (!File.Exists(installerPath))
            {
                j.Report(0, 0, "下载安装器", JobStatus.Running);
                var progress = DownloadManager.UiProgress<FileProgress>(p =>
                {
                    var pct = p.TotalBytes is > 0
                        ? Math.Clamp(p.ReceivedBytes * 100.0 / p.TotalBytes.Value, 0, 100)
                        : 0;
                    j.Report(0, pct, $"{p.FileName} · {Humanize.Bytes(p.ReceivedBytes)}", JobStatus.Running);
                });

                await new DownloadService(ctx.Http).DownloadFileAsync(
                    new DownloadTask(downloadUrl, installerPath, null, null)
                    {
                        DisplayName = installerFileName,
                        FallbackUrls = new[] { fallbackUrl },
                    },
                    progress, ct);
            }

            j.Report(0, 100, "安装器就绪", JobStatus.Succeeded);

            // 安装器是 Java 程序，先保证有能跑它的 Java（至少 17）
            j.Report(1, 0, "检查 Java 运行时", JobStatus.Running);
            var java = await ctx.Java.EnsureJavaAsync(17, null, ct);
            j.Report(1, 100, $"Java {java.MajorVersion}", JobStatus.Succeeded);

            // Forge 的安装器会去写 launcher_profiles.json，没有就先放一个空的，
            // 否则它会弹一个"找不到档案"的错然后退出
            EnsureLauncherProfiles(ctx.Paths.Root);

            var lines = new List<string>();
            j.Report(2, 5, "启动安装器", JobStatus.Running);

            // 先记下现有的版本目录，装完才知道新出来的是哪个
            var before = ExistingVersionDirs(ctx);

            var code = await ProcessRunner.RunAsync(
                java.ExecutablePath,
                new[] { "-jar", installerPath, installArgument, ctx.Paths.Root },
                ctx.Paths.Root,
                line =>
                {
                    lines.Add(line);
                    var text = line.Trim();
                    if (text.Length == 0) return;

                    // 安装器只会往控制台打进度，百分比只能按行数大致推进
                    var pct = Math.Min(90, 5 + lines.Count);
                    j.Report(2, pct, text.Length > 60 ? text[..60] + "…" : text, JobStatus.Running);
                },
                ct);

            if (code != 0)
                throw new IOException(ProcessRunner.DescribeFailure($"{loaderName} 安装器", code, lines));

            // 版本 id 由安装器自己决定（Forge 是 1.20.4-forge-49.0.31，NeoForge 的命名又不完全一样），
            // 拿它真正建出来的那个 id，别猜
            var createdId = DiscoverCreatedVersion(ctx, before) ?? resultVersionId;

            // 1.17 之后 Forge / NeoForge 不再改动主 jar，安装器也就不会往版本目录里放 jar，
            // 但启动器按"jar + json 都在"才算装好，而且启动时主 jar 必须在 classpath 里——
            // 这里沿用原版那份。老版本（1.16.5 及更早）装完自带打过补丁的 jar，就不要覆盖。
            EnsureVersionJar(ctx, gameVersion, createdId);

            j.Report(2, 100, $"已安装 {createdId}", JobStatus.Succeeded);
            ctx.RefreshInstalled();
            MarkIsolation(ctx, createdId, ctx.LoadInstalledVersion(createdId));
        };

        return job;
    }

    private static HashSet<string> ExistingVersionDirs(GameContext ctx)
    {
        var dir = ctx.Paths.VersionsDir;
        if (!Directory.Exists(dir)) return new HashSet<string>(StringComparer.Ordinal);

        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal)!;
    }

    /// <summary>安装器新建出来的那个版本 id（读它自己写的版本 JSON，不猜命名规则）。</summary>
    private static string? DiscoverCreatedVersion(GameContext ctx, ISet<string> before)
    {
        if (!Directory.Exists(ctx.Paths.VersionsDir)) return null;

        foreach (var name in Directory.GetDirectories(ctx.Paths.VersionsDir)
                     .Select(Path.GetFileName)
                     .Where(n => !string.IsNullOrEmpty(n) && !before.Contains(n)))
        {
            var jsonPath = ctx.Paths.VersionJsonPath(name!);
            if (!File.Exists(jsonPath)) continue;

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(
                    File.ReadAllText(jsonPath), JsonOptions.Default);
                if (parsed is { Id.Length: > 0 }) return parsed.Id;
            }
            catch
            {
                // 读不出来就退回目录名
            }
            return name;
        }

        return null;
    }

    /// <summary>
    /// 版本目录里得有主 jar——启动器按"jar + json 都在"算装好，启动时它还要进 classpath。
    /// 已经有的话就不动（1.16.5 及更早的 Forge 会自己产出打过补丁的 jar）。
    /// </summary>
    private static void EnsureVersionJar(GameContext ctx, string vanillaVersion, string targetVersionId)
    {
        var dst = ctx.Paths.VersionJarPath(targetVersionId);
        if (File.Exists(dst)) return;

        var src = ctx.Paths.VersionJarPath(vanillaVersion);
        if (!File.Exists(src))
            throw new IOException($"缺少原版 {vanillaVersion} 的主 jar，无法完成加载器安装。");

        Directory.CreateDirectory(ctx.Paths.VersionDir(targetVersionId));
        File.Copy(src, dst, overwrite: true);
    }

    /// <summary>官方安装器认死这个文件的存在，没有它会直接退出。</summary>
    private static void EnsureLauncherProfiles(string gameDir)
    {
        var path = Path.Combine(gameDir, "launcher_profiles.json");
        if (File.Exists(path)) return;

        try
        {
            Directory.CreateDirectory(gameDir);
            File.WriteAllText(path, "{\"profiles\":{},\"selectedProfile\":\"\"}");
        }
        catch
        {
            // 写不出来就赌安装器不管它
        }
    }

    // ====================================================================
    // OptiFine
    // 和 Forge 共存时它就是一个 Forge mod（丢进 mods 文件夹即可，官方也是这么用的）；
    // 单独装时才需要打补丁 + 自建版本。
    // ====================================================================

    /// <param name="withForge">本次安装是否同时装了 Forge——决定了 OptiFine 以什么形态落地。</param>
    public static DownloadJob OptiFine(OptiFineBuild build, bool withForge)
    {
        var job = new DownloadJob
        {
            Kind = withForge ? "模组 · " : "模组加载器 · ",
            Title = $"OptiFine {build.SelfVersion}",
            Subtitle = withForge
                ? $"Minecraft {build.GameVersion} · 作为 Forge 模组安装"
                : $"Minecraft {build.GameVersion} · {build.VersionId}",
        };

        job.AddStep("下载安装包");
        job.AddStep(withForge ? "放入 mods 目录" : "打补丁");
        if (!withForge) job.AddStep("建立版本");

        job.Runner = async (j, ct) =>
        {
            var ctx = AppServices.Context;

            var installerPath = LoaderMeta.LocalInstallerPath(build.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

            if (!File.Exists(installerPath))
            {
                j.Report(0, 0, "下载 OptiFine", JobStatus.Running);
                var progress = DownloadManager.UiProgress<FileProgress>(p =>
                {
                    var pct = p.TotalBytes is > 0
                        ? Math.Clamp(p.ReceivedBytes * 100.0 / p.TotalBytes.Value, 0, 100)
                        : 0;
                    j.Report(0, pct, $"{p.FileName} · {Humanize.Bytes(p.ReceivedBytes)}", JobStatus.Running);
                });

                await new DownloadService(ctx.Http).DownloadFileAsync(
                    new DownloadTask(LoaderMeta.OptiFineDownloadUrl(build), installerPath, null, null)
                    {
                        DisplayName = build.FileName,
                    },
                    progress, ct);
            }

            j.Report(0, 100, "安装包就绪", JobStatus.Succeeded);

            if (withForge)
            {
                // 官方用法：OptiFine 的安装包本身就是 Forge 认的 mod jar
                var dir = ctx.Paths.ModsDir(build.GameVersion);
                Directory.CreateDirectory(dir);
                File.Copy(installerPath, Path.Combine(dir, build.FileName), overwrite: true);
                j.Report(1, 100, $"已放入 mods/{build.GameVersion}/", JobStatus.Succeeded);
                ctx.RefreshInstalled();
                return;
            }

            // 单独装：打补丁 → 建版本
            j.Report(1, 10, "检查 Java 运行时", JobStatus.Running);
            var java = await ctx.Java.EnsureJavaAsync(17, null, ct);

            var vanillaJar = ctx.Paths.VersionJarPath(build.GameVersion);
            if (!File.Exists(vanillaJar))
                throw new IOException($"缺少原版 {build.GameVersion} 的主 jar，无法给 OptiFine 打补丁。");

            j.Report(1, 30, "给主 jar 打补丁", JobStatus.Running);
            var artifacts = await OptiFinePatcher.PatchAsync(
                java.ExecutablePath, installerPath, vanillaJar, ctx.Paths.LibrariesDir, build.MavenVersion, ct);
            j.Report(1, 100, "补丁完成", JobStatus.Succeeded);

            j.Report(2, 40, "写入版本 JSON", JobStatus.Running);

            var versionId = build.VersionId;
            var json = new VersionJson
            {
                Id = versionId,
                InheritsFrom = build.GameVersion,
                MainClass = "net.minecraft.launchwrapper.Launch",
                Type = "release",
                Libraries =
                {
                    new Library { Name = artifacts.OptiFineName },
                    new Library { Name = artifacts.LaunchWrapperName },
                },
                Arguments = new ArgumentsSpec
                {
                    Game =
                    {
                        new ArgumentEntry { Values = { "--tweakClass", "optifine.OptiFineTweaker" } },
                    },
                },
            };

            await ctx.Installer.SaveVersionJsonAsync(versionId, json, ct);

            // 版本目录里得有主 jar，启动器才算它装好了。
            // OptiFine 的补丁是往库jar里打的，主 jar 沿用原版那份。
            Directory.CreateDirectory(ctx.Paths.VersionDir(versionId));
            File.Copy(vanillaJar, ctx.Paths.VersionJarPath(versionId), overwrite: true);

            j.Report(2, 100, $"已建立 {versionId}", JobStatus.Succeeded);
            ctx.RefreshInstalled();
            MarkIsolation(ctx, versionId, json);
        };

        return job;
    }
}
