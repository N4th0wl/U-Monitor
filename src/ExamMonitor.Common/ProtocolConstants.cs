// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\ProtocolConstants.cs
namespace ExamMonitor.Common;

public static class ProtocolConstants
{
    public const int DiscoveryUdpPort = 45454;
    public const int DefaultServerTcpPort = 45455;
    public const string DiscoveryMagicHeader = "EXAMMONITOR_DISCOVERY_V1";
    public const int HeartbeatIntervalMs = 1500;
    public const int HeartbeatTimeoutSeconds = 5;
    public const int MaxPacketSizeBytes = 16 * 1024 * 1024; // 16 MB max frame/packet payload
    public const int DefaultThumbnailWidth = 480;
    public const int DefaultThumbnailHeight = 270;
    public const int DefaultThumbnailJpegQuality = 65;
    public const int HighResJpegQuality = 80;
}
