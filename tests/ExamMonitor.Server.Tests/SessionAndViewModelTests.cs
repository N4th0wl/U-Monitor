// c:\Users\ivand\Documents\Coding\Project\U-Monitor\tests\ExamMonitor.Server.Tests\SessionAndViewModelTests.cs
using System.IO;
using System.Net;
using ExamMonitor.Client.Services;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;
using ExamMonitor.Server.Services;
using ExamMonitor.Server.ViewModels;

namespace ExamMonitor.Server.Tests;

public sealed class SessionAndViewModelTests
{
    public void SessionManager_MarksStudentInactive_WhenHeartbeatExceedsFiveSeconds()
    {
        var sessionManager = new SessionManagerService(TimeSpan.FromSeconds(ProtocolConstants.HeartbeatTimeoutSeconds));
        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        sessionManager.RegisterOrUpdateHandshake(new HandshakePacket
        {
            ClientId = "CLIENT-01",
            MachineName = "PC-01",
            IpAddress = "10.0.0.11",
            StudentId = "STU-01",
            StudentName = "Andi Wijaya",
            OsVersion = "Windows 11"
        }, nowUtc: t0);

        // At +4s (< 5s timeout): still active
        int expiredAt4s = sessionManager.EvaluateHeartbeats(t0.AddSeconds(4));
        ServerTestAssert.Equal(0, expiredAt4s);
        ServerTestAssert.True(sessionManager.Sessions["CLIENT-01"].IsActive);

        // At +5.5s (> 5s timeout): marked inactive
        int expiredAt5_5s = sessionManager.EvaluateHeartbeats(t0.AddSeconds(5.5));
        ServerTestAssert.Equal(1, expiredAt5_5s);
        ServerTestAssert.False(sessionManager.Sessions["CLIENT-01"].IsActive);
        ServerTestAssert.Equal(ClientStatus.Disconnected, sessionManager.Sessions["CLIENT-01"].Status);
    }

    public void DiscoveryBroadcaster_And_DiscoveryListener_ParseAnnouncementCorrectly()
    {
        var broadcaster = new DiscoveryBroadcasterService(
            udpPort: ProtocolConstants.DiscoveryUdpPort,
            tcpPort: 45455,
            serverName: "PROCTOR-MASTER");

        byte[] datagram = broadcaster.CreateAnnouncementPayload("192.168.10.1");
        bool parsed = DiscoveryListenerService.TryParseDiscoveryDatagram(
            datagram,
            new IPEndPoint(IPAddress.Parse("192.168.10.1"), ProtocolConstants.DiscoveryUdpPort),
            out DiscoveryPacket? packet);

        ServerTestAssert.True(parsed);
        ServerTestAssert.NotNull(packet);
        ServerTestAssert.Equal("PROCTOR-MASTER", packet!.ServerName);
        ServerTestAssert.Equal("192.168.10.1", packet.ServerIp);
        ServerTestAssert.Equal(45455, packet.TcpPort);
    }

    public void MainViewModel_UpdatesStudentGridAndAlerts_OnHandshakeAndHeartbeat()
    {
        var sessionManager = new SessionManagerService();
        var frameDispatcher = new FrameDispatcherService(uiDispatcher: null);
        var mainVm = new MainViewModel(
            discoveryBroadcaster: new DiscoveryBroadcasterService(),
            sessionManager: sessionManager,
            frameDispatcher: frameDispatcher,
            uiDispatcher: null);

        sessionManager.RegisterOrUpdateHandshake(new HandshakePacket
        {
            ClientId = "CLIENT-07",
            MachineName = "LAB-PC07",
            IpAddress = "192.168.1.107",
            StudentId = "STU-07",
            StudentName = "Siti Aminah",
            OsVersion = "Windows 11"
        });

        ServerTestAssert.Equal(1, mainVm.Students.Count);
        ServerTestAssert.Equal(1, mainVm.TotalConnectedCount);
        ServerTestAssert.Equal(0, mainVm.AlertCount);

        sessionManager.ProcessHeartbeat(new HeartbeatPacket
        {
            ClientId = "CLIENT-07",
            Status = ClientStatus.FocusLost,
            CurrentQuestionIndex = 5,
            ActiveWindowTitle = "Chrome"
        });

        StudentSessionViewModel studentVm = mainVm.Students[0];
        ServerTestAssert.Equal(ClientStatus.FocusLost, studentVm.Status);
        ServerTestAssert.Equal(5, studentVm.CurrentQuestionIndex);
        ServerTestAssert.Equal(1, studentVm.FocusLostCount);
        ServerTestAssert.Equal(1, mainVm.AlertCount);
    }

