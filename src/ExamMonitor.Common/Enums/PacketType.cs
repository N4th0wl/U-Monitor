// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Enums\PacketType.cs
namespace ExamMonitor.Common.Enums;

public enum PacketType : byte
{
    Unknown = 0,
    Handshake = 1,
    Heartbeat = 2,
    Frame = 3,
    Command = 4,
    Discovery = 5
}
