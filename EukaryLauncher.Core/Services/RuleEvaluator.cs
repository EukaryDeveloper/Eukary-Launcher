using System.Runtime.InteropServices;
using EukaryLauncher.Core.Models;

namespace EukaryLauncher.Core.Services;

/// <summary>
/// 版本 JSON 的 rules 求值。官方语义：默认 disallow，逐条匹配，最后一条命中的规则决定结果。
/// </summary>
public static class RuleEvaluator
{
    /// <summary>当前进程在版本 JSON 里的 OS 名字。</summary>
    public static string CurrentOsName { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
        : "linux";

    /// <summary>版本 JSON 用的架构标识（老版本是 x86 / x86_64）。</summary>
    public static string CurrentArch { get; } =
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64",
        };

    public static bool IsAllowed(
        IEnumerable<Rule>? rules,
        bool isDemoUser = false,
        bool hasCustomResolution = false)
    {
        if (rules is null) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (Matches(rule, isDemoUser, hasCustomResolution))
                allowed = rule.IsAllow;
        }
        return allowed;
    }

    private static bool Matches(Rule rule, bool isDemoUser, bool hasCustomResolution)
    {
        if (rule.Os is not null && !MatchesOs(rule.Os))
            return false;

        if (rule.Features is { Count: > 0 })
        {
            foreach (var (key, expected) in rule.Features)
            {
                var actual = key switch
                {
                    "is_demo_user" => isDemoUser,
                    "has_custom_resolution" => hasCustomResolution,
                    _ => false,
                };
                if (actual != expected) return false;
            }
        }

        return true;
    }

    private static bool MatchesOs(OsCondition os)
    {
        if (os.Name is { Length: > 0 } name)
        {
            // 历史上有三种写法：osx / macos / darwin
            var normalized = name.ToLowerInvariant() switch
            {
                "osx" or "macos" or "darwin" or "mac" => "osx",
                "win" or "windows" => "windows",
                "linux" => "linux",
                _ => name.ToLowerInvariant(),
            };
            if (!string.Equals(normalized, CurrentOsName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (os.Arch is { Length: > 0 } arch)
        {
            // 老版本 JSON 里 64 位写作 x86_64，这里两种都接受
            var ok = arch.Equals(CurrentArch, StringComparison.OrdinalIgnoreCase)
                     || (arch == "x86_64" && CurrentArch == "x64")
                     || (arch == "amd64" && CurrentArch == "x64")
                     || (arch == "aarch64" && CurrentArch == "arm64");
            if (!ok) return false;
        }

        if (os.Version is { Length: > 0 } version)
        {
            // 正则匹配系统版本，绝大多数情况下不设置；匹配失败按不匹配处理
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(Environment.OSVersion.VersionString, version))
                    return false;
            }
            catch (System.Text.RegularExpressions.RegexParseException)
            {
                return false;
            }
        }

        return true;
    }
}
