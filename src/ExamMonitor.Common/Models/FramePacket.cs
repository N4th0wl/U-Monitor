// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\FramePacket.cs
namespace ExamMonitor.Common.Models;

public sealed record FramePacket
{
    public string ClientId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public byte[] JpegPayload { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsHighResolution { get; init; }
}
