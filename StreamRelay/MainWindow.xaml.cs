using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace StreamRelay;

public partial class MainWindow : Window
{
    private sealed record StreamRow(string Path, Grid Container, TextBox Source, TextBox Output, Button Remove);

    private readonly List<StreamRow> _rows = [];
    private readonly Forms.NotifyIcon _tray;
    private RelayServer? _server;
    private bool _isStarting;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        Forms.ContextMenuStrip menu = new();
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
            StartupBox.IsChecked = RelaySettings.HasLoginStartup();
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
    }

    internal async void StartFromLogin()
    {
        if (_rows.Exists(row => !string.IsNullOrWhiteSpace(row.Source.Text)))
            await StartRelayAsync();

        if (_server is null && !_isExiting)
            ShowFromTray();
    }

    internal void ReleaseResources()
    {
        _isExiting = true;
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

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        bool enabled = StartupBox.IsChecked == true;
        try
        {
            RelaySettings.SetLoginStartup(enabled);
            Status.Text = enabled ? "已啟用登入時自動啟動。" : "已停用登入時自動啟動。";
        }
        catch (Exception)
        {
            StartupBox.IsChecked = !enabled;
            Status.Text = "無法更新 Windows 自動啟動設定。";
        }
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

    private void OnEndpointChanged(object sender, RoutedEventArgs e) => RefreshLinks();

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
        if (_server is not null)
            return;

        RelayServer server = new();
        _server = server;
        _isStarting = true;
        UpdateControls();
        Status.Text = "正在啟動 MediaMTX…";
        server.Exited += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            if (!_isStarting && ReferenceEquals(_server, server))
                StopRelay("MediaMTX 已結束，可重新啟動轉發。");
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
                Status.Text += " 無法儲存設定，下次啟動可能需要重新輸入。";
            }
        }
        catch (InvalidOperationException ex)
        {
            StopRelay(ex.Message);
        }
        catch (TimeoutException)
        {
            StopRelay("等待 MediaMTX 啟動逾時。");
        }
        catch (Exception)
        {
            StopRelay("無法啟動 MediaMTX，請確認程式檔案及本機設定。");
        }
        finally
        {
            _isStarting = false;
            UpdateControls();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopRelay("轉發已停止。");

    private void StopRelay(string message)
    {
        RelayServer? server = _server;
        _server = null;
        server?.Dispose();
        Status.Text = message;
        UpdateControls();
    }

    private void UpdateControls()
    {
        bool canEdit = _server is null;
        EndpointPanel.IsEnabled = canEdit;
        AddButton.IsEnabled = canEdit;
        StartButton.IsEnabled = canEdit;
        StopButton.IsEnabled = _server is not null && !_isStarting;
        foreach (StreamRow row in _rows)
        {
            row.Source.IsReadOnly = !canEdit;
            row.Remove.IsEnabled = canEdit;
        }
    }
}
