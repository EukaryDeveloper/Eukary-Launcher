using System.IO.Compression;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// 老 Minecraft（jar 里带 lang/ 目录的那批，1.10- 及更早）的语言文件兼容处理。
/// </summary>
internal static class LegacyCompat
{
    /// <summary>
    /// 修正 options.txt 里 lang 值的大小写。
    /// 现代 MC（1.11+）把语言写成全小写（zh_cn），老 MC 会按大小写敏感去 jar 里找
    /// lang/zh_cn.lang——同一个游戏目录里新旧版本混装时，老版本读到小写值必崩
    /// （InputStreamReader(null) 的 NPE，StringTranslate 不捕获它）。
    /// jar 里没有 lang/ 目录的（现代版本）直接跳过，不影响任何现有行为。
    /// </summary>
    public static void NormalizeLangCase(string versionJar, string gameDir)
    {
        var optionsPath = Path.Combine(gameDir, "options.txt");
        if (!File.Exists(versionJar) || !File.Exists(optionsPath)) return;

        string[] langFiles;
        try
        {
            using var zip = ZipFile.OpenRead(versionJar);
            langFiles = zip.Entries
                .Select(e => e.FullName)
                .Where(n => n.StartsWith("lang/", StringComparison.Ordinal)
                            && n.EndsWith(".lang", StringComparison.Ordinal))
                .ToArray();
        }
        catch
        {
            return; // jar 读不了就别动用户文件
        }
        if (langFiles.Length == 0) return; // 现代版本：lang 在 assets 里，不管

        string[] lines;
        try { lines = File.ReadAllLines(optionsPath); }
        catch { return; }

        var idx = Array.FindIndex(lines, l => l.StartsWith("lang:", StringComparison.Ordinal));
        if (idx < 0) return;

        var value = lines[idx]["lang:".Length..].Trim();
        if (value.Length == 0) return;

        var match = langFiles.FirstOrDefault(n => string.Equals(
            Path.GetFileName(n), value + ".lang", StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            var proper = Path.GetFileNameWithoutExtension(match);
            if (!string.Equals(proper, value, StringComparison.Ordinal))
            {
                lines[idx] = "lang:" + proper;
            }
            else
            {
                return; // 大小写已经对，不用写
            }
        }
        else
        {
            // 老 MC 对不存在的语言文件会直接 NPE，找不到就退回 en_US（老 jar 必有）
            if (string.Equals(value, "en_US", StringComparison.Ordinal)) return;
            lines[idx] = "lang:en_US";
        }

        try { File.WriteAllLines(optionsPath, lines); }
        catch { /* 写不进去就算了，游戏会报错但至少提示明确 */ }
    }
}