    public void FrameDispatcher_DecodesSyntheticJpegFrame_ToFrozenBitmapSource()
    {
        var bmp = ScreenCaptureService.CreateSyntheticFrame(360, 202);
        byte[] jpeg = ScreenCaptureService.EncodeToJpeg(bmp, 60);

        var decoded = FrameDispatcherService.TryDecodeJpeg(jpeg);
        ServerTestAssert.NotNull(decoded);
        ServerTestAssert.Equal(360, decoded!.PixelWidth);
        ServerTestAssert.Equal(202, decoded.PixelHeight);
        ServerTestAssert.True(decoded.IsFrozen);
    }

    public async Task EndToEnd_ServerAndClient_HandshakeAndCommandDelivery_OverStream()
    {
        await using var server = new SessionManagerService();
        using var outboundStream = new MemoryStream();

        server.RegisterOrUpdateHandshake(
            new HandshakePacket
            {
                ClientId = "STREAM-CLIENT-1",
                MachineName = "PC-STREAM",
                IpAddress = "192.168.1.50",
                StudentId = "STU-900",
                StudentName = "Rina Kusuma",
                OsVersion = "Windows 11"
            },
            transportStream: outboundStream);

        bool cmdSent = await server.SendCommandAsync("STREAM-CLIENT-1", ServerCommandType.LockScreen, "Please pause");
        ServerTestAssert.True(cmdSent);

        outboundStream.Position = 0;
        PacketEnvelope? envelope = await PacketSerializer.ReadPacketAsync(outboundStream);
        ServerTestAssert.NotNull(envelope);
        ServerTestAssert.Equal(PacketType.Command, envelope!.Type);

        CommandPacket receivedCmd = PacketSerializer.DeserializePayload<CommandPacket>(envelope.Type, envelope.Payload);
        ServerTestAssert.Equal(ServerCommandType.LockScreen, receivedCmd.CommandType);
        ServerTestAssert.Equal("Please pause", receivedCmd.Message);
    }

    public async Task StudentDetailViewModel_RequestsAndStopsHighResStream_AndExecutesLockUnlockCommands()
    {
        await using var server = new SessionManagerService();
        using var outboundStream = new MemoryStream();

        var handshake = new HandshakePacket
        {
            ClientId = "CLIENT-HD-01",
            MachineName = "PC-HD-01",
            IpAddress = "192.168.1.201",
            StudentId = "STU-201",
            StudentName = "Dimas Pratama",
            OsVersion = "Windows 11"
        };
        server.RegisterOrUpdateHandshake(handshake, transportStream: outboundStream);

        var studentVm = new StudentSessionViewModel();
        studentVm.ApplyHandshake(handshake);

        var detailVm = new StudentDetailViewModel(studentVm, server);
        await detailVm.StartHighResInspectionAsync();
        ServerTestAssert.True(detailVm.IsHighResStreaming);

        await detailVm.StopHighResInspectionAsync();
        ServerTestAssert.False(detailVm.IsHighResStreaming);

        outboundStream.Position = 0;
        PacketEnvelope? env1 = await PacketSerializer.ReadPacketAsync(outboundStream);
        PacketEnvelope? env2 = await PacketSerializer.ReadPacketAsync(outboundStream);

        CommandPacket cmd1 = PacketSerializer.DeserializePayload<CommandPacket>(env1!.Type, env1.Payload);
        CommandPacket cmd2 = PacketSerializer.DeserializePayload<CommandPacket>(env2!.Type, env2.Payload);

        ServerTestAssert.Equal(ServerCommandType.RequestHighResStream, cmd1.CommandType);
        ServerTestAssert.Equal(ServerCommandType.StopHighResStream, cmd2.CommandType);
    }

