// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Client\Services\DiscoveryListenerService.cs
using System.Net;
using System.Net.Sockets;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;

namespace ExamMonitor.Client.Services;

public sealed class DiscoveryListenerService : IAsyncDisposable
{
    private readonly int _udpPort;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public DiscoveryListenerService(int udpPort = ProtocolConstants.DiscoveryUdpPort)
    {
        _udpPort = udpPort;
    }

    public event Action<DiscoveryPacket>? ServerDiscovered;

    public void Start()
    {
        if (_listenTask is { IsCompleted: false })
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _listenTask = null;
    }

    public static bool TryParseDiscoveryDatagram(ReadOnlySpan<byte> datagram, IPEndPoint? remoteEndPoint, out DiscoveryPacket? packet)
    {
        packet = null;
        try
        {
            DiscoveryPacket decoded = PacketSerializer.Deserialize<DiscoveryPacket>(datagram, out PacketType type);
            if (type != PacketType.Discovery ||
                !string.Equals(decoded.MagicHeader, ProtocolConstants.DiscoveryMagicHeader, StringComparison.Ordinal))
            {
                return false;
            }

            string resolvedIp = string.IsNullOrWhiteSpace(decoded.ServerIp) && remoteEndPoint is not null
                ? remoteEndPoint.Address.ToString()
                : decoded.ServerIp;

            packet = decoded with { ServerIp = resolvedIp };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (TryParseDiscoveryDatagram(result.Buffer, result.RemoteEndPoint, out DiscoveryPacket? packet) && packet is not null)
                {
                    ServerDiscovered?.Invoke(packet);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
