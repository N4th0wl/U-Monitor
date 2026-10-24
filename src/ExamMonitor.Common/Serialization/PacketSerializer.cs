// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Serialization\PacketSerializer.cs
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Common.Serialization;

public static class PacketSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] Serialize<T>(PacketType type, T packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        byte[] bodyBytes;
        if (type == PacketType.Frame && packet is FramePacket framePacket)
        {
            bodyBytes = EncodeBinaryFrame(framePacket);
        }
        else
        {
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonOptions);
        }

        if (bodyBytes.Length > ProtocolConstants.MaxPacketSizeBytes)
        {
            throw new InvalidOperationException($"Packet payload size {bodyBytes.Length} exceeds maximum allowed {ProtocolConstants.MaxPacketSizeBytes} bytes.");
        }

        // Wire frame format: [1 byte PacketType][4 bytes Little-Endian Length][N bytes Payload]
        byte[] buffer = new byte[5 + bodyBytes.Length];
        buffer[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), bodyBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, buffer, 5, bodyBytes.Length);
        return buffer;
    }

    public static PacketEnvelope Unwrap(ReadOnlySpan<byte> wireBytes)
    {
        if (wireBytes.Length < 5)
        {
            throw new InvalidDataException("Wire packet is shorter than the 5-byte header.");
        }

        var type = (PacketType)wireBytes[0];
        int length = BinaryPrimitives.ReadInt32LittleEndian(wireBytes.Slice(1, 4));
        if (length < 0 || length > ProtocolConstants.MaxPacketSizeBytes || wireBytes.Length < 5 + length)
        {
            throw new InvalidDataException($"Invalid payload length {length} in packet header.");
        }

        byte[] payload = wireBytes.Slice(5, length).ToArray();
        return new PacketEnvelope(type, payload);
    }

    public static T DeserializePayload<T>(PacketType type, ReadOnlySpan<byte> payload)
    {
        if (type == PacketType.Frame && typeof(T) == typeof(FramePacket))
        {
            FramePacket frame = DecodeBinaryFrame(payload);
            return (T)(object)frame;
        }

        T? result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        return result ?? throw new InvalidDataException($"Failed to deserialize payload as {typeof(T).Name}.");
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> wireBytes, out PacketType type)
    {
        PacketEnvelope envelope = Unwrap(wireBytes);
        type = envelope.Type;
        return DeserializePayload<T>(envelope.Type, envelope.Payload);
    }

    public static async ValueTask WritePacketAsync<T>(Stream stream, PacketType type, T packet, CancellationToken cancellationToken = default)
    {
        byte[] wireBytes = Serialize(type, packet);
        await stream.WriteAsync(wireBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<PacketEnvelope?> ReadPacketAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[5];
        int headerRead = await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
        {
            return null; // Clean EOF
        }

        if (headerRead < 5)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading packet header.");
        }

        var type = (PacketType)header[0];
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1, 4));
        if (length < 0 || length > ProtocolConstants.MaxPacketSizeBytes)
        {
            throw new InvalidDataException($"Packet length {length} is out of valid bounds.");
        }

        byte[] payload = new byte[length];
        if (length > 0)
        {
            int payloadRead = await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            if (payloadRead < length)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading packet payload.");
            }
        }

        return new PacketEnvelope(type, payload);
    }

    private static async ValueTask<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }

    private static byte[] EncodeBinaryFrame(FramePacket packet)
    {
        byte[] clientIdBytes = Encoding.UTF8.GetBytes(packet.ClientId ?? string.Empty);
        byte[] jpegBytes = packet.JpegPayload ?? Array.Empty<byte>();

        // Layout:
        // [4B clientIdLen][clientId UTF8][8B timestamp UnixTimeMilliseconds][4B width][4B height][1B isHighRes][4B jpegLen][jpegBytes]
        int totalLen = 4 + clientIdBytes.Length + 8 + 4 + 4 + 1 + 4 + jpegBytes.Length;
        byte[] buffer = new byte[totalLen];
        Span<byte> span = buffer.AsSpan();

        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), clientIdBytes.Length);
        offset += 4;

        clientIdBytes.CopyTo(span.Slice(offset, clientIdBytes.Length));
        offset += clientIdBytes.Length;

        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), packet.Timestamp.ToUnixTimeMilliseconds());
        offset += 8;

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), packet.Width);
        offset += 4;

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), packet.Height);
        offset += 4;

        span[offset] = packet.IsHighResolution ? (byte)1 : (byte)0;
        offset += 1;

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), jpegBytes.Length);
        offset += 4;

        jpegBytes.CopyTo(span.Slice(offset, jpegBytes.Length));
        return buffer;
    }

    private static FramePacket DecodeBinaryFrame(ReadOnlySpan<byte> span)
    {
        int offset = 0;
        int clientIdLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        string clientId = Encoding.UTF8.GetString(span.Slice(offset, clientIdLen));
        offset += clientIdLen;

        long unixMs = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        offset += 8;

        int width = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        int height = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        bool isHighRes = span[offset] != 0;
        offset += 1;

        int jpegLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        offset += 4;

        byte[] jpegPayload = span.Slice(offset, jpegLen).ToArray();

        return new FramePacket
        {
            ClientId = clientId,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs),
            Width = width,
            Height = height,
            IsHighResolution = isHighRes,
            JpegPayload = jpegPayload
        };
    }
}
