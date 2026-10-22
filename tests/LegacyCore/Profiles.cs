using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OneTunnel.Core;
public enum Protocol { AmneziaWG, WireGuard, VLESS, SOCKS5, Hysteria2, Shadowsocks }
public sealed record Profile(Guid Id, string Name, Protocol Protocol, string Source)
{
    public string Group { get; init; } = "Без группы";
    public TransportOptions? Transport { get; init; }
    public string Label => $"{Name}  ·  {Protocol}";
    public override string ToString() => Label;
}
public static class ProfileParser
{
    public static Profile Parse(string name, string source, Protocol? requested = null)
    {
        source = source.Trim();
        if (source.Length is 0 or > 131072) throw new FormatException("Конфигурация пуста или больше 128 КБ.");
        if (AmneziaImport.IsLink(source)) return AmneziaImport.Parse(name, source);
        Protocol protocol;
        if (source.StartsWith('{')) { protocol = ProxyCore.JsonProtocol(source); _ = ProxyCore.Outbound(source, protocol); }
        else if (source.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) { protocol = Protocol.VLESS; _ = VlessConfig.Outbound(source); }
        else if (source.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) { protocol = Protocol.Shadowsocks; _ = ProxyCore.Outbound(source, protocol); }
        else if (source.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)) { protocol = Protocol.Hysteria2; _ = ProxyCore.Outbound(source, protocol); }
        else if (source.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)) { protocol = Protocol.SOCKS5; _ = ProxyOutbound(source, protocol); }
        else
        {
            var awg = Regex.IsMatch(source, @"(?mi)^\s*(Jc|Jmin|Jmax|S[1-4]|H[1-4]|I[1-5])\s*=");
            protocol = requested ?? (awg ? Protocol.AmneziaWG : Protocol.WireGuard);
            if (protocol is not (Protocol.WireGuard or Protocol.AmneziaWG)) throw new FormatException("Вставь ссылку соответствующего протокола или JSON Xray. Для Hysteria поддерживается версия 2 (hy2:// или hysteria2://).");
            if (awg && protocol == Protocol.WireGuard) throw new FormatException("В конфигурации есть параметры AmneziaWG. Выбери AmneziaWG.");
            ValidateTunnel(source, protocol);
        }
        if (requested is not null && protocol != requested) throw new FormatException("Тип ссылки не совпадает с выбранным протоколом.");
        if (string.IsNullOrWhiteSpace(name))
        {
            if (ProxyCore.IsProxy(protocol) && !source.StartsWith('{') && source.IndexOf('#') is var hash && hash >= 0) name = Uri.UnescapeDataString(source[(hash + 1)..]);
            if (string.IsNullOrWhiteSpace(name)) name = $"{protocol} · новое подключение";
        }
        if (name.Length > 4096 || !DisplayText.IsWellFormed(name) || DisplayText.Elements(name).Take(101).Count() > 100 || name.Any(char.IsControl)) throw new FormatException("Название должно содержать не больше 100 символов или эмодзи, без управляющих символов.");
        return new(Guid.NewGuid(), name.Trim(), protocol, source);
    }
    private static void ValidateTunnel(string source, Protocol protocol)
    {
        string section = ""; int interfaces = 0, peers = 0;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowedInterface = new HashSet<string>(["PrivateKey", "Address", "DNS", "MTU", "ListenPort"], StringComparer.OrdinalIgnoreCase);
        var allowedPeer = new HashSet<string>(["PublicKey", "PresharedKey", "AllowedIPs", "Endpoint", "PersistentKeepalive"], StringComparer.OrdinalIgnoreCase);
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim(); if (line.Length == 0) continue;
            if (line.Equals("[Interface]", StringComparison.OrdinalIgnoreCase)) { section = "I"; interfaces++; continue; }
            if (line.Equals("[Peer]", StringComparison.OrdinalIgnoreCase)) { section = "P"; peers++; continue; }
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || section == "" || pair[1].Length == 0) throw new FormatException("Неверный формат WG/AWG-конфигурации.");
            bool awg = protocol == Protocol.AmneziaWG && section == "I" && Regex.IsMatch(pair[0], "^(Jc|Jmin|Jmax|S[1-4]|H[1-4]|I[1-5])$");
            if (!(section == "I" ? allowedInterface : allowedPeer).Contains(pair[0]) && !awg)
                throw new FormatException($"Параметр {pair[0]} не поддерживается. Скрипты подключения запрещены.");
            if (!fields.TryAdd(section + pair[0], pair[1])) throw new FormatException($"Повторяющийся параметр {pair[0]}.");
        }
        if (interfaces != 1 || peers != 1) throw new FormatException("В прототипе поддерживается один Interface и один Peer.");
        foreach (var key in new[] { "IPrivateKey", "PPublicKey" })
        {
            if (!fields.TryGetValue(key, out var value)) throw new FormatException("Нужны PrivateKey и PublicKey.");
            try { if (Convert.FromBase64String(value).Length != 32) throw new Exception(); } catch { throw new FormatException("Ключ WG/AWG должен содержать 32 байта в Base64."); }
        }
        if (fields.TryGetValue("PPresharedKey", out var psk)) { try { if (Convert.FromBase64String(psk).Length != 32) throw new Exception(); } catch { throw new FormatException("Неверный PresharedKey."); } }
        foreach (var key in new[] { "IAddress", "PAllowedIPs" })
        {
            if (!fields.TryGetValue(key, out var value)) throw new FormatException("Нужны Address и AllowedIPs.");
            foreach (var cidr in value.Split(',', StringSplitOptions.TrimEntries))
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out int prefix) || prefix < 0 || prefix > (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128)) throw new FormatException("Неверный IP-адрес или маска подсети.");
            }
        }
        if (!fields.TryGetValue("PEndpoint", out var endpoint) || !Uri.TryCreate("udp://" + endpoint, UriKind.Absolute, out var ep) || ep.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(ep.Host)) throw new FormatException("Нужен Endpoint вида server:51820.");
    }
    public static JsonObject ProxyOutbound(string source, Protocol protocol)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(uri.Host)) throw new FormatException("В ссылке нужны адрес сервера и порт 1–65535.");
        if (uri.AbsolutePath is not ("" or "/")) throw new FormatException("Путь транспорта должен быть в параметре path, а не после адреса сервера.");
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=', 2); var k = Uri.UnescapeDataString(kv[0]);
            if (!q.TryAdd(k, kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "")) throw new FormatException("Повторяющийся параметр ссылки.");
        }
        var result = new JsonObject { ["type"] = protocol == Protocol.SOCKS5 ? "socks" : "vless", ["tag"] = "proxy", ["server"] = uri.IdnHost.Trim('[', ']'), ["server_port"] = uri.Port };
        if (protocol == Protocol.SOCKS5)
        {
            if (!uri.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase) || q.Count > 0) throw new FormatException("Нужна ссылка socks5://[user:password@]host:port без дополнительных параметров.");
            result["version"] = "5";
            if (uri.UserInfo.Length > 0)
            {
                var auth = uri.UserInfo.Split(':', 2); string user = Uri.UnescapeDataString(auth[0]), password = auth.Length > 1 ? Uri.UnescapeDataString(auth[1]) : "";
                if (Encoding.UTF8.GetByteCount(user) is < 1 or > 255 || Encoding.UTF8.GetByteCount(password) > 255) throw new FormatException("Логин SOCKS5 должен занимать 1–255 байт, пароль — не больше 255 байт.");
                result["username"] = user; result["password"] = password;
            }
            return result;
        }
        var known = new HashSet<string>(["encryption", "security", "type", "flow", "sni", "fp", "pbk", "sid", "spx", "alpn", "path", "host", "serviceName"], StringComparer.OrdinalIgnoreCase);
        if (q.Keys.Any(k => !known.Contains(k))) throw new FormatException("В VLESS-ссылке есть неподдерживаемые параметры. Прототип принимает TCP, WS и gRPC, TLS/REALITY.");
        if (q.TryGetValue("encryption", out var enc) && enc != "none") throw new FormatException("Поддерживается encryption=none.");
        if (!Guid.TryParse(Uri.UnescapeDataString(uri.UserInfo), out var uuid)) throw new FormatException("В VLESS-ссылке нужен корректный UUID.");
        result["uuid"] = uuid.ToString();
        var flow = q.GetValueOrDefault("flow", "");
        if (flow is not ("" or "xtls-rprx-vision")) throw new FormatException("Поддерживается только flow=xtls-rprx-vision.");
        if (flow != "") result["flow"] = flow;
        string security = q.GetValueOrDefault("security", "none"), transport = q.GetValueOrDefault("type", "tcp");
        if (transport is not ("tcp" or "ws" or "grpc")) throw new FormatException("Поддерживаются транспорты TCP, WS и gRPC.");
        if (flow != "" && (transport != "tcp" || security == "none")) throw new FormatException("Vision требует TCP и TLS/REALITY.");
        if (transport == "ws") result["transport"] = new JsonObject { ["type"] = "ws", ["path"] = q.GetValueOrDefault("path", "/"), ["headers"] = new JsonObject { ["Host"] = q.GetValueOrDefault("host", uri.Host) } };
        if (transport == "grpc") result["transport"] = new JsonObject { ["type"] = "grpc", ["service_name"] = q.GetValueOrDefault("serviceName", "") };
        if (security is not ("none" or "tls" or "reality")) throw new FormatException("Поддерживаются security=none, tls и reality.");
        if (security != "none")
        {
            var tls = new JsonObject { ["enabled"] = true, ["server_name"] = q.GetValueOrDefault("sni", uri.Host) };
            if (q.TryGetValue("alpn", out var alpn)) tls["alpn"] = new JsonArray(alpn.Split(',').Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            if (q.TryGetValue("fp", out var fp) || security == "reality") tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = fp ?? "chrome" };
            if (security == "reality")
            {
                if (!q.TryGetValue("pbk", out var pbk) || string.IsNullOrWhiteSpace(pbk)) throw new FormatException("Для REALITY нужен pbk.");
                if (q.GetValueOrDefault("spx", "/") != "/") throw new FormatException("Нестандартный spx не поддерживается этим движком.");
                tls["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = pbk, ["short_id"] = q.GetValueOrDefault("sid", "") };
            }
            result["tls"] = tls;
        }
        return result;
    }
    public static string BuildProxyConfig(Profile profile, int port, bool tun, int? vlessBridgePort = null, ClockPlan? clock = null)
    {
        var inbound = new JsonObject { ["type"] = "mixed", ["tag"] = "local", ["listen"] = "127.0.0.1", ["listen_port"] = port };
        var inbounds = new JsonArray(inbound);
        var outbound = vlessBridgePort is { } bridge ? new JsonObject { ["type"] = "socks", ["tag"] = "proxy", ["server"] = "127.0.0.1", ["server_port"] = bridge, ["version"] = "5" } : ShadowsocksNative.CanUse(profile) ? ShadowsocksNative.Outbound(profile) : ProxyOutbound(profile.Source, profile.Protocol);
        var config = new JsonObject { ["log"] = new JsonObject { ["level"] = "error" }, ["inbounds"] = inbounds, ["outbounds"] = new JsonArray(outbound), ["route"] = new JsonObject { ["final"] = "proxy", ["auto_detect_interface"] = true } };
        if (clock?.Synchronize == true)
        {
            config["ntp"] = new JsonObject { ["enabled"] = true, ["server"] = clock.Server, ["server_port"] = clock.Port, ["interval"] = "30m" };
            config["log"]!["level"] = "info";
        }
        if (tun)
        {
            // Split defaults outrank the existing physical default route on Windows.
            inbounds.Add(new JsonObject { ["type"] = "tun", ["tag"] = "tun-in", ["interface_name"] = "OneTunnel", ["address"] = new JsonArray("172.30.19.1/30", "fdfe:dcba:9876::1/126"), ["auto_route"] = true, ["strict_route"] = true, ["dns_mode"] = "hijack", ["route_address"] = new JsonArray("0.0.0.0/1", "128.0.0.0/1", "::/1", "8000::/1"), ["stack"] = "mixed" });
            config["dns"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["type"] = "https", ["tag"] = "remote-dns", ["server"] = "1.1.1.1", ["server_port"] = 443, ["path"] = "/dns-query", ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = "cloudflare-dns.com" }, ["detour"] = "proxy" }), ["final"] = "remote-dns" };
            config["route"]!["rules"] = new JsonArray(new JsonObject { ["inbound"] = new JsonArray("tun-in"), ["action"] = "sniff" }, new JsonObject { ["port"] = 53, ["action"] = "hijack-dns" }, new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" });
            if (vlessBridgePort != null)
            {
                ((JsonArray)config["outbounds"]!).Add(new JsonObject { ["type"] = "direct", ["tag"] = "xray-uplink" });
                ((JsonArray)config["route"]!["rules"]!).Insert(0, new JsonObject { ["process_path"] = new JsonArray(Connection.XrayPath), ["action"] = "route", ["outbound"] = "xray-uplink" });
            }
        }
        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
