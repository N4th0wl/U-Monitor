// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\CommandPacket.cs
using ExamMonitor.Common.Enums;

namespace ExamMonitor.Common.Models;

public sealed record CommandPacket
{
    public string CommandId { get; init; } = Guid.NewGuid().ToString("N");
    public string TargetClientId { get; init; } = "*";
    public ServerCommandType CommandType { get; init; }
    public string Message { get; init; } = string.Empty;
    public double NormalizedX { get; init; }
    public double NormalizedY { get; init; }
    public int MouseButton { get; init; } // 0=Move, 1=LeftDown, 2=LeftUp, 3=RightDown, 4=RightUp
    public int VirtualKey { get; init; }
    public bool IsKeyUp { get; init; }
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
