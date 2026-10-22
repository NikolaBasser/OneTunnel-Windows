using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using OneTunnel.Core;

internal static class SystemModeChecks
{
    public static async Task Run()
    {
        if (!Connection.IsAdministrator) throw new InvalidOperationException("System-mode tests require an elevated Windows administrator token.");
        string output = Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_REPORT_DIR") ?? Path.Combine(Storage.Root, "system-report");
        Storage.PrivateDirectory(output);
        var profiles = new List<Profile>();
        var profileFile = Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_PROFILES");
        if (profileFile != null && File.Exists(profileFile))
        {
            byte[] clear = Storage.Protect(File.ReadAllBytes(profileFile), false);
            using var document = JsonDocument.Parse(clear);
            var list = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : document.RootElement.GetProperty("Profiles");
            profiles.AddRange(JsonSerializer.Deserialize<List<Profile>>(list) ?? []);
        }
        if (Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL") is { Length: > 0 } url)
            profiles.AddRange((await Subscriptions.DownloadAsync(url)).Profiles);
        var results = new List<object>();
        await Snapshot(Path.Combine(output, "before.txt"));
        using var socksFixture = profiles.Any(p => p.Protocol == Protocol.SOCKS5) ? null : await SocksSystemFixture.Start();
        if (socksFixture != null) profiles.Add(socksFixture.Profile);
        // One connection per transport, with a separate verdict for each missing protocol.
        string Transport(Profile p) => p.Protocol is Protocol.VLESS ? ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() ?? "tcp" : "native";
        var selected = profiles.GroupBy(p => (p.Protocol, Transport: Transport(p))).Select(g => g.First()).ToList();
        foreach (var protocol in Enum.GetValues<Protocol>().Where(p => !selected.Any(s => s.Protocol == p)))
            results.Add(new { Protocol = protocol.ToString(), Status = "Skipped: no configuration", DnsLeakTest = "Not run", IPv6LeakTest = "Not run" });
        using var watchdog = Process.Start(Connection.Info(Environment.ProcessPath!, [typeof(SystemModeChecks).Assembly.Location, "--system-watch", Environment.ProcessId.ToString()]));
        int index = 0;
        foreach (var profile in selected)
        {
            index++;
            var diagnosticCounts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var connection = new Connection(line =>
            {
                foreach (var word in new[] { "timeout", "unreachable", "refused", "resolve", "certificate", "denied", "EOF", "ntp", "started" })
                    if (line.Contains(word, StringComparison.OrdinalIgnoreCase)) diagnosticCounts.AddOrUpdate(word, 1, (_, value) => value + 1);
            });
            string stage = "Policy";
            string status = "Not started";
            string localStatus = "Not applicable";
            string bootstrapStatus = "Not applicable";
            long begin = 0, end = 0;
            try
            {
                TunnelPolicy.RequireFullDevice(TunnelPolicy.ForFullDevice(profile));
                if (ProxyCore.IsProxy(profile.Protocol))
                {
                    stage = "Local proxy comparison";
                    try { await connection.StartAsync(profile, false); localStatus = await connection.ProbeAsync(); }
                    catch (Exception ex) { localStatus = "Failed: " + ex.GetType().Name; }
                    finally { await connection.StopAsync(); }
                    if (ProxyCore.NeedsXray(profile.Protocol) && !ShadowsocksNative.CanUse(profile))
                    {
                        var prepared = JsonNode.Parse(ProxyCore.Build(profile, 19767))!;
                        await Connection.PrepareTunEndpoint(prepared);
                        try
                        {
                            await connection.StartAsync(profile with { Source = prepared["outbounds"]![0]!.ToJsonString() }, false);
                            bootstrapStatus = await connection.ProbeAsync();
                        }
                        catch (Exception ex) { bootstrapStatus = "Failed: " + ex.GetType().Name; }
                        finally { await connection.StopAsync(); }
                    }
                }
                stage = "Start";
                await connection.StartAsync(profile, true);
                await Task.Delay(1500);
                await Snapshot(Path.Combine(output, $"connected-{index}.txt"));
                begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                stage = "Probe";
                status = await connection.ProbeAsync();
                // Generate fresh system resolver traffic while a separately started packet
                // capture observes the physical adapters. No proxy handler is used here.
                try { await Dns.GetHostAddressesAsync(Guid.NewGuid().ToString("N") + ".example.com").WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            catch (Exception ex) { status = "Failed at " + stage + ": " + ex.GetType().Name + (ex is InvalidOperationException ? ": " + ex.Message : ""); }
            finally
            {
                if (connection.Active)
                {
                    if (begin == 0) begin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    try { await Dns.GetHostAddressesAsync($"onetunnel-{index}-" + Guid.NewGuid().ToString("N") + ".example.com").WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
                    end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
                await connection.StopAsync();
                await Snapshot(Path.Combine(output, $"restored-{index}.txt"));
                results.Add(new { Index = index, Protocol = profile.Protocol.ToString(), Transport = Transport(profile), LocalFixture = socksFixture?.Profile.Id == profile.Id, Status = status, LocalStatus = localStatus, BootstrapLocalStatus = bootstrapStatus, BeginMilliseconds = begin, EndMilliseconds = end, DiagnosticCounts = diagnosticCounts, DnsLeakTest = "Packet capture review required", IPv6LeakTest = "Packet capture and baseline IPv6 review required" });
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"System check {index}: {profile.Protocol} ({Transport(profile)}); inspect private report.");
        }
        await Snapshot(Path.Combine(output, "after.txt"));
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("System checks finished. Functional success does not certify absence of leaks; review packet capture and before/after snapshots.");
    }
    public static async Task Watch(int parentId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentId);
            try { await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(20)); }
            // Killing the tree would also kill this recovery helper. Proxy engines
            // belong to the parent's kill-on-close job; WG services use the journal.
            catch (TimeoutException) { if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); } }
        }
        catch (ArgumentException) { /* Parent already exited before the watcher started. */ }
        if (Connection.NeedsRecovery) await new Connection().RecoverAsync();
    }
    internal static async Task Snapshot(string file)
    {
        using var process = Process.Start(Connection.Info(Path.Combine(Environment.SystemDirectory, "route.exe"), ["print"]))!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        string text = await output; await error;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ip = adapter.GetIPProperties();
            text += $"\nInterface: {adapter.Name}; status={adapter.OperationalStatus}; DNS={string.Join(',', ip.DnsAddresses)}; addresses={string.Join(',', ip.UnicastAddresses.Select(a => a.Address))}";
        }
        File.WriteAllText(file, text);
    }
}
