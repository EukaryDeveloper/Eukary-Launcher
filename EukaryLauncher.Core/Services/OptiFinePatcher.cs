using System.IO.Compression;

namespace EukaryLauncher.Core.Services;

/// <summary>OptiFine 打完补丁之后的两件产物：OptiFine 本体 + 它依赖的 launchwrapper。</summary>
public sealed record OptiFineArtifacts(string OptiFineName, string LaunchWrapperName);

/// <summary>
/// 把 OptiFine 的安装包变成能直接用的依赖库。
/// 安装包里带着 <c>optifine.Patcher</c>：只有跑它，才会产出真正打过补丁的 OptiFine jar——
/// 直接把安装包丢进 classpath 是不行的。
/// </summary>
public static class OptiFinePatcher
{
    private const string FallbackLaunchWrapper = "net.minecraft:launchwrapper:1.12";

    /// <summary>
    /// 产出 OptiFine 库与 launchwrapper 库，返回它们的 maven 坐标。
    /// </summary>
    /// <param name="javaExe">用来跑 Patcher 的 Java（安装包自己的版本要求不高，有 17 就行）。</param>
    /// <param name="installerJar">从 BMCLAPI 下载下来的安装包。</param>
    /// <param name="vanillaJar">原版主 jar，Patcher 要往它上面打补丁。</param>
    /// <param name="librariesDir">.minecraft/libraries。</param>
    /// <param name="mavenVersion">形如 1.20.4_HD_U_I7。</param>
    public static async Task<OptiFineArtifacts> PatchAsync(
        string javaExe,
        string installerJar,
        string vanillaJar,
        string librariesDir,
        string mavenVersion,
        CancellationToken ct = default)
    {
        var optiFineName = $"optifine:OptiFine:{mavenVersion}";
        var optiFinePath = Path.Combine(librariesDir,
            VersionInstaller.MavenPathFromName(optiFineName).Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(optiFinePath)!);

        if (Contains(installerJar, "optifine/Patcher.class"))
        {
            // Patcher 的三个参数：原版 jar、安装包自己、产物 jar
            var code = await ProcessRunner.RunAsync(
                javaExe,
                new[] { "-cp", installerJar, "optifine.Patcher", vanillaJar, installerJar, optiFinePath },
                Path.GetDirectoryName(installerJar) ?? ".",
                null,
                ct);

            if (code != 0)
                throw new IOException($"OptiFine 补丁程序失败（退出码 {code}）。");
        }
        else
        {
            // 很老的 OptiFine 没有 Patcher，安装包本身就是成品
            File.Copy(installerJar, optiFinePath, overwrite: true);
        }

        // 去掉 Forge 的 mod 描述：作为库加载时不能再被 Forge 当成普通 mod 扫一遍
        RemoveEntry(optiFinePath, "META-INF/mods.toml");

        return new OptiFineArtifacts(optiFineName, ExtractLaunchWrapper(installerJar, librariesDir));
    }

    /// <summary>
    /// OptiFine 1.13+ 自带改过的 launchwrapper（原版那份已经跟不上它了）。
    /// 安装包里没有就退回原版 launchwrapper。
    /// </summary>
    private static string ExtractLaunchWrapper(string installerJar, string librariesDir)
    {
        var text = ReadTextEntry(installerJar, "launchwrapper-of.txt")?.Trim();

        string? name = null;
        string? entryName = null;

        if (!string.IsNullOrEmpty(text))
        {
            name = $"optifine:launchwrapper-of:{text}";
            entryName = $"launchwrapper-of-{text}.jar";
        }
        else if (Contains(installerJar, "launchwrapper-2.0.jar"))
        {
            name = "optifine:launchwrapper:2.0";
            entryName = "launchwrapper-2.0.jar";
        }

        if (name is null || entryName is null || !Contains(installerJar, entryName))
            return FallbackLaunchWrapper;

        var dest = Path.Combine(librariesDir,
            VersionInstaller.MavenPathFromName(name).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        using var zip = ZipFile.OpenRead(installerJar);
        zip.GetEntry(entryName)?.ExtractToFile(dest, overwrite: true);

        return name;
    }

    private static bool Contains(string jarPath, string entryName)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            return zip.GetEntry(entryName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadTextEntry(string jarPath, string entryName)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry(entryName);
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveEntry(string jarPath, string entryName)
    {
        try
        {
            using var zip = ZipFile.Open(jarPath, ZipArchiveMode.Update);
            zip.GetEntry(entryName)?.Delete();
        }
        catch
        {
            // 没有这一项，或者包是只读的——都不影响后面的步骤
        }
    }
}