    public async Task BroadcastCommand_DeliversToAllConnectedSessions()
    {
        await using var server = new SessionManagerService();
        using var s1 = new MemoryStream();
        using var s2 = new MemoryStream();

        server.RegisterOrUpdateHandshake(new HandshakePacket { ClientId = "C1", StudentId = "S1" }, transportStream: s1);
        server.RegisterOrUpdateHandshake(new HandshakePacket { ClientId = "C2", StudentId = "S2" }, transportStream: s2);

        int sent = await server.BroadcastCommandAsync(ServerCommandType.SendBroadcastMessage, "10 minutes remaining");
        ServerTestAssert.Equal(2, sent);

        s1.Position = 0;
        s2.Position = 0;
        PacketEnvelope? env1 = await PacketSerializer.ReadPacketAsync(s1);
        PacketEnvelope? env2 = await PacketSerializer.ReadPacketAsync(s2);

        CommandPacket cmd1 = PacketSerializer.DeserializePayload<CommandPacket>(env1!.Type, env1.Payload);
        CommandPacket cmd2 = PacketSerializer.DeserializePayload<CommandPacket>(env2!.Type, env2.Payload);

        ServerTestAssert.Equal("10 minutes remaining", cmd1.Message);
        ServerTestAssert.Equal("10 minutes remaining", cmd2.Message);
    }

    public void ScreenCaptureService_CaptureFrame_ProducesValidJpegPacketForThumbnailAndHighRes()
    {
        var capture = new ScreenCaptureService();
        FramePacket thumb = capture.CaptureFrame("CLIENT-CAP", highResolution: false);

        ServerTestAssert.Equal("CLIENT-CAP", thumb.ClientId);
        ServerTestAssert.False(thumb.IsHighResolution);
        ServerTestAssert.Equal(ProtocolConstants.DefaultThumbnailWidth, thumb.Width);
        ServerTestAssert.Equal(ProtocolConstants.DefaultThumbnailHeight, thumb.Height);
        ServerTestAssert.True(thumb.JpegPayload.Length > 0);
    }

    public async Task StudentDetailViewModel_SendsRemoteMouseAndKeyboardPackets_WhenRemoteControlEnabled()
    {
        await using var server = new SessionManagerService();
        using var outboundStream = new MemoryStream();

        var handshake = new HandshakePacket { ClientId = "CLIENT-REMOTE-1", StudentId = "NIM-001" };
        server.RegisterOrUpdateHandshake(handshake, transportStream: outboundStream);

        var studentVm = new StudentSessionViewModel();
        studentVm.ApplyHandshake(handshake);

        var detailVm = new StudentDetailViewModel(studentVm, server)
        {
            IsRemoteControlEnabled = true
        };

        await detailVm.SendRemoteMouseAsync(0.5, 0.25, mouseButton: 1);
        await detailVm.SendRemoteKeyAsync(65, isKeyUp: false);

        outboundStream.Position = 0;
        PacketEnvelope? envMouse = await PacketSerializer.ReadPacketAsync(outboundStream);
        PacketEnvelope? envKey = await PacketSerializer.ReadPacketAsync(outboundStream);

        CommandPacket mouseCmd = PacketSerializer.DeserializePayload<CommandPacket>(envMouse!.Type, envMouse.Payload);
        CommandPacket keyCmd = PacketSerializer.DeserializePayload<CommandPacket>(envKey!.Type, envKey.Payload);

        ServerTestAssert.Equal(ServerCommandType.RemoteMouseInput, mouseCmd.CommandType);
        ServerTestAssert.Equal(0.5, mouseCmd.NormalizedX);
        ServerTestAssert.Equal(0.25, mouseCmd.NormalizedY);
        ServerTestAssert.Equal(1, mouseCmd.MouseButton);

        ServerTestAssert.Equal(ServerCommandType.RemoteKeyboardInput, keyCmd.CommandType);
        ServerTestAssert.Equal(65, keyCmd.VirtualKey);
        ServerTestAssert.False(keyCmd.IsKeyUp);
    }
}
