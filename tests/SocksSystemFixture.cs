using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using OneTunnel.Core;

internal sealed class SocksSystemFixture : IDisposable
{
    private readonly ProcessJob job = new();
    private Process? process;
    public Profile Profile { get; private set; } = null!;
    public static async Task<SocksSystemFixture> Start(string? enginePath = null)
    {
        var fixture = new SocksSystemFixture();
        try
        {
            string adapter = SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1"));
            if (adapter is "No route" or "Unknown interface" || adapter.StartsWith("OneTunnel")) throw new InvalidOperationException("No physical baseline route for SOCKS fixture.");
            int port;
            using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; }
            var config = new JsonObject
            {
                ["log"] = new JsonObject { ["level"] = "error" },
                ["inbounds"] = new JsonArray(new JsonObject { ["type"] = "socks", ["listen"] = "127.0.0.1", ["listen_port"] = port }),
                ["outbounds"] = new JsonArray(new JsonObject { ["type"] = "direct", ["tag"] = "fixture-direct", ["bind_interface"] = adapter, ["domain_resolver"] = "fixture-dns" }),
                ["dns"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["type"] = "https", ["tag"] = "fixture-dns", ["server"] = "1.1.1.1", ["path"] = "/dns-query", ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = "cloudflare-dns.com" }, ["detour"] = "fixture-direct" }) },
                ["route"] = new JsonObject { ["final"] = "fixture-direct" }
            };
            string file = Path.Combine(Storage.Runtime, "socks-system-fixture.json"); File.WriteAllText(file, config.ToJsonString());
            enginePath ??= Connection.EnginePath(Protocol.SOCKS5);
            if (await Connection.RunAsync(enginePath, ["check", "-c", file]) != 0) throw new InvalidOperationException("SOCKS fixture config rejected.");
            fixture.process = Process.Start(Connection.Info(enginePath, ["run", "-c", file]))!;
            fixture.job.Add(fixture.process);
            _ = fixture.process.StandardOutput.ReadToEndAsync(); _ = fixture.process.StandardError.ReadToEndAsync();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (fixture.process.HasExited) throw new InvalidOperationException("SOCKS fixture stopped.");
                try { using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, port); fixture.Profile = ProfileParser.Parse("Local system test fixture", $"socks5://127.0.0.1:{port}"); return fixture; }
                catch (SocketException) { await Task.Delay(100); }
            }
            throw new TimeoutException("SOCKS fixture did not open its port.");
        }
        catch { fixture.Dispose(); throw; }
    }
    public void Dispose() { job.Dispose(); process?.Dispose(); }
}
