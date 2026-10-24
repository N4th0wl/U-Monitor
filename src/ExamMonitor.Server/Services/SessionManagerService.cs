// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Services\SessionManagerService.cs
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;

namespace ExamMonitor.Server.Services;

public sealed class ClientSessionState
{
    public required string ClientId { get; init; }
    public HandshakePacket? Handshake { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
    public ClientStatus Status { get; set; } = ClientStatus.Normal;
    public int CurrentQuestionIndex { get; set; }
    public string ActiveWindowTitle { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    internal TcpClient? TcpClient { get; set; }
    internal Stream? TransportStream { get; set; }
    internal SemaphoreSlim WriteLock { get; } = new(1, 1);
}

public sealed class SessionManagerService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ClientSessionState> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _heartbeatTimeout;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private Task? _monitorLoopTask;

    public SessionManagerService(TimeSpan? heartbeatTimeout = null)
    {
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(ProtocolConstants.HeartbeatTimeoutSeconds);
    }

    public int ListeningPort { get; private set; }

    public IReadOnlyDictionary<string, ClientSessionState> Sessions => _sessions;

    public event Action<HandshakePacket>? HandshakeReceived;
    public event Action<HeartbeatPacket>? HeartbeatReceived;
    public event Action<FramePacket>? FrameReceived;
    public event Action<string>? SessionMarkedInactive;
    public event Action<string>? ClientDisconnected;

    public void Start(int tcpPort = ProtocolConstants.DefaultServerTcpPort)
    {
        if (_listener is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, tcpPort);
        _listener.Start();
        ListeningPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _acceptLoopTask = Task.Run(() => AcceptConnectionsLoopAsync(_cts.Token));
        _monitorLoopTask = Task.Run(() => MonitorHeartbeatsLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        _listener = null;

        foreach (ClientSessionState session in _sessions.Values)
        {
            try
            {
                session.TcpClient?.Close();
            }
            catch
            {
            }
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (_monitorLoopTask is not null)
        {
            try
            {
                await _monitorLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _cts.Dispose();
        _cts = null;
    }

    public void RegisterOrUpdateHandshake(HandshakePacket handshake, TcpClient? client = null, DateTimeOffset? nowUtc = null, Stream? transportStream = null)
    {
        DateTimeOffset timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        ClientSessionState state = _sessions.AddOrUpdate(
            handshake.ClientId,
            id => new ClientSessionState
            {
                ClientId = id,
                Handshake = handshake,
                LastHeartbeatUtc = timestamp,
                Status = ClientStatus.Normal,
                IsActive = true,
                TcpClient = client,
                TransportStream = transportStream ?? (client is { Connected: true } ? client.GetStream() : null)
            },
            (_, existing) =>
            {
                existing.Handshake = handshake;
                existing.LastHeartbeatUtc = timestamp;
                existing.IsActive = true;
                if (existing.Status == ClientStatus.Disconnected)
                {
                    existing.Status = ClientStatus.Normal;
                }
                if (client is not null)
                {
                    existing.TcpClient = client;
                    if (client.Connected)
                    {
                        existing.TransportStream = client.GetStream();
                    }
                }
                if (transportStream is not null)
                {
                    existing.TransportStream = transportStream;
                }
                return existing;
            });

        HandshakeReceived?.Invoke(handshake);
    }

    public void ProcessHeartbeat(HeartbeatPacket heartbeat, DateTimeOffset? nowUtc = null)
    {
        DateTimeOffset timestamp = nowUtc ?? DateTimeOffset.UtcNow;
        if (_sessions.TryGetValue(heartbeat.ClientId, out ClientSessionState? session))
        {
            session.LastHeartbeatUtc = timestamp;
            session.Status = heartbeat.Status;
            session.CurrentQuestionIndex = heartbeat.CurrentQuestionIndex;
            session.ActiveWindowTitle = heartbeat.ActiveWindowTitle;
            session.IsActive = heartbeat.Status != ClientStatus.AppTerminated && heartbeat.Status != ClientStatus.Disconnected;
        }
        else
        {
            _sessions[heartbeat.ClientId] = new ClientSessionState
            {
                ClientId = heartbeat.ClientId,
                LastHeartbeatUtc = timestamp,
                Status = heartbeat.Status,
                CurrentQuestionIndex = heartbeat.CurrentQuestionIndex,
                ActiveWindowTitle = heartbeat.ActiveWindowTitle,
                IsActive = heartbeat.Status != ClientStatus.AppTerminated && heartbeat.Status != ClientStatus.Disconnected
            };
        }

        HeartbeatReceived?.Invoke(heartbeat);
    }

    public int EvaluateHeartbeats(DateTimeOffset nowUtc)
    {
        int newlyInactiveCount = 0;
        foreach (ClientSessionState session in _sessions.Values)
        {
            if (session.IsActive && (nowUtc - session.LastHeartbeatUtc) > _heartbeatTimeout)
            {
                session.IsActive = false;
                session.Status = ClientStatus.Disconnected;
                newlyInactiveCount++;
                SessionMarkedInactive?.Invoke(session.ClientId);
            }
        }
        return newlyInactiveCount;
    }

    public async Task<bool> SendCommandAsync(string clientId, ServerCommandType commandType, string message = "", CancellationToken cancellationToken = default)
    {
        var packet = new CommandPacket
        {
            TargetClientId = clientId,
            CommandType = commandType,
            Message = message,
            IssuedAtUtc = DateTimeOffset.UtcNow
        };
        return await SendCommandPacketAsync(clientId, packet, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SendCommandPacketAsync(string clientId, CommandPacket packet, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(clientId, out ClientSessionState? session))
        {
            return false;
        }

        Stream? stream = session.TransportStream ?? (session.TcpClient is { Connected: true } tcpClient ? tcpClient.GetStream() : null);
        if (stream is null || !stream.CanWrite)
        {
            return false;
        }

        await session.WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PacketSerializer.WritePacketAsync(stream, PacketType.Command, packet, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            session.WriteLock.Release();
        }
    }

    public async Task<int> BroadcastCommandAsync(ServerCommandType commandType, string message = "", CancellationToken cancellationToken = default)
    {
        int sentCount = 0;
        foreach (string clientId in _sessions.Keys)
        {
            if (await SendCommandAsync(clientId, commandType, message, cancellationToken).ConfigureAwait(false))
            {
                sentCount++;
            }
        }
        return sentCount;
    }

    private async Task AcceptConnectionsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                _ = Task.Run(() => HandleClientConnectionAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleClientConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        string? connectedClientId = null;
        try
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    PacketEnvelope? envelope = await PacketSerializer.ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (envelope is null)
                    {
                        break;
                    }

                    switch (envelope.Type)
                    {
                        case PacketType.Handshake:
                            HandshakePacket handshake = PacketSerializer.DeserializePayload<HandshakePacket>(envelope.Type, envelope.Payload);
                            connectedClientId = handshake.ClientId;
                            RegisterOrUpdateHandshake(handshake, client);
                            break;

                        case PacketType.Heartbeat:
                            HeartbeatPacket heartbeat = PacketSerializer.DeserializePayload<HeartbeatPacket>(envelope.Type, envelope.Payload);
                            connectedClientId ??= heartbeat.ClientId;
                            ProcessHeartbeat(heartbeat);
                            break;

                        case PacketType.Frame:
                            FramePacket frame = PacketSerializer.DeserializePayload<FramePacket>(envelope.Type, envelope.Payload);
                            connectedClientId ??= frame.ClientId;
                            if (_sessions.TryGetValue(frame.ClientId, out ClientSessionState? state))
                            {
                                state.LastHeartbeatUtc = DateTimeOffset.UtcNow;
                                state.IsActive = true;
                            }
                            FrameReceived?.Invoke(frame);
                            break;
                    }
                }
            }
        }
        catch
        {
            // Client socket closed or reset
        }
        finally
        {
            if (!string.IsNullOrEmpty(connectedClientId) && _sessions.TryGetValue(connectedClientId, out ClientSessionState? session))
            {
                session.IsActive = false;
                session.Status = ClientStatus.Disconnected;
                ClientDisconnected?.Invoke(connectedClientId);
            }
        }
    }

    private async Task MonitorHeartbeatsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                EvaluateHeartbeats(DateTimeOffset.UtcNow);
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
