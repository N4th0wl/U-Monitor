// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Services\DiscoveryBroadcasterService.cs
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Common.Serialization;

namespace ExamMonitor.Server.Services;

public sealed class DiscoveryBroadcasterService : IAsyncDisposable
{
    private readonly int _udpPort;
    private readonly int _tcpPort;
    private readonly string _serverName;
    private readonly TimeSpan _broadcastInterval;
    private CancellationTokenSource? _cts;
    private Task? _broadcastLoopTask;

    public DiscoveryBroadcasterService(
        int udpPort = ProtocolConstants.DiscoveryUdpPort,
        int tcpPort = ProtocolConstants.DefaultServerTcpPort,
        string? serverName = null,
        TimeSpan? broadcastInterval = null)
    {
        _udpPort = udpPort;
        _tcpPort = tcpPort;
        _serverName = string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName;
        _broadcastInterval = broadcastInterval ?? TimeSpan.FromSeconds(2);
    }

    public bool IsRunning => _broadcastLoopTask is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _broadcastLoopTask = Task.Run(() => BroadcastLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_broadcastLoopTask is not null)
        {
            try
            {
                await _broadcastLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _broadcastLoopTask = null;
    }

    public byte[] CreateAnnouncementPayload(string? localIpOverride = null)
    {
        var packet = new DiscoveryPacket
        {
            MagicHeader = ProtocolConstants.DiscoveryMagicHeader,
            ServerName = _serverName,
            ServerIp = localIpOverride ?? ResolvePrimaryLanIp(),
            TcpPort = _tcpPort,
            Timestamp = DateTimeOffset.UtcNow
        };

        return PacketSerializer.Serialize(PacketType.Discovery, packet);
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _udpPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                byte[] payload = CreateAnnouncementPayload();
                await udpClient.SendAsync(payload, broadcastEndpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Ignore transient interface changes on LAN
            }

            await Task.Delay(_broadcastInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public static string ResolvePrimaryLanIp()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // Fallback to loopback if network interface query fails
        }

        return IPAddress.Loopback.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
