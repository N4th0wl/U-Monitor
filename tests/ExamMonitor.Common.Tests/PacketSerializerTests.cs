// c:\Users\ivand\Documents\Coding\Project\U-Monitor\tests\ExamMonitor.Common.Tests\PacketSerializerTests.cs
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;

namespace ExamMonitor.Common.Tests;

public sealed class PacketSerializerTests
{
    public async Task HandshakePacket_RoundTrip_PreservesAllFields()
    {
        var original = new HandshakePacket
        {
            ClientId = "PC01-STU101",
            MachineName = "LAB-PC01",
            IpAddress = "192.168.1.101",
            StudentId = "STU101",
            StudentName = "Budi Santoso",
            OsVersion = "Windows 11 Pro 23H2",
            ConnectedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(1730000000000)
        };

        using var ms = new MemoryStream();
        await PacketSerializer.WritePacketAsync(ms, PacketType.Handshake, original);
        ms.Position = 0;

        PacketEnvelope? envelope = await PacketSerializer.ReadPacketAsync(ms);
        TestAssert.NotNull(envelope);
        TestAssert.Equal(PacketType.Handshake, envelope!.Type);

        HandshakePacket decoded = PacketSerializer.DeserializePayload<HandshakePacket>(envelope.Type, envelope.Payload);
        TestAssert.Equal(original.ClientId, decoded.ClientId);
        TestAssert.Equal(original.MachineName, decoded.MachineName);
        TestAssert.Equal(original.IpAddress, decoded.IpAddress);
        TestAssert.Equal(original.StudentId, decoded.StudentId);
        TestAssert.Equal(original.StudentName, decoded.StudentName);
        TestAssert.Equal(original.OsVersion, decoded.OsVersion);
        TestAssert.Equal(original.ConnectedAtUtc, decoded.ConnectedAtUtc);
    }

    public void HeartbeatPacket_RoundTrip_PreservesStatusAndQuestionIndex()
    {
        var original = new HeartbeatPacket
        {
            ClientId = "PC02-STU102",
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1730000005000),
            Status = ClientStatus.FocusLost,
            CurrentQuestionIndex = 14,
            ActiveWindowTitle = "Task Manager"
        };

        byte[] wire = PacketSerializer.Serialize(PacketType.Heartbeat, original);
        HeartbeatPacket decoded = PacketSerializer.Deserialize<HeartbeatPacket>(wire, out PacketType type);

        TestAssert.Equal(PacketType.Heartbeat, type);
        TestAssert.Equal(original.ClientId, decoded.ClientId);
        TestAssert.Equal(ClientStatus.FocusLost, decoded.Status);
        TestAssert.Equal(14, decoded.CurrentQuestionIndex);
        TestAssert.Equal("Task Manager", decoded.ActiveWindowTitle);
    }

    public void FramePacket_BinaryFraming_RoundTripsRawJpegAndResolution()
    {
        byte[] sampleJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9];
        var original = new FramePacket
        {
            ClientId = "PC03-STU103",
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1730000010000),
            JpegPayload = sampleJpeg,
            Width = 1920,
            Height = 1080,
            IsHighResolution = true
        };

        byte[] wire = PacketSerializer.Serialize(PacketType.Frame, original);
        FramePacket decoded = PacketSerializer.Deserialize<FramePacket>(wire, out PacketType type);

        TestAssert.Equal(PacketType.Frame, type);
        TestAssert.Equal(original.ClientId, decoded.ClientId);
        TestAssert.Equal(original.Timestamp, decoded.Timestamp);
        TestAssert.Equal(1920, decoded.Width);
        TestAssert.Equal(1080, decoded.Height);
        TestAssert.True(decoded.IsHighResolution);
        TestAssert.SequenceEqual(sampleJpeg, decoded.JpegPayload);
    }

    public void CommandPacket_RoundTrip_PreservesServerInstruction()
    {
        var original = new CommandPacket
        {
            CommandId = "cmd-99",
            TargetClientId = "PC01-STU101",
            CommandType = ServerCommandType.LockScreen,
            Message = "Eyes on your own monitor"
        };

        byte[] wire = PacketSerializer.Serialize(PacketType.Command, original);
        CommandPacket decoded = PacketSerializer.Deserialize<CommandPacket>(wire, out PacketType type);

        TestAssert.Equal(PacketType.Command, type);
        TestAssert.Equal("cmd-99", decoded.CommandId);
        TestAssert.Equal("PC01-STU101", decoded.TargetClientId);
        TestAssert.Equal(ServerCommandType.LockScreen, decoded.CommandType);
        TestAssert.Equal("Eyes on your own monitor", decoded.Message);
    }

    public void DiscoveryPacket_RoundTrip_PreservesServerEndpoint()
    {
        var original = new DiscoveryPacket
        {
            MagicHeader = ProtocolConstants.DiscoveryMagicHeader,
            ServerName = "ADMIN-CONSOLE-01",
            ServerIp = "192.168.100.1",
            TcpPort = 45455
        };

        byte[] wire = PacketSerializer.Serialize(PacketType.Discovery, original);
        DiscoveryPacket decoded = PacketSerializer.Deserialize<DiscoveryPacket>(wire, out PacketType type);

        TestAssert.Equal(PacketType.Discovery, type);
        TestAssert.Equal(ProtocolConstants.DiscoveryMagicHeader, decoded.MagicHeader);
        TestAssert.Equal("ADMIN-CONSOLE-01", decoded.ServerName);
        TestAssert.Equal("192.168.100.1", decoded.ServerIp);
        TestAssert.Equal(45455, decoded.TcpPort);
    }

    public async Task MultiplePackets_InSingleStream_ReadSequentiallyAndHandleCleanEof()
    {
        using var ms = new MemoryStream();
        await PacketSerializer.WritePacketAsync(ms, PacketType.Heartbeat, new HeartbeatPacket
        {
            ClientId = "C1",
            Status = ClientStatus.Normal,
            CurrentQuestionIndex = 1
        });
        await PacketSerializer.WritePacketAsync(ms, PacketType.Frame, new FramePacket
        {
            ClientId = "C1",
            Width = 360,
            Height = 202,
            IsHighResolution = false,
            JpegPayload = [1, 2, 3, 4]
        });

        ms.Position = 0;

        PacketEnvelope? first = await PacketSerializer.ReadPacketAsync(ms);
        PacketEnvelope? second = await PacketSerializer.ReadPacketAsync(ms);
        PacketEnvelope? eof = await PacketSerializer.ReadPacketAsync(ms);

        TestAssert.NotNull(first);
        TestAssert.Equal(PacketType.Heartbeat, first!.Type);

        TestAssert.NotNull(second);
        TestAssert.Equal(PacketType.Frame, second!.Type);

        TestAssert.True(eof is null, "Expected null at clean EOF.");
    }

    public void Unwrap_ThrowsInvalidDataException_OnTruncatedOrCorruptHeader()
    {
        bool threwOnShort = false;
        try
        {
            PacketSerializer.Unwrap([0x01, 0x02]);
        }
        catch (InvalidDataException)
        {
            threwOnShort = true;
        }
        TestAssert.True(threwOnShort, "Expected InvalidDataException on short buffer.");
    }
}
