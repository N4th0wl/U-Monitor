// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\HeartbeatPacket.cs
using ExamMonitor.Common.Enums;

namespace ExamMonitor.Common.Models;

public sealed record HeartbeatPacket
{
    public string ClientId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public ClientStatus Status { get; init; } = ClientStatus.Normal;
    public int CurrentQuestionIndex { get; init; }
    public string ActiveWindowTitle { get; init; } = string.Empty;
}
