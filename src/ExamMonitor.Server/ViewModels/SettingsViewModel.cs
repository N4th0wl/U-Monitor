// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\ViewModels\SettingsViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using ExamMonitor.Common;

namespace ExamMonitor.Server.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private int _udpDiscoveryPort = ProtocolConstants.DiscoveryUdpPort;
    private int _tcpListenPort = ProtocolConstants.DefaultServerTcpPort;
    private int _heartbeatTimeoutSeconds = ProtocolConstants.HeartbeatTimeoutSeconds;
    private int _gridColumns = 4;
    private string _roomName = "LAB-A101";

    public int UdpDiscoveryPort
    {
        get => _udpDiscoveryPort;
        set => SetProperty(ref _udpDiscoveryPort, value);
    }

    public int TcpListenPort
    {
        get => _tcpListenPort;
        set => SetProperty(ref _tcpListenPort, value);
    }

    public int HeartbeatTimeoutSeconds
    {
        get => _heartbeatTimeoutSeconds;
        set => SetProperty(ref _heartbeatTimeoutSeconds, value);
    }

    public int GridColumns
    {
        get => _gridColumns;
        set => SetProperty(ref _gridColumns, Math.Clamp(value, 1, 8));
    }

    public string RoomName
    {
        get => _roomName;
        set => SetProperty(ref _roomName, value);
    }
}
