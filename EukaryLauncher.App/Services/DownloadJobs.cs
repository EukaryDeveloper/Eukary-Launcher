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
            await ctx.Installer.InstallAsync(entry, DownloadManager.RouteByStage(j), ct);
            ctx.RefreshInstalled();
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
}
