using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using OneTunnel.Core;

internal static class KillSwitchChecks
{
    public static async Task Run(bool smoke)
    {
        if (!Connection.IsAdministrator) throw new InvalidOperationException("Kill-switch tests require a Windows administrator token.");
        if (KillSwitch.InstalledFilterCount() != 0) throw new InvalidOperationException("Existing OneTunnel kill switch found. It was left untouched.");
        string output = Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_REPORT_DIR") ?? Path.Combine(Storage.Root, "kill-switch-report");
        Storage.PrivateDirectory(output);
        await SystemModeChecks.Snapshot(Path.Combine(output, "before.txt"));
        string physical = SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1"));
        var adapter = NetworkInterface.GetAllNetworkInterfaces().First(n => n.Name == physical);
        var ip = adapter.GetIPProperties();
        var source = ip.UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
        int interfaceIndex = ip.GetIPv4Properties()!.Index;
        var dns = ip.DnsAddresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
        var ipv6 = await Ipv6KillCanary.Find(adapter);
        using var fixture = await SocksSystemFixture.Start();
        var profiles = new List<Profile>();
        if (!smoke)
        {
            string file = Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_PROFILES")!;
            using var doc = JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file), false));
            var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("Profiles");
            profiles.AddRange(JsonSerializer.Deserialize<List<Profile>>(list)!.GroupBy(p => (p.Protocol, Transport: p.Protocol == Protocol.VLESS ? ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() ?? "tcp" : "native")).Select(g => g.First()));
        }
        if (!profiles.Any(p => p.Protocol == Protocol.SOCKS5)) profiles.Add(fixture.Profile);
        using var watchdog = Process.Start(Connection.Info(Environment.ProcessPath!, [typeof(KillSwitchChecks).Assembly.Location, "--system-watch", Environment.ProcessId.ToString()]));
        var results = new List<object>();
        int index = 0;
        foreach (var profile in profiles)
        {
            index++;
            var connection = new Connection(); Process? worker = null;
            var windows = new List<object>(); var ports = new List<int>();
            bool connected = false, oldConnectionBlocked = false, engineBlocked = false, crashBlocked = false, persistent = false, restored = false;
            string? failure = null; string udpAfterEngine = "Not run", udpAfterCrash = "Not run";
            string ipv6Baseline = "Unavailable", ipv6AfterEngine = "Not run", ipv6AfterCrash = "Not run";
            int ipv6BaselinePort = 0;
            int connectionAttempts = 0;
            var probeDiagnostics = new List<string>();
            try
            {
                using var oldHttp = PhysicalHttp(source, interfaceIndex);
                if (!await HttpWorks(oldHttp)) throw new InvalidOperationException("Physical HTTPS baseline unavailable.");
                if (ipv6 != null) { var baseline = await ipv6.Probe(); ipv6Baseline = baseline.Status; ipv6BaselinePort = baseline.Port; }
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    connectionAttempts++;
                    try { await connection.StartAsync(profile, true); connected = (await connection.ProbeAsync()).StartsWith("Системный HTTPS"); break; }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        probeDiagnostics.Add("IPv4: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetwork) + "; IPv6: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetworkV6));
                        if (ProxyCore.IsProxy(profile.Protocol) && connection.LocalPort > 0) probeDiagnostics.Add("Proxy DoH: " + await TunnelDns.CheckAsync(TunnelDns.Candidates[0], new Uri($"http://127.0.0.1:{connection.LocalPort}")));
                        if (attempt != 0) throw;
                        await connection.StopAsync(); await Task.Delay(1000);
                    }
                }
                if (!connection.TrafficLocked || KillSwitch.InstalledFilterCount() == 0) throw new Exception("WFP protection not installed.");
                oldConnectionBlocked = !await HttpWorks(oldHttp);
                var session = ReadSession();
                await BreakTransport(session);
                connection.Poll();
                long begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var tcp = await TcpDenied(source, interfaceIndex); ports.Add(tcp.Port);
                engineBlocked = tcp.Denied;
                udpAfterEngine = await DnsProbe(source, interfaceIndex, dns);
                var ipv6Probe = ipv6 != null ? await ipv6.Probe() : (Status: "Unavailable", Port: 0); ipv6AfterEngine = ipv6Probe.Status;
                windows.Add(new { Phase = "Engine stopped", BeginMilliseconds = begin, EndMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TcpSourcePort = tcp.Port, Ipv6TcpSourcePort = ipv6Probe.Port });
                if (!engineBlocked || !oldConnectionBlocked || udpAfterEngine.Contains("LEAK")) throw new Exception("Direct traffic remained usable while locked.");
                if (ipv6Baseline is "Reachable" or "ConnectionRefused" && ipv6AfterEngine != "AccessDenied") throw new Exception("Direct IPv6 did not receive a WFP denial.");
                await connection.StopAsync();
                if (!await HttpWorks(oldHttp)) throw new Exception("Network did not recover after disconnect.");

                string input = Path.Combine(Storage.Runtime, "kill-worker-profile.bin"), ready = Path.Combine(Storage.Runtime, "kill-worker-ready.json");
                File.Delete(ready);
                File.WriteAllBytes(input, Storage.Protect(JsonSerializer.SerializeToUtf8Bytes(profile), true));
                worker = Process.Start(Connection.Info(Environment.ProcessPath!, [typeof(KillSwitchChecks).Assembly.Location, "--kill-switch-worker", input, ready]))!;
                _ = worker.StandardOutput.ReadToEndAsync(); _ = worker.StandardError.ReadToEndAsync();
                for (int n = 0; n < 360 && !File.Exists(ready) && !worker.HasExited; n++) await Task.Delay(250);
                if (!File.Exists(ready) || File.ReadAllText(ready) != "ready") throw new Exception("Crash worker failed to become ready.");
                var crashSession = ReadSession();
                worker.Kill(); await worker.WaitForExitAsync();
                await BreakTransport(crashSession);
                persistent = KillSwitch.InstalledFilterCount() > 0;
                begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                tcp = await TcpDenied(source, interfaceIndex); ports.Add(tcp.Port);
                crashBlocked = tcp.Denied;
                udpAfterCrash = await DnsProbe(source, interfaceIndex, dns);
                ipv6Probe = ipv6 != null ? await ipv6.Probe() : (Status: "Unavailable", Port: 0); ipv6AfterCrash = ipv6Probe.Status;
                windows.Add(new { Phase = "Application crashed and transport stopped", BeginMilliseconds = begin, EndMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TcpSourcePort = tcp.Port, Ipv6TcpSourcePort = ipv6Probe.Port });
                if (!persistent || !crashBlocked || udpAfterCrash.Contains("LEAK")) throw new Exception("Protection did not survive app termination.");
                if (ipv6Baseline is "Reachable" or "ConnectionRefused" && ipv6AfterCrash != "AccessDenied") throw new Exception("Direct IPv6 protection did not survive app termination.");
                await new Connection().RecoverAsync();
                restored = KillSwitch.InstalledFilterCount() == 0 && !Connection.NeedsRecovery && await HttpWorks(oldHttp);
                if (!restored) throw new Exception("Recovery did not restore the physical network.");
            }
            catch (Exception ex) { failure = ex is System.ComponentModel.Win32Exception ? ex.Message : ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                if (worker is { HasExited: false }) { worker.Kill(); await worker.WaitForExitAsync(); }
                worker?.Dispose();
                if (connection.Active) await connection.StopAsync();
                if (Connection.NeedsRecovery) await new Connection().RecoverAsync();
                await SystemModeChecks.Snapshot(Path.Combine(output, $"restored-{index}.txt"));
                results.Add(new { Index = index, Protocol = profile.Protocol.ToString(), Transport = profile.Protocol == Protocol.VLESS ? ProxyCore.Outbound(profile.Source, profile.Protocol)["streamSettings"]?["network"]?.GetValue<string>() : "native", LocalFixture = profile.Id == fixture.Profile.Id, ConnectedHttps = connected, ConnectionAttempts = connectionAttempts, ProbeDiagnostics = probeDiagnostics, ExistingPhysicalConnectionBlocked = oldConnectionBlocked, EngineFailureBlockedTcp = engineBlocked, EngineFailureDns = udpAfterEngine, Ipv6Baseline = ipv6Baseline, Ipv6BaselineTcpPort = ipv6BaselinePort, EngineFailureIpv6 = ipv6AfterEngine, AppCrashBlockedTcp = crashBlocked, AppCrashDns = udpAfterCrash, AppCrashIpv6 = ipv6AfterCrash, PersistentFiltersAfterCrash = persistent, RecoveryRestoredNetwork = restored, BlockedWindows = windows, Failure = failure });
                File.WriteAllText(Path.Combine(output, "kill-switch-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"Kill switch {index}: {profile.Protocol}, passed={failure == null}; private report saved.");
            if (failure != null) throw new Exception("Kill-switch test failed; see private report.");
        }
        await SystemModeChecks.Snapshot(Path.Combine(output, "after.txt"));
        Console.WriteLine("Kill-switch failure, crash and recovery tests finished.");
    }
    public static async Task Worker(string input, string ready)
    {
        var profile = JsonSerializer.Deserialize<Profile>(Storage.Protect(File.ReadAllBytes(input), false))!;
        var connection = new Connection();
        try { await connection.StartAsync(profile, true); await connection.ProbeAsync(); File.WriteAllText(ready, "ready"); await Task.Delay(Timeout.Infinite); }
        finally { await connection.StopAsync(); }
    }
    private static Session ReadSession() => JsonSerializer.Deserialize<Session>(File.ReadAllText(Path.Combine(Storage.Root, "session.json")))!;
    internal static async Task BreakTransport(Session session)
    {
        if (session.Kind == "proxy")
        {
            try { using var process = Process.GetProcessById(session.Pid); if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } } catch (ArgumentException) { }
        }
        else await Connection.RunAsync(Path.Combine(Environment.SystemDirectory, "sc.exe"), ["stop", Connection.ServiceName(session)]);
        for (int i = 0; i < 100 && SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")).StartsWith("OneTunnel"); i++) await Task.Delay(100);
        if (SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")).StartsWith("OneTunnel")) throw new Exception("TUN route survived fault injection; test would not prove fallback protection.");
    }
    private static Socket PhysicalSocket(IPAddress source, int index, SocketType type, ProtocolType protocol)
    {
        var socket = new Socket(AddressFamily.InterNetwork, type, protocol);
        try { socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(index)); socket.Bind(new IPEndPoint(source, 0)); return socket; }
        catch { socket.Dispose(); throw; }
    }
    internal static HttpClient PhysicalHttp(IPAddress source, int index)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        handler.ConnectCallback = async (_, cancellation) =>
        {
            var socket = PhysicalSocket(source, index, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443), cancellation); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
    }
    internal static async Task<bool> HttpWorks(HttpClient http)
    {
        try { using var response = await http.GetAsync("https://one.one.one.one/cdn-cgi/trace"); await response.Content.ReadAsByteArrayAsync(); return response.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException) { return false; }
    }
    internal static async Task<(bool Denied, int Port)> TcpDenied(IPAddress source, int index)
    {
        using var socket = PhysicalSocket(source, index, SocketType.Stream, ProtocolType.Tcp);
        int port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        try { await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443)).WaitAsync(TimeSpan.FromSeconds(3)); return (false, port); }
        catch (SocketException ex) { return (ex.SocketErrorCode == SocketError.AccessDenied, port); }
        catch (TimeoutException) { return (false, port); }
    }
    internal static async Task<string> DnsProbe(IPAddress source, int index, IPAddress server)
    {
        using var socket = PhysicalSocket(source, index, SocketType.Dgram, ProtocolType.Udp);
        var query = new List<byte> { 0x51, 0x91, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (string label in ("onetunnel-killswitch-" + Guid.NewGuid().ToString("N") + ".example.com").Split('.')) { query.Add((byte)label.Length); query.AddRange(System.Text.Encoding.ASCII.GetBytes(label)); }
        query.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        try
        {
            await socket.SendToAsync(query.ToArray(), SocketFlags.None, new IPEndPoint(server, 53));
            var reply = new byte[512]; await socket.ReceiveAsync(reply).WaitAsync(TimeSpan.FromSeconds(2));
            return "Reply received: LEAK";
        }
        catch (SocketException ex) { return ex.SocketErrorCode == SocketError.AccessDenied ? "Denied by Windows" : ex.SocketErrorCode.ToString(); }
        catch (TimeoutException) { return "No reply; inspect capture"; }
    }
}
