using System.Threading;
using System.Windows;

namespace StreamRelay;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(false, @"Local\StreamRelay", out bool isFirstInstance);
        bool startsAtLogin = e.Args.Contains("--startup");
        if (!isFirstInstance)
        {
            if (!startsAtLogin)
                MessageBox.Show("StreamRelay 已在執行，可從 Windows 系統匣開啟。", "StreamRelay");
            Shutdown();
            return;
        }

        MainWindow window = new();
        MainWindow = window;
        if (startsAtLogin)
            window.StartFromLogin();
        else
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
