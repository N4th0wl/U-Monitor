// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\DiscoveryPacket.cs
namespace ExamMonitor.Common.Models;

public sealed record DiscoveryPacket
{
    public string MagicHeader { get; init; } = ProtocolConstants.DiscoveryMagicHeader;
    public string ServerName { get; init; } = Environment.MachineName;
    public string ServerIp { get; init; } = string.Empty;
    public int TcpPort { get; init; } = ProtocolConstants.DefaultServerTcpPort;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
