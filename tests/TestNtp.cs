using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

internal sealed class TestNtp : IAsyncDisposable
{
    private readonly UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource cancel = new();
    private readonly Task loop;
    public int Port => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    public int Requests { get; private set; }
    public TestNtp(int offsetSeconds)
    {
        loop = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                UdpReceiveResult packet; try { packet = await socket.ReceiveAsync(cancel.Token); } catch (OperationCanceledException) { break; }
                if (packet.Buffer.Length < 48) continue;
                var response = new byte[48]; response[0] = 0x24; response[1] = 1; response[2] = 6; response[3] = 236;
                "LOCL"u8.CopyTo(response.AsSpan(12));
                var now = DateTimeOffset.UtcNow.AddSeconds(offsetSeconds);
                ulong seconds = (ulong)(now.ToUnixTimeSeconds() + 2208988800L);
                ulong fraction = (ulong)(now.Ticks % TimeSpan.TicksPerSecond) * 0x100000000UL / TimeSpan.TicksPerSecond;
                ulong timestamp = (seconds << 32) | fraction;
                BinaryPrimitives.WriteUInt64BigEndian(response.AsSpan(16, 8), timestamp);
                packet.Buffer.AsSpan(40, 8).CopyTo(response.AsSpan(24, 8));
                BinaryPrimitives.WriteUInt64BigEndian(response.AsSpan(32, 8), timestamp);
                BinaryPrimitives.WriteUInt64BigEndian(response.AsSpan(40, 8), timestamp);
                await socket.SendAsync(response, packet.RemoteEndPoint, cancel.Token); Requests++;
            }
        });
    }
    public async ValueTask DisposeAsync() { cancel.Cancel(); await loop; socket.Dispose(); cancel.Dispose(); }
}
