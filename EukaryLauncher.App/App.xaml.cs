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
            // 处理器自身绝不能再抛：之前这里记日志时若再次触发异常，
            // 无人可捕获，进程会直接终止（用户看到的就是“软件自动关掉”）
            try
            {
                Services.LogService.Instance.Error("[UI] 未处理异常：" + args.Exception);
            }
            catch { /* 记日志失败就算了 */ }

            try
            {
                MessageBox.Show(
                    args.Exception.Message,
                    "真核启动器出错了",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* 弹窗失败就算了 */ }

            args.Handled = true;
        };

        // 非 UI 线程的未处理异常：默认直接终结进程且不留日志，先记下来
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Services.LogService.Instance.Error("[APPDOMAIN] 未处理异常：" + args.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.LogService.Instance.Error("[TASK] 未观察异常：" + args.Exception);
            args.SetObserved();
        };
    }
}
