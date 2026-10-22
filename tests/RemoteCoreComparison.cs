using System.Text.Json;
using OneTunnel.Client;
using OneTunnel.Core;

internal static class RemoteCoreComparison
{
    internal static async Task Run()
    {
        var service = new ServiceConnection(); await service.RefreshAsync();
        if (!service.Available || service.Active || service.NeedsRecovery) throw new Exception("Idle service required.");
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneTunnel", "profiles.bin");
        using var doc = JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file), false));
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("Profiles");
        var profiles = JsonSerializer.Deserialize<List<Profile>>(list)!;
        var rows = new List<object>();
        foreach (var protocol in new[] { Protocol.VLESS, Protocol.Hysteria2, Protocol.Shadowsocks })
        {
            var candidates = profiles.Where(p => p.Protocol == protocol && (protocol != Protocol.VLESS || (ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() ?? "tcp") == "tcp")).Take(3);
            int index = 0;
            foreach (var profile in candidates)
            {
                index++;
                bool legacy = false, go = false;
                var reasons = new List<string>();
                var old = new Connection(line => reasons.Add(line));
                try { await old.StartAsync(profile, false); await old.ProbeAsync(); legacy = true; }
                catch { }
                finally { await old.StopAsync(); }
                string classification = ProviderDiagnostics.Classify(reasons);
                try { await service.StartAsync(profile, false); await service.ProbeAsync(); go = true; }
                catch (CoreCommandException) { }
                finally { await service.StopAsync(); }
                rows.Add(new { Protocol = protocol.ToString(), Candidate = index, LegacyHttps = legacy, GoHttps = go, LegacyFailureCategory = classification });
                Console.WriteLine($"REMOTE COMPARISON: {protocol}, candidate={index}, legacy={legacy}, go={go}, category={classification}");
                if (legacy != go) throw new Exception("Remote compatibility difference detected.");
            }
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ONETUNNEL_COMPARISON_REPORT_DIR")!, "remote-comparison.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}
