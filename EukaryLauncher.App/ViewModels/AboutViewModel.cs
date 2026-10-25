using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.ViewModels;

/// <summary>
/// 关于页。合规要求：必须明确声明与 Mojang 无关联，并标注资源来源。
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private const string RepositoryUrl = "https://github.com/EukaryDeveloper/Eukary-Launcher";
    private const string BmclapiUrl = "https://bmclapidoc.bangbang93.com/";
    private const string AdoptiumUrl = "https://adoptium.net/";
    private const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    public string Version => "0.2.0 (M2)";
    public string Disclaimer => "本启动器与 Microsoft / Mojang 无任何关联。Minecraft 是 Mojang Synergies AB 的商标。";
    public string License => "GPL-3.0";
    public string Author => "DL_Hachi";
    public string SourceNote => "游戏本体、依赖库与资源均来自 Mojang 官方或 BMCLAPI 镜像，本启动器不分发任何游戏文件。";

    public string Repository => "github.com/EukaryDeveloper/Eukary-Launcher";
    public string DataDirectory => GamePaths.AppDataRoot;
    public string RuntimeText => $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSArchitecture}";

    public string RepositoryLink => RepositoryUrl;
    public string BmclapiLink => BmclapiUrl;
    public string AdoptiumLink => AdoptiumUrl;
    public string LicenseLink => LicenseUrl;

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

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(GamePaths.AppDataRoot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                ArgumentList = { GamePaths.AppDataRoot },
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打不开就不打
        }
    }

    [RelayCommand]
    private void CopyRepository()
    {
        try
        {
            System.Windows.Clipboard.SetText(RepositoryUrl);
            AppServices.Toasts.Show("已复制仓库地址", ToastKind.Success);
        }
        catch (Exception ex)
        {
            AppServices.Toasts.Show("复制失败：" + ex.Message, ToastKind.Error);
        }
    }
}
