using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace OneTunnel.Core;
public sealed record ClockPlan(bool Synchronize, string Server = "time.cloudflare.com", int Port = 123, double? OffsetSeconds = null);
public static class ShadowsocksNative
{
    public static bool CanUse(Profile p)
    {
        if (p.Protocol != Protocol.Shadowsocks) return false;
        var ob = ProxyCore.Outbound(p.Source, p.Protocol); var stream = ob["streamSettings"]!;
        if (stream["network"]?.GetValue<string>() is not (null or "" or "tcp" or "raw") || stream["security"]?.GetValue<string>() is not (null or "" or "none")) return false;
        if (stream.AsObject().Any(kv => kv.Key is not ("network" or "security" or "tcpSettings" or "rawSettings"))) return false;
        foreach (var key in new[] { "tcpSettings", "rawSettings" }) if (stream[key] is JsonObject settings && settings.Count > 0 && (settings.Any(pair => pair.Key != "header") || settings["header"]?["type"]?.GetValue<string>() is not (null or "" or "none"))) return false;
        return true;
    }
    public static JsonObject Outbound(Profile p)
    {
        if (!CanUse(p)) throw new FormatException("Для расширенных транспортных настроек Shadowsocks требуется Xray.");
        var peer = ProxyCore.Outbound(p.Source, p.Protocol)["settings"]!["servers"]![0]!;
        string method = peer["method"]!.GetValue<string>();
        method = method switch { "chacha20-poly1305" => "chacha20-ietf-poly1305", "xchacha20-poly1305" => "xchacha20-ietf-poly1305", _ => method };
        var outbound = new JsonObject { ["type"] = "shadowsocks", ["tag"] = "proxy", ["server"] = peer["address"]!.DeepClone(), ["server_port"] = peer["port"]!.DeepClone(), ["method"] = method, ["password"] = peer["password"]!.DeepClone() };
        if (peer["uot"]?.GetValue<bool>() == true) outbound["udp_over_tcp"] = new JsonObject { ["enabled"] = true, ["version"] = peer["UoTVersion"]?.DeepClone() ?? JsonValue.Create(2) };
        return outbound;
    }
    public static bool RequiresAccurateTime(Profile p) => CanUse(p) && Outbound(p)["method"]!.GetValue<string>().StartsWith("2022-", StringComparison.Ordinal);
    public static async Task<ClockPlan> PlanClockAsync()
    {
        double? offset = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var before = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.cloudflare.com/cdn-cgi/trace?onetunnel=" + Guid.NewGuid().ToString("N"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode && response.Headers.Date is { } remote)
            {
                var midpoint = before + (DateTimeOffset.UtcNow - before) / 2;
                offset = (remote - midpoint).TotalSeconds;
                if (Math.Abs(offset.Value) <= 20) return new(false, OffsetSeconds: offset);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        try
        {
            var ips = await Dns.GetHostAddressesAsync("time.cloudflare.com").WaitAsync(TimeSpan.FromSeconds(4));
            string server = (ips.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? ips.First()).ToString();
            return new(true, server, 123, offset);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException) { throw new InvalidOperationException("Не удалось проверить точное время для Shadowsocks 2022. Проверь сеть или синхронизируй часы Windows."); }
    }
}
