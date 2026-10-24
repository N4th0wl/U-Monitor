// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Models\PacketEnvelope.cs
using ExamMonitor.Common.Enums;

namespace ExamMonitor.Common.Models;

public sealed record PacketEnvelope(PacketType Type, byte[] Payload);
