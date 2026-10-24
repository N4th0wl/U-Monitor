// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Services\TelemetryService.cs
using System.Net;
using System.Net.Sockets;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;

namespace ExamMonitor.Client.Services;

public sealed class TelemetryService : IAsyncDisposable
{
    private string _clientId;
    private string _studentId;
    private string _studentName;
    private readonly ScreenCaptureService _screenCapture;
    private readonly HookMonitorService _hookMonitor;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private TcpClient? _tcpClient;
    private CancellationTokenSource? _cts;
    private Task? _heartbeatLoopTask;
    private Task? _frameLoopTask;
    private Task? _receiveLoopTask;

    private volatile bool _isHighResRequested;
    private volatile ClientStatus _overrideStatus = ClientStatus.Normal;
    private volatile int _currentQuestionIndex = 1;

    public TelemetryService(
        string clientId,
        string studentId,
        string studentName,
        ScreenCaptureService? screenCapture = null,
        HookMonitorService? hookMonitor = null)
    {
        _clientId = clientId;
        _studentId = studentId;
        _studentName = studentName;
        _screenCapture = screenCapture ?? new ScreenCaptureService();
        _hookMonitor = hookMonitor ?? new HookMonitorService();
    }

    public bool IsConnected => _tcpClient is { Connected: true };

    public int CurrentQuestionIndex
    {
        get => _currentQuestionIndex;
        set => _currentQuestionIndex = Math.Max(1, value);
    }

    public ClientStatus OverrideStatus
    {
        get => _overrideStatus;
        set => _overrideStatus = value;
    }

    public event Action<CommandPacket>? CommandReceived;
    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ActiveWindowChanged;

    public void UpdateIdentity(string studentId, string studentName)
    {
        _studentId = string.IsNullOrWhiteSpace(studentId) ? "NIM-UNKNOWN" : studentId.Trim();
        _studentName = string.IsNullOrWhiteSpace(studentName) ? Environment.UserName : studentName.Trim();
        _clientId = $"{Environment.MachineName}-{_studentId}";
    }

    public async Task<bool> ConnectAsync(string serverIp, int tcpPort, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(serverIp, tcpPort, cancellationToken).ConfigureAwait(false);
            _tcpClient = client;
            _cts = new CancellationTokenSource();

            string localIp = (client.Client.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
            var handshake = new HandshakePacket
            {
                ClientId = _clientId,
                MachineName = Environment.MachineName,
                IpAddress = localIp,
                StudentId = _studentId,
                StudentName = _studentName,
                OsVersion = Environment.OSVersion.VersionString,
                ConnectedAtUtc = DateTimeOffset.UtcNow
            };

            await SendPacketAsync(PacketType.Handshake, handshake, _cts.Token).ConfigureAwait(false);
            ConnectionStateChanged?.Invoke(true);

            _heartbeatLoopTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token), _cts.Token);
            _frameLoopTask = Task.Run(() => FrameStreamingLoopAsync(_cts.Token), _cts.Token);
            _receiveLoopTask = Task.Run(() => ReceiveCommandsLoopAsync(_cts.Token), _cts.Token);
            return true;
        }
        catch
        {
            client.Dispose();
            ConnectionStateChanged?.Invoke(false);
            return false;
        }
    }

    public async Task SendTerminatingHeartbeatAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        var packet = new HeartbeatPacket
        {
            ClientId = _clientId,
            Timestamp = DateTimeOffset.UtcNow,
            Status = ClientStatus.AppTerminated,
            CurrentQuestionIndex = _currentQuestionIndex,
            ActiveWindowTitle = "Agent Closed"
        };

        try
        {
            await SendPacketAsync(PacketType.Heartbeat, packet, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task DisconnectAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch
        {
        }
        _tcpClient = null;

        if (_cts is not null)
        {
            _cts.Dispose();
            _cts = null;
        }

        ConnectionStateChanged?.Invoke(false);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                (bool isCompliant, string activeTitle) = _hookMonitor.InspectForegroundWindow();
                ActiveWindowChanged?.Invoke(activeTitle);

                ClientStatus effectiveStatus = _overrideStatus != ClientStatus.Normal
                    ? _overrideStatus
                    : (isCompliant ? ClientStatus.Normal : ClientStatus.FocusLost);

                var heartbeat = new HeartbeatPacket
                {
                    ClientId = _clientId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Status = effectiveStatus,
                    CurrentQuestionIndex = _currentQuestionIndex,
                    ActiveWindowTitle = activeTitle
                };

                await SendPacketAsync(PacketType.Heartbeat, heartbeat, cancellationToken).ConfigureAwait(false);
                await Task.Delay(ProtocolConstants.HeartbeatIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                ConnectionStateChanged?.Invoke(false);
                break;
            }
        }
    }

    private async Task FrameStreamingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                bool highRes = _isHighResRequested;
                FramePacket frame = _screenCapture.CaptureFrame(_clientId, highRes);
                await SendPacketAsync(PacketType.Frame, frame, cancellationToken).ConfigureAwait(false);

                // ~12 FPS in HD Remote/Inspect mode, ~3 FPS in classroom thumbnail grid mode
                int delayMs = highRes ? 80 : 330;
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private async Task ReceiveCommandsLoopAsync(CancellationToken cancellationToken)
    {
        if (_tcpClient is null)
        {
            return;
        }

        try
        {
            NetworkStream stream = _tcpClient.GetStream();
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                PacketEnvelope? envelope = await PacketSerializer.ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                if (envelope.Type == PacketType.Command)
                {
                    CommandPacket command = PacketSerializer.DeserializePayload<CommandPacket>(envelope.Type, envelope.Payload);
                    HandleInternalCommandState(command);
                    CommandReceived?.Invoke(command);
                }
            }
        }
        catch
        {
        }
        finally
        {
            ConnectionStateChanged?.Invoke(false);
        }
    }

    private void HandleInternalCommandState(CommandPacket command)
    {
        switch (command.CommandType)
        {
            case ServerCommandType.RequestHighResStream:
                _isHighResRequested = true;
                break;
            case ServerCommandType.StopHighResStream:
                _isHighResRequested = false;
                break;
            case ServerCommandType.LockScreen:
                _overrideStatus = ClientStatus.Locked;
                _hookMonitor.SuppressSystemSwitchKeys = true;
                break;
            case ServerCommandType.UnlockScreen:
                _overrideStatus = ClientStatus.Normal;
                _hookMonitor.SuppressSystemSwitchKeys = false;
                break;
            case ServerCommandType.TerminateApp:
                _overrideStatus = ClientStatus.AppTerminated;
                break;
            case ServerCommandType.RemoteMouseInput:
                HookMonitorService.ExecuteRemoteMouse(command);
                break;
            case ServerCommandType.RemoteKeyboardInput:
                HookMonitorService.ExecuteRemoteKey(command);
                break;
        }
    }

    private async Task SendPacketAsync<T>(PacketType type, T packet, CancellationToken cancellationToken)
    {
        if (_tcpClient is not { Connected: true } client)
        {
            throw new InvalidOperationException("TCP client is not connected.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NetworkStream stream = client.GetStream();
            await PacketSerializer.WritePacketAsync(stream, type, packet, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
