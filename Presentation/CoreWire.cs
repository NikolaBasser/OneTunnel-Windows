using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneTunnel.Core;

public static class CoreWire
{
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 48 };
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellation)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 2 || length > CoreProtocol.MaxFrameBytes) throw new InvalidDataException("Invalid IPC frame.");
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data, cancellation);
        try { return JsonSerializer.Deserialize<T>(data, Options) ?? throw new InvalidDataException("Empty IPC frame."); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellation)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        try
        {
            if (data.Length > CoreProtocol.MaxFrameBytes) throw new InvalidDataException("IPC frame too large.");
            byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
            await stream.WriteAsync(header, cancellation); await stream.WriteAsync(data, cancellation); await stream.FlushAsync(cancellation);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
    }
}
