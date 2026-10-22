using System.Text.Json;
using OneTunnel.Core;

internal static class DnsProviderChecks
{
    public static async Task Run(string transport = "xhttp")
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneTunnel", "profiles.bin");
        using var document = JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file), false));
        var list = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement : document.RootElement.GetProperty("Profiles");
        var profiles = JsonSerializer.Deserialize<List<Profile>>(list)!;
        var profile = profiles.First(p => p.Protocol == Protocol.VLESS && ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() == transport);
        var connection = new Connection();
        try
        {
            await connection.StartAsync(profile, false);
            Console.WriteLine(await connection.ProbeAsync());
            foreach (var dns in TunnelDns.Candidates)
                Console.WriteLine($"DoH {dns.ServerName}: {await TunnelDns.CheckAsync(dns, new Uri($"http://127.0.0.1:{connection.LocalPort}"))}");
        }
        finally { await connection.StopAsync(); }
    }
}
