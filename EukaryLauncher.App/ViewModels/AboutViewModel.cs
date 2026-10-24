using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>
/// 关于页。合规要求：必须明确声明与 Mojang 无关联，并标注资源来源。
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public string Version => "0.1.0 (M1)";
    public string Disclaimer => "本启动器与 Microsoft / Mojang 无任何关联。Minecraft 是 Mojang Synergies AB 的商标。";
    public string License => "GPL-3.0";
    public string Author => "DL_Hachi";
    public string SourceNote => "游戏本体、依赖库与资源均来自 Mojang 官方或 BMCLAPI 镜像，本启动器不分发任何游戏文件。";

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打不开就不打
        }
    }
}
