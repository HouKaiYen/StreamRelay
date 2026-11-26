using System.Threading;
using System.Windows;

namespace StreamRelay;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is [BootStartup.EnableArgument] or [BootStartup.DisableArgument])
        {
            Shutdown(BootStartup.RunElevated(e.Args[0] == BootStartup.EnableArgument));
            return;
        }

        BootStartup.RemoveLegacyLoginEntry();
        if (e.Args is ["--startup"])
        {
            Shutdown();
            return;
        }

        _instance = new Mutex(false, @"Local\StreamRelay", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("StreamRelay 已在執行，可從 Windows 系統匣開啟。", "StreamRelay");
            Shutdown();
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
            window.ReleaseResources();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
