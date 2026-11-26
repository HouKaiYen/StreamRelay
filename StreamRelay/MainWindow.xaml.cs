using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace StreamRelay;

public partial class MainWindow : Window
{
    private sealed record StreamRow(string Path, Grid Container, TextBox Source, TextBox Output, Button Remove);

    private readonly List<StreamRow> _rows = [];
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripLabel _trayEndpoint = new();
    private readonly Forms.ToolStripLabel _trayStatus = new();
    private readonly DispatcherTimer _trayRefresh = new() { Interval = TimeSpan.FromSeconds(4) };
    private RelayServer? _server;
    private BootTaskStatus _bootTaskStatus;
    private bool _isStarting;
    private bool _hasRelayError;
    private bool _isExiting;
    private bool _isBootEnabled;
    private bool _isChangingBootStartup;
    private bool _canReadBootTask;

    public MainWindow()
    {
        InitializeComponent();
        PortableSettingsPathBox.Text = RelaySettings.SettingsPath;
        BootSettingsPathBox.Text = BootStartup.ConfigurationPath;
        UsernameBox.Text = RelaySettings.DefaultCredential;
        PasswordBox.Password = RelaySettings.DefaultCredential;
        Forms.ContextMenuStrip menu = new();
        menu.Items.AddRange([_trayEndpoint, _trayStatus, new Forms.ToolStripSeparator()]);
        menu.Opening += (_, _) => RefreshBootStartup();
        menu.Items.Add("開啟 StreamRelay", null, (_, _) => ShowFromTray());
        menu.Items.Add("結束 StreamRelay", null, (_, _) =>
        {
            _isExiting = true;
            Application.Current.Shutdown();
        });
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "StreamRelay",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        try
        {
            if (RelaySettings.Load() is RelaySettings settings)
            {
                IpBox.Text = settings.Ip;
                PortBox.Text = settings.Port.ToString();
                UsernameBox.Text = settings.Username;
                PasswordBox.Password = settings.Password;
                foreach (RelayStream stream in settings.Streams)
                    AddRow(stream.Source, stream.Path);
            }
        }
        catch (Exception)
        {
            Status.Text = "無法讀取本機設定，請重新輸入。";
        }

        if (_rows.Count == 0)
            AddRow();

        RefreshBootStartup();
        Activated += (_, _) => RefreshBootStartup();
        _trayRefresh.Tick += (_, _) => RefreshBootStartup();
        _trayRefresh.Start();
    }

    internal void ReleaseResources()
    {
        _isExiting = true;
        _trayRefresh.Stop();
        _server?.Dispose();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
    }

    private void ShowFromTray()
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
            return;

