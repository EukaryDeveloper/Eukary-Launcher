using System.Windows;
using EukaryLauncher.App.Services;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(GamePaths.AppDataRoot);
        Directory.CreateDirectory(GamePaths.LogsRoot);

        AppServices.Initialize();
        Services.LogService.Instance.Info("真核启动器启动");

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        DispatcherUnhandledException += (_, args) =>
        {
            Services.LogService.Instance.Error("[UI] 未处理异常：" + args.Exception.Message);
            MessageBox.Show(
                args.Exception.Message,
                "真核启动器出错了",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
