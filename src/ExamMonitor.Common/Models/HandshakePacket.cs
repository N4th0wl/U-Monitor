// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\HandshakePacket.cs
namespace ExamMonitor.Common.Models;

public sealed record HandshakePacket
{
    public string ClientId { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string StudentId { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public DateTimeOffset ConnectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