        e.Cancel = true;
        Hide();
    }

    private async void OnStartupClick(object sender, RoutedEventArgs e)
    {
        bool enabled = StartupBox.IsChecked == true;
        _isChangingBootStartup = true;
        UpdateControls();
        try
        {
            if (enabled)
            {
                RelaySettings settings = ReadSettings();
                settings.Validate();
                settings.Save();
            }

            await BootStartup.SetEnabledAsync(enabled);
            _isBootEnabled = enabled;
            Status.Text = enabled ? "已建立開機排程，下次開機時會自動轉發。" : "已停用開機自啟動。";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status.Text = "已取消管理員授權。";
        }
        catch (InvalidOperationException ex)
        {
            Status.Text = ex.Message;
        }
        catch (Exception)
        {
            Status.Text = "無法更新開機自啟動，請確認程式資料夾可寫入，且工作排程器可用。";
        }
        finally
        {
            _isChangingBootStartup = false;
            StartupBox.IsChecked = _isBootEnabled;
            RefreshBootStartup();
        }
    }

    private void RefreshBootStartup()
    {
        if (_isChangingBootStartup || _isExiting)
            return;

        try
        {
            _bootTaskStatus = BootStartup.ReadStatus();
            bool enabled = _bootTaskStatus.IsEnabled;
            if (enabled != _isBootEnabled && _server is null)
                Status.Text = enabled ? "已啟用開機排程，轉發由工作排程器管理。" : "開機排程已停用。";
            _isBootEnabled = enabled;
            _canReadBootTask = true;
        }
        catch (Exception)
        {
            _canReadBootTask = false;
        }

        StartupBox.IsChecked = _isBootEnabled;
        UpdateControls();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddRow().Source.Focus();

    private StreamRow AddRow(string sourceUrl = "", string? path = null)
    {
        path ??= $"stream-{Guid.NewGuid():N}";
        Grid container = new() { Margin = new Thickness(0, 0, 0, 8) };
        container.ColumnDefinitions.Add(new ColumnDefinition());
        container.ColumnDefinitions.Add(new ColumnDefinition());

        TextBox source = new() { Text = sourceUrl, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(source, "來源 RTSP 連結");
        container.Children.Add(source);

        DockPanel outputPanel = new() { Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(outputPanel, 1);
        container.Children.Add(outputPanel);

        TextBox output = new() { IsReadOnly = true };
        AutomationProperties.SetName(output, "轉發 RTSP 連結");
        Button remove = new() { Content = "移除", Margin = new Thickness(6, 0, 0, 0) };
        Button copy = new() { Content = "複製", Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetName(copy, "複製轉發 RTSP 連結");
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(copy, Dock.Right);
        outputPanel.Children.Add(remove);
        outputPanel.Children.Add(copy);
        outputPanel.Children.Add(output);

        StreamRow row = new(path, container, source, output, remove);
        source.TextChanged += OnEndpointChanged;
        remove.Click += (_, _) =>
        {
            _rows.Remove(row);
            RowsPanel.Children.Remove(row.Container);
            if (_rows.Count == 0)
                AddRow();
        };
        copy.Click += (_, _) =>
        {
            if (output.Text.Length == 0)
                return;
            try
            {
                Clipboard.SetText(output.Text);
                Status.Text = "已複製轉發連結。";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Status.Text = "剪貼簿目前忙碌，請再試一次。";
            }
        };
        _rows.Add(row);
        RowsPanel.Children.Add(container);
        RefreshLinks();
        return row;
    }

    private void OnEndpointChanged(object sender, RoutedEventArgs e)
    {
        RefreshLinks();
        RefreshTrayStatus();
    }

    private RelaySettings ReadSettings()
    {
        string ip = IpBox.Text.Trim();
        if (IPAddress.TryParse(ip, out IPAddress? address))
            ip = address.ToString();
        int.TryParse(PortBox.Text, out int port);
        RelayStream[] streams = _rows.Select(row => new RelayStream(row.Path, row.Source.Text.Trim()))
            .Where(stream => stream.Source.Length > 0)
            .ToArray();
        return new RelaySettings(ip, port, UsernameBox.Text.Trim(), PasswordBox.Password, streams);
    }

    private void RefreshLinks()
    {
        if (PasswordBox is null)
            return;

        RelaySettings settings = ReadSettings();
        bool hasEndpoint = IPAddress.TryParse(settings.Ip, out _) && settings.Port is >= 1 and <= 65535
            && settings.HasValidCredentials;
        foreach (StreamRow row in _rows)
        {
            row.Output.Text = hasEndpoint && RelaySettings.TryParseSource(row.Source.Text.Trim(), out _)
                ? settings.GetOutputUrl(row.Path)
                : string.Empty;
        }
    }

    private async void OnStartClick(object sender, RoutedEventArgs e) => await StartRelayAsync();

    private async Task StartRelayAsync()
    {
        RefreshBootStartup();
        if (_server is not null || _isBootEnabled || _isChangingBootStartup)
            return;

        RelayServer server = new();
        _server = server;
        _isStarting = true;
        _hasRelayError = false;
        UpdateControls();
        Status.Text = "正在啟動 MediaMTX…";
        server.Exited += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            if (!_isStarting && ReferenceEquals(_server, server))
                StopRelay("MediaMTX 已結束，可重新啟動轉發。", hasError: true);
        });

        try
        {
            RelaySettings settings = ReadSettings();
            settings.Validate();
            await server.StartAsync(settings);
            if (_isExiting)
                return;

            Status.Text = $"正在監聽 {settings.Address}，共 {settings.Streams.Length} 路。";
            try
            {
                settings.Save();
            }
            catch (Exception)
            {
                Status.Text += " 無法儲存設定，請將程式移到可寫入的資料夾。";
            }
        }
        catch (InvalidOperationException ex)
        {
            StopRelay(ex.Message, hasError: true);
        }
        catch (TimeoutException)
        {
            StopRelay("等待 MediaMTX 啟動逾時。", hasError: true);
        }
        catch (Exception)
        {
            StopRelay("無法啟動 MediaMTX，請確認程式檔案及本機設定。", hasError: true);
        }
        finally
        {
            _isStarting = false;
            UpdateControls();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopRelay("轉發已停止。");

    private void StopRelay(string message, bool hasError = false)
    {
        RelayServer? server = _server;
        _server = null;
        _hasRelayError = hasError;
        server?.Dispose();
        Status.Text = message;
        UpdateControls();
    }

    private void UpdateControls()
    {
        bool canEdit = _server is null && !_isBootEnabled && !_isChangingBootStartup;
        EndpointPanel.IsEnabled = canEdit;
        AddButton.IsEnabled = canEdit;
        StartButton.IsEnabled = canEdit;
        StartupBox.IsEnabled = _canReadBootTask && _server is null && !_isChangingBootStartup;
        StopButton.IsEnabled = _server is not null && !_isStarting;
        foreach (StreamRow row in _rows)
        {
            row.Source.IsReadOnly = !canEdit;
            row.Remove.IsEnabled = canEdit;
        }

        RefreshTrayStatus();
    }

    private void RefreshTrayStatus()
    {
        if (_tray is null || _isExiting)
            return;

        string address = "未設定";
        if (IPAddress.TryParse(IpBox.Text.Trim(), out IPAddress? ip)
            && int.TryParse(PortBox.Text, out int port) && port is >= 1 and <= 65535)
        {
            address = RelaySettings.FormatAddress(ip.ToString(), port);
        }

        bool isBootManaged = _server is null && (_isBootEnabled || _bootTaskStatus.IsRunning);
        string addressLabel = "轉發端點";
        string status;
        if (isBootManaged)
        {
            IPEndPoint? endpoint = _bootTaskStatus.Endpoint;
            addressLabel = endpoint is null ? "本機設定端點" : addressLabel;
            address = endpoint?.ToString() ?? address;
            status = GetBootTrayStatus(endpoint);
        }
        else if (_server is not null)
        {
            status = _isStarting ? "啟動中" : "轉發中";
        }
        else
        {
            status = _hasRelayError ? "轉發失敗" : "已停止";
        }

        _trayEndpoint.Text = $"{addressLabel} {address}";
        _trayStatus.Text = $"轉發狀態 {status}";
        _tray.Text = $"StreamRelay {status}\n{address}";
    }

    private string GetBootTrayStatus(IPEndPoint? endpoint)
    {
        if (!_canReadBootTask)
            return "排程狀態未知";
        if (!_bootTaskStatus.IsRunning)
            return "開機排程未執行";
        if (endpoint is null)
            return "開機排程執行中";

        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Contains(endpoint)
                ? "轉發中（開機）" : "等待監聽";
        }
        catch (NetworkInformationException)
        {
            return "開機排程執行中";
        }
    }
}
