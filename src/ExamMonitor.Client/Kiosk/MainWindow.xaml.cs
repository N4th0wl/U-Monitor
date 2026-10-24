// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Kiosk\MainWindow.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ExamMonitor.Client.Services;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Client.Kiosk;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush OnlineDotBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush StandbyDotBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private readonly DiscoveryListenerService _discoveryListener;
    private readonly HookMonitorService _hookMonitor;
    private readonly TelemetryService _telemetry;
    private LockOverlayWindow? _lockOverlay;
    private bool _isConnectingOrConnected;

    public MainWindow()
    {
        InitializeComponent();

        string studentId = Environment.GetEnvironmentVariable("EXAM_STUDENT_ID") ?? $"NIM-{Environment.UserName.ToUpperInvariant()}";
        string studentName = Environment.GetEnvironmentVariable("EXAM_STUDENT_NAME") ?? Environment.UserName;
        string clientId = $"{Environment.MachineName}-{studentId}";

        WorkstationIdTextBlock.Text = Environment.MachineName;
        WorkstationInfoTextBlock.Text = $"OS: {Environment.OSVersion.Version}";
        StudentIdTextBox.Text = studentId;
        StudentNameTextBox.Text = studentName;

        _hookMonitor = new HookMonitorService();
        _discoveryListener = new DiscoveryListenerService();
        _telemetry = new TelemetryService(clientId, studentId, studentName, new ScreenCaptureService(), _hookMonitor);

        _discoveryListener.ServerDiscovered += OnServerDiscovered;
        _telemetry.CommandReceived += OnServerCommandReceived;
        _telemetry.ConnectionStateChanged += OnConnectionStateChanged;
        _telemetry.ActiveWindowChanged += OnActiveWindowChanged;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hookMonitor.InstallKeyboardHook();
        _discoveryListener.Start();

        await TryConnectToServerAsync("127.0.0.1", ProtocolConstants.DefaultServerTcpPort);
    }

    private void OnServerDiscovered(DiscoveryPacket packet)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (!_telemetry.IsConnected && !_isConnectingOrConnected)
            {
                ServerIpTextBox.Text = packet.ServerIp;
                ServerPortTextBox.Text = packet.TcpPort.ToString();
                await TryConnectToServerAsync(packet.ServerIp, packet.TcpPort);
            }
        });
    }

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        _telemetry.UpdateIdentity(StudentIdTextBox.Text, StudentNameTextBox.Text);
        string ip = string.IsNullOrWhiteSpace(ServerIpTextBox.Text) ? "127.0.0.1" : ServerIpTextBox.Text.Trim();
        if (!int.TryParse(ServerPortTextBox.Text, out int port) || port <= 0 || port > 65535)
        {
            port = ProtocolConstants.DefaultServerTcpPort;
        }

        _isConnectingOrConnected = false;
        await TryConnectToServerAsync(ip, port);
    }

    private async Task TryConnectToServerAsync(string serverIp, int tcpPort)
    {
        if (_isConnectingOrConnected)
        {
            return;
        }

        _telemetry.UpdateIdentity(StudentIdTextBox.Text, StudentNameTextBox.Text);
        _isConnectingOrConnected = true;
        ConnectionStatusTextBlock.Text = $"Connecting to {serverIp}:{tcpPort}...";
        FooterStatusTextBlock.Text = $"Connecting to {serverIp}:{tcpPort}...";

        try
        {
            bool connected = await _telemetry.ConnectAsync(serverIp, tcpPort);
            if (!connected)
            {
                _isConnectingOrConnected = false;
                ConnectionStatusTextBlock.Text = $"Ready. Enter Proctor Server IP ({serverIp}:{tcpPort}) and click Connect.";
                FooterStatusTextBlock.Text = "Ready — Waiting for Proctor Server on LAN (UDP :45454)";
            }
        }
        catch
        {
            _isConnectingOrConnected = false;
        }
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        _isConnectingOrConnected = isConnected;
        Dispatcher.BeginInvoke(() =>
        {
            StatusDot.Fill = isConnected ? OnlineDotBrush : StandbyDotBrush;
            ConnectionBadgeTextBlock.Text = isConnected ? "ONLINE" : "STANDBY";
            ConnectButton.Content = isConnected ? "Reconnect" : "Connect";
            ConnectionStatusTextBlock.Text = isConnected
                ? $"Sharing live desktop with {ServerIpTextBox.Text}:{ServerPortTextBox.Text}"
                : "Not connected. Waiting for Proctor Server announcement or manual IP connection.";
            FooterStatusTextBlock.Text = isConnected
                ? $"Connected to {ServerIpTextBox.Text}:{ServerPortTextBox.Text} • Live screen stream active"
                : "Ready — Listening for LAN Server on UDP :45454";
        });
    }

    private void OnActiveWindowChanged(string activeWindowDescription)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ActiveWindowTextBlock.Text = activeWindowDescription;
        });
    }

    private void OnServerCommandReceived(CommandPacket command)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (command.CommandType)
            {
                case ServerCommandType.LockScreen:
                    ShowLockOverlay(command.Message);
                    break;

                case ServerCommandType.UnlockScreen:
                    HideLockOverlay();
                    break;

                case ServerCommandType.SendBroadcastMessage:
                    BroadcastMessageTextBlock.Text = $"Message from Proctor: {command.Message}";
                    BroadcastBanner.Visibility = Visibility.Visible;
                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                    }
                    Activate();
                    break;

                case ServerCommandType.TerminateApp:
                    HideLockOverlay();
                    Application.Current.Shutdown();
                    break;
            }
        });
    }

    private void ShowLockOverlay(string? message)
    {
        if (_lockOverlay is null)
        {
            _lockOverlay = new LockOverlayWindow();
            _lockOverlay.SetLockMessage(message);
            _lockOverlay.Show();
        }
        else
        {
            _lockOverlay.SetLockMessage(message);
            _lockOverlay.Activate();
        }
    }

    private void HideLockOverlay()
    {
        if (_lockOverlay is not null)
        {
            _lockOverlay.UnlockAndClose();
            _lockOverlay = null;
        }
    }

    private void OnDismissBroadcastClicked(object sender, RoutedEventArgs e)
    {
        BroadcastBanner.Visibility = Visibility.Collapsed;
    }

    private void OnMinimizeToBackgroundClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private async void OnExitExamClicked(object sender, RoutedEventArgs e)
    {
        await _telemetry.SendTerminatingHeartbeatAsync();
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        HideLockOverlay();
        _hookMonitor.Dispose();
        await _telemetry.SendTerminatingHeartbeatAsync();
        await _telemetry.DisposeAsync();
        await _discoveryListener.DisposeAsync();
        base.OnClosing(e);
    }
}
