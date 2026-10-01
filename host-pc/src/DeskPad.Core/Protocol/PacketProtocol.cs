using System.Buffers.Binary;

namespace DeskPad.Core.Protocol;

public enum PacketType : byte
{
    Handshake = 0x01,
    ConfigAck = 0x02,
    VideoFrame = 0x10,
    Ping = 0x20,
    Pong = 0x21
}

[Flags]
public enum FrameFlags : byte
{
    None = 0,
    Keyframe = 1 << 0,
    ConfigData = 1 << 1,
}

public class StreamPacket
{
    public static readonly byte[] Magic = [0x54, 0x41, 0x42, 0x4C]; // "TABL"
    public const int HeaderSize = 12;

    public PacketType Type { get; set; }
    public FrameFlags Flags { get; set; }
    public byte[] Payload { get; set; } = [];

    public static byte[] Serialize(PacketType type, FrameFlags flags, byte[] payload)
    {
        byte[] buffer = new byte[HeaderSize + payload.Length];
        
        // Magic
        Magic.CopyTo(buffer, 0);
        // Type
        buffer[4] = (byte)type;
        // Flags
        buffer[5] = (byte)flags;
        // Reserved
        buffer[6] = 0;
        buffer[7] = 0;
        // Payload Length (Big-endian)
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8, 4), payload.Length);
        // Payload
        if (payload.Length > 0)
        {
            payload.CopyTo(buffer, HeaderSize);
        }

        return buffer;
    }

    public static async Task<StreamPacket?> ReadFromStreamAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] headerBuffer = new byte[HeaderSize];
        int read = 0;
        while (read < HeaderSize)
        {
            int r = await stream.ReadAsync(headerBuffer.AsMemory(read, HeaderSize - read), ct);
            if (r == 0) return null; // Connection closed
            read += r;
        }

        // Validate Magic
        if (headerBuffer[0] != Magic[0] || headerBuffer[1] != Magic[1] ||
            headerBuffer[2] != Magic[2] || headerBuffer[3] != Magic[3])
        {
            throw new InvalidDataException("Invalid packet magic header");
        }

        var type = (PacketType)headerBuffer[4];
        var flags = (FrameFlags)headerBuffer[5];
        int payloadLen = BinaryPrimitives.ReadInt32BigEndian(headerBuffer.AsSpan(8, 4));

        if (payloadLen < 0 || payloadLen > 50 * 1024 * 1024) // 50MB safety cap
        {
            throw new InvalidDataException($"Payload size out of bounds: {payloadLen}");
        }

        byte[] payload = new byte[payloadLen];
        int payloadRead = 0;
        while (payloadRead < payloadLen)
        {
            int r = await stream.ReadAsync(payload.AsMemory(payloadRead, payloadLen - payloadRead), ct);
            if (r == 0) return null;
            payloadRead += r;
        }

        return new StreamPacket
        {
            Type = type,
            Flags = flags,
            Payload = payload
        };
    }
}
