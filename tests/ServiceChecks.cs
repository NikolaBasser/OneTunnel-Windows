using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using OneTunnel.Client;
using OneTunnel.Core;

internal static class ServiceChecks
{
    internal static async Task Diagnose()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneTunnel", "profiles.bin");
        using var doc = JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file), false));
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("Profiles");
        var profile = JsonSerializer.Deserialize<List<Profile>>(list)!.First(p => p.Protocol == Protocol.VLESS && Transport(p) == "tcp");
        var client = new ServiceConnection(); await client.RefreshAsync();
        if (!client.Available || client.Active || client.NeedsRecovery) throw new Exception("Idle service required.");
        try
        {
            await client.StartAsync(profile, false);
            try { Console.WriteLine("Service HTTPS: " + await client.ProbeAsync()); } catch (CoreCommandException ex) { Console.WriteLine("Service HTTPS: " + ex.Code); }
            Console.WriteLine("User IPv4: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetwork));
            Console.WriteLine("User IPv6: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetworkV6));
            Console.WriteLine("Proxy DoH: " + await TunnelDns.CheckAsync(TunnelDns.Candidates[0], new Uri($"http://127.0.0.1:{client.LocalPort}")));
            try { Console.WriteLine("Windows DNS count: " + (await Dns.GetHostAddressesAsync("example.com").WaitAsync(TimeSpan.FromSeconds(5))).Length); } catch { Console.WriteLine("Windows DNS failed"); }
            using var handler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{client.LocalPort}") };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            try { Console.WriteLine("Proxy HTTPS: " + (await http.GetAsync("https://example.com")).IsSuccessStatusCode); } catch { Console.WriteLine("Proxy HTTPS failed"); }
        }
        finally { await client.StopAsync(); }
    }
    internal static async Task UserCheck()
    {
        await ServicePeer.StartAsync();
        var client = new ServiceConnection();
        try { await client.CallAsync("hello"); } catch (Exception ex) { Console.WriteLine(ex.ToString()); throw; }
        if (!client.Available) throw new Exception("Service unavailable.");
        Console.WriteLine("Authenticated service IPC works; elevated=" + Connection.IsAdministrator);
        Console.WriteLine($"State: active={client.Active}, recovery={client.NeedsRecovery}, protection={client.TrafficLocked}; {client.Status}");
        async Task<CoreResponse> Raw(CoreRequest request)
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", CoreProtocol.PipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(timeout.Token); ServicePeer.Verify(pipe);
            await CoreWire.WriteAsync(pipe, request, timeout.Token); return await CoreWire.ReadAsync<CoreResponse>(pipe, timeout.Token);
        }
        if ((await Raw(new CoreRequest(99, "status"))).Error != "incompatible_version") throw new Exception("Version negotiation failed.");
        if ((await Raw(new CoreRequest(1, "execute"))).Error != "unknown_command") throw new Exception("Unknown command accepted.");
        if ((await Raw(new CoreRequest(1, "status", FullDevice: true))).Error != "invalid_request") throw new Exception("Invalid command arguments accepted.");
        Console.WriteLine("PASS IPC: incompatible versions, unknown commands and unexpected arguments rejected");
        if (!client.Active && !client.NeedsRecovery)
        {
            foreach (var kind in Enum.GetValues<Protocol>())
            {
                var invalid = new Profile(Guid.NewGuid(), "synthetic invalid", kind, "invalid configuration");
                var rejected = await Raw(new CoreRequest(1, "start", invalid, true));
                if (rejected.Error != "invalid_configuration" || rejected.State.Active || rejected.State.NeedsRecovery || rejected.State.TrafficLocked)
                    throw new Exception("Invalid protocol input changed idle state.");
            }
            await client.RefreshAsync();
            if (!client.Available || client.Active || client.NeedsRecovery) throw new Exception("Compiler rejection damaged service state.");
            Console.WriteLine("PASS IPC: Go rejects invalid profiles for all six protocols before launch and preserves idle state");
        }
    }
    internal static async Task Run()
    {
        if (!Connection.IsAdministrator) throw new Exception("Fault injection requires UAC.");
        var client = new ServiceConnection(); await client.CallAsync("hello");
        if (client.Active || client.NeedsRecovery) throw new Exception("Existing service session was left untouched.");
        string output = Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_REPORT_DIR")!;
        Storage.PrivateDirectory(output);
        ServiceSafetyWatch.DirectoryPath = output;
        string serviceRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OneTunnel", "Service", RuntimeIdentity.OwnerSid);
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OneTunnel", "Core", "engines", "sing-box.exe");
        await SystemModeChecks.Snapshot(Path.Combine(output, "before.txt"));
        var nic = NetworkInterface.GetAllNetworkInterfaces().First(n => n.Name == SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")));
        var properties = nic.GetIPProperties();
        var source = properties.UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Address;
        int index = properties.GetIPv4Properties()!.Index;
        var dns = properties.DnsAddresses.First(a => a.AddressFamily == AddressFamily.InterNetwork);
        var ipv6 = await Ipv6KillCanary.Find(nic);
        using var fixture = await SocksSystemFixture.Start(installed);
        using var protocolFixtures = new ProxySystemFixtures(fixture.Profile);
        using var doc = JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_PROFILES")!), false));
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("Profiles");
        bool hasAdapter = File.Exists(Path.Combine(Path.GetDirectoryName(installed)!, "..", "OneTunnel.Adapter.exe"));
        bool nativeGo = File.Exists(Path.Combine(Path.GetDirectoryName(installed)!, "..", "go-runtime.json"));
        bool goCore = hasAdapter || nativeGo;
        var profiles = new List<Profile>();
        var preflight = new Dictionary<Guid, int>();
        var unavailable = new HashSet<Guid>();
        foreach (var group in JsonSerializer.Deserialize<List<Profile>>(list)!.GroupBy(p => (p.Protocol, Transport(p))))
        {
            if (Environment.GetEnvironmentVariable("ONETUNNEL_TEST_FIXTURE_ONLY") == "1") continue;
            if (!ProxyCore.IsProxy(group.Key.Protocol)) { profiles.Add(group.First()); continue; }
            // A remote endpoint can go offline independently of a core migration.
            // Pick a working representative via the new service, never silently
            // omit a protocol/transport whose entire group fails.
            Profile? ready = null; int tried = 0;
            foreach (var candidate in group.Take(3))
            {
                tried++;
                try { await client.StartAsync(candidate, false); await client.ProbeAsync(); ready = candidate; }
                catch (CoreCommandException) { }
                finally { await client.StopAsync(); }
                if (ready != null) break;
            }
            Console.WriteLine($"Preflight: {group.Key.Protocol}/{group.Key.Item2}, tried={tried}, ready={ready != null}");
            if (ready == null) { ready = await protocolFixtures.Create(group.Key.Protocol, group.Key.Item2); unavailable.Add(ready.Id); }
            profiles.Add(ready); preflight[ready.Id] = tried;
        }
        if (!profiles.Any(p => p.Protocol == Protocol.SOCKS5)) profiles.Add(fixture.Profile);
        var results = new List<object>(); int ordinal = 0;
        foreach (var profile in profiles)
        {
            ordinal++; var windows = new List<object>(); string? failure = null; string stage = "Baseline";
            void Stage(string value) { stage = value; File.AppendAllText(Path.Combine(output, "stages.txt"), DateTime.UtcNow.ToString("O") + " " + ordinal + " " + value + Environment.NewLine); }
            bool connected = false, survivedClient = false, oldBlocked = false, engineBlocked = false, crashBlocked = false, persistent = false, restored = false;
            bool adapterBlocked = false, adapterRecovered = false, goServiceSurvived = false;
            string ipv6Baseline = "Unavailable", engineIpv6 = "Not run", crashIpv6 = "Not run";
            string adapterIpv6 = "Not run";
            int probeAttempts = 0;
            int connectionAttempts = 0;
            var diagnostics = new List<string>();
            async Task<string> Probe()
            {
                for (int attempt = 0; ; attempt++)
                {
                    probeAttempts++;
                    try { return await client.ProbeAsync(); }
                    catch (CoreCommandException ex) when (attempt == 0 && ex.Code is "network_timeout" or "network_error") { await Task.Delay(500); }
                }
            }
            try
            {
                ServiceSafetyWatch.Arm(); Stage("Physical baseline");
                using var old = KillSwitchChecks.PhysicalHttp(source, index);
                if (!await KillSwitchChecks.HttpWorks(old)) throw new Exception("No physical HTTPS baseline.");
                if (ipv6 != null) ipv6Baseline = (await ipv6.Probe()).Status;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    Stage("Start"); connectionAttempts++; await client.StartAsync(profile, true);
                    Stage("Initial HTTPS");
                    try { connected = (await Probe()).StartsWith("Системный HTTPS"); break; }
                    catch (CoreCommandException ex) when (ex.Code is "network_timeout" or "network_error")
                    {
                        diagnostics.Add("IPv4: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetwork) + "; IPv6: " + await SystemNetworkProbe.FamilyAsync(AddressFamily.InterNetworkV6));
                        if (client.LocalPort > 0) diagnostics.Add("Proxy DoH: " + await TunnelDns.CheckAsync(TunnelDns.Candidates[0], new Uri($"http://127.0.0.1:{client.LocalPort}")));
                        if (attempt == 1) throw;
                        await client.StopAsync(); await Task.Delay(1000);
                    }
                }
                if (!connected || !client.TrafficLocked) throw new Exception("Tunnel or protection not ready.");
                // A new client attaches to the existing connection; no UI-owned process.
                var replacement = new ServiceConnection(); await replacement.RefreshAsync();
                survivedClient = replacement.Active && replacement.ProfileId == profile.Id && replacement.TrafficLocked;
                if (!survivedClient) throw new Exception("Client reconnection lost session.");
                oldBlocked = !await KillSwitchChecks.HttpWorks(old);
                if (!oldBlocked) throw new Exception("Old physical flow still usable.");
                Session ReadSession() => JsonSerializer.Deserialize<Session>(File.ReadAllText(Path.Combine(serviceRoot, "session.json")))!;
                async Task<bool> Window(string phase)
                {
                    long begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var tcp = await KillSwitchChecks.TcpDenied(source, index);
                    var udp = await KillSwitchChecks.DnsProbe(source, index, dns);
                    var v6 = ipv6 == null ? (Status: "Unavailable", Port: 0) : await ipv6.Probe();
                    if (phase == "Engine stopped") engineIpv6 = v6.Status; else if (phase == "Adapter crashed and transport stopped") adapterIpv6 = v6.Status; else crashIpv6 = v6.Status;
                    windows.Add(new { Phase = phase, BeginMilliseconds = begin, EndMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TcpSourcePort = tcp.Port, Ipv6TcpSourcePort = v6.Port });
                    return tcp.Denied && !udp.Contains("LEAK") && (ipv6Baseline is not ("Reachable" or "ConnectionRefused") || v6.Status == "AccessDenied");
                }
                Stage("Engine failure"); await KillSwitchChecks.BreakTransport(ReadSession());
                engineBlocked = await Window("Engine stopped");
                if (!engineBlocked) throw new Exception("Engine failure allowed direct traffic.");
                await client.StopAsync();
                Stage("Second start"); await client.StartAsync(profile, true); await Probe();
                if (hasAdapter)
                {
                    Stage("Adapter crash");
                    var adapterSession = ReadSession();
                    uint servicePid = ServicePeer.RunningProcessId();
                    string adapterPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(installed)!, "..", "OneTunnel.Adapter.exe"));
                    var adapters = Process.GetProcessesByName("OneTunnel.Adapter").Where(p => string.Equals(p.MainModule?.FileName, adapterPath, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (adapters.Count != 1) throw new Exception("Expected one owned adapter.");
                    using (var adapter = adapters[0]) { adapter.Kill(); await adapter.WaitForExitAsync(); }
                    await KillSwitchChecks.BreakTransport(adapterSession);
                    for (int i = 0; i < 30; i++) { await client.RefreshAsync(); if (client.NeedsRecovery) break; await Task.Delay(100); }
                    goServiceSurvived = client.Available && client.NeedsRecovery && !client.Active && client.TrafficLocked && ServicePeer.RunningProcessId() == servicePid;
                    adapterBlocked = await Window("Adapter crashed and transport stopped");
                    if (!goServiceSurvived || !adapterBlocked || KillSwitch.InstalledFilterCount() == 0) throw new Exception("Adapter crash did not fail closed.");
                    try { await client.StartAsync(profile, true); throw new Exception("Failed adapter silently restarted."); }
                    catch (CoreCommandException ex) when (ex.Code == "backend_unavailable") { }
                    await client.RecoverAsync();
                    using var recovered = KillSwitchChecks.PhysicalHttp(source, index);
                    adapterRecovered = await KillSwitchChecks.HttpWorks(recovered);
                    if (!adapterRecovered) throw new Exception("Adapter recovery did not restore network.");
                    Stage("Third start"); await client.StartAsync(profile, true); await Probe();
                }
                var session = ReadSession();
                // Kill only the service process; its engine job closes automatically.
                Stage("Service crash"); using (var service = Process.GetProcessById((int)ServicePeer.RunningProcessId())) { service.Kill(); await service.WaitForExitAsync(); }
                await KillSwitchChecks.BreakTransport(session);
                persistent = File.Exists(Path.Combine(serviceRoot, "kill-switch.json")) && KillSwitch.InstalledFilterCount() > 0;
                crashBlocked = await Window("Service crashed and transport stopped");
                if (!persistent || !crashBlocked) throw new Exception("Service crash protection failed.");
                await Connection.RunAsync(Path.Combine(Environment.SystemDirectory, "sc.exe"), ["start", CoreProtocol.ServiceName]);
                for (int i = 0; i < 30; i++) { await client.RefreshAsync(); if (client.Available) break; await Task.Delay(200); }
                if (!client.NeedsRecovery || client.Active || !client.TrafficLocked) throw new Exception("Restart did not preserve recovery state.");
                Stage("Recovery"); await client.RecoverAsync();
                Stage("Physical HTTPS after recovery");
                using var direct = KillSwitchChecks.PhysicalHttp(source, index); restored = await KillSwitchChecks.HttpWorks(direct);
                if (!restored) throw new Exception("Recovery did not restore network.");
            }
            catch (Exception ex) { failure = stage + ": " + (ex is CoreCommandException command ? command.Code : ex.GetType().Name) + "; " + client.Status; }
            finally
            {
                Stage("Final cleanup");
                await Connection.RunAsync(Path.Combine(Environment.SystemDirectory, "sc.exe"), ["start", CoreProtocol.ServiceName]);
                await client.RefreshAsync();
                if (client.Available) await client.RecoverAsync();
                await ServiceSafetyWatch.Disarm(client);
                results.Add(new { Index = ordinal, Protocol = profile.Protocol.ToString(), Transport = Transport(profile), LocalFixture = profile.Id == fixture.Profile.Id || protocolFixtures.ProfileIds.Contains(profile.Id), SubscriptionRepresentativeUnavailable = unavailable.Contains(profile.Id), GoCore = goCore, NativeGo = nativeGo, AdapterPresent = hasAdapter, PreflightCandidatesTried = preflight.GetValueOrDefault(profile.Id, 1), ConnectedHttps = connected, ConnectionAttempts = connectionAttempts, HttpsProbeAttempts = probeAttempts, ProbeDiagnostics = diagnostics, ClientReattachment = survivedClient, ExistingPhysicalConnectionBlocked = oldBlocked, EngineFailureBlockedTcp = engineBlocked, AdapterFailureBlockedTcp = adapterBlocked, GoServiceSurvivedAdapterCrash = goServiceSurvived, AdapterRecoveryRestoredNetwork = adapterRecovered, AdapterFailureIpv6 = adapterIpv6, AppCrashBlockedTcp = crashBlocked, Ipv6Baseline = ipv6Baseline, EngineFailureIpv6 = engineIpv6, AppCrashIpv6 = crashIpv6, PersistentFiltersAfterCrash = persistent, RecoveryRestoredNetwork = restored, BlockedWindows = windows, Failure = failure });
                File.WriteAllText(Path.Combine(output, "kill-switch-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"Service {ordinal}: {profile.Protocol}, passed={failure == null}");
            if (failure != null) throw new Exception("Service checks incomplete; private report saved.");
        }
        await SystemModeChecks.Snapshot(Path.Combine(output, "after.txt"));
    }
    private static string Transport(Profile p) => p.Protocol == Protocol.VLESS ? ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() ?? "tcp" : "native";
}
