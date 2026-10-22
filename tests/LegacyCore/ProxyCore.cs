using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneTunnel.Core;
public static class ProxyCore
{
    public static bool NeedsXray(Protocol protocol) => protocol is Protocol.VLESS or Protocol.Hysteria2 or Protocol.Shadowsocks;
    public static bool IsProxy(Protocol protocol) => NeedsXray(protocol) || protocol == Protocol.SOCKS5;
    public static Protocol JsonProtocol(string source)
    {
        try { return JsonNode.Parse(source)?["protocol"]?.GetValue<string>() switch { "vless" => Protocol.VLESS, "shadowsocks" => Protocol.Shadowsocks, "hysteria" => Protocol.Hysteria2, _ => throw new FormatException("Нужен outbound VLESS, Hysteria2 или Shadowsocks; для подписки вставь весь JSON в новый профиль.") }; }
        catch (JsonException) { throw new FormatException("Неверный JSON профиля."); }
    }
    public static JsonObject Outbound(string source, Protocol protocol)
    {
        if (protocol == Protocol.VLESS) return VlessConfig.Outbound(source);
        if (protocol is not (Protocol.Hysteria2 or Protocol.Shadowsocks)) throw new FormatException("Неподдерживаемый тип профиля Xray.");
        JsonObject original;
        try { original = source.TrimStart().StartsWith('{') ? JsonNode.Parse(source) as JsonObject ?? throw new FormatException("Неверный JSON профиля.") : protocol == Protocol.Hysteria2 ? Hysteria(source) : Shadowsocks(source); }
        catch (JsonException) { throw new FormatException("Неверный JSON профиля."); }
        if (original.ContainsKey("proxySettings") || original.ContainsKey("dialerProxy")) throw new FormatException("Цепочки прокси из подписки не поддерживаются.");
        string S(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        string expected = protocol == Protocol.Hysteria2 ? "hysteria" : "shadowsocks";
        if (S(original["protocol"]) != expected) throw new FormatException("Протокол JSON не совпадает с типом профиля.");
        var settings = original["settings"] as JsonObject ?? throw new FormatException("В профиле отсутствует settings.");
        var stream = original["streamSettings"] as JsonObject ?? new JsonObject();
        JsonNode peer;
        if (protocol == Protocol.Hysteria2)
        {
            peer = settings;
            if (settings["version"]?.GetValue<int>() != 2 || S(stream["network"]) != "hysteria" || S(stream["security"]) != "tls" || stream["hysteriaSettings"]?["version"]?.GetValue<int>() != 2) throw new FormatException("Поддерживается Hysteria версии 2 с TLS. Hysteria 1 пока не поддерживается.");
        }
        else
        {
            if (settings["servers"] is not JsonArray peers || peers.Count != 1 || peers[0] is not JsonObject p) throw new FormatException("Один профиль Shadowsocks должен содержать один сервер.");
            peer = p;
            if (!new[] { "aes-128-gcm", "aes-256-gcm", "chacha20-poly1305", "chacha20-ietf-poly1305", "xchacha20-poly1305", "xchacha20-ietf-poly1305", "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm", "2022-blake3-chacha20-poly1305" }.Contains(S(peer["method"]))) throw new FormatException("Неподдерживаемый шифр Shadowsocks. Используй AEAD или Shadowsocks 2022.");
            if (string.IsNullOrEmpty(S(peer["password"]))) throw new FormatException("В профиле Shadowsocks отсутствует пароль.");
        }
        if (string.IsNullOrWhiteSpace(S(peer["address"])) || peer["port"] is not JsonValue pv || !pv.TryGetValue<int>(out var port) || port is < 1 or > 65535) throw new FormatException("Нужны адрес сервера и порт 1–65535.");
        VlessConfig.CheckSafe(settings); VlessConfig.CheckSafe(stream);
        return new JsonObject { ["tag"] = "proxy", ["protocol"] = expected, ["settings"] = settings.DeepClone(), ["streamSettings"] = stream.DeepClone() };
    }
    private static string Decode(string input)
    {
        try { string s = input.Replace('-', '+').Replace('_', '/'); s = s.PadRight((s.Length + 3) / 4 * 4, '='); return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch (FormatException) { throw new FormatException("Неверный Base64 в ссылке Shadowsocks."); }
    }
    private static JsonObject Shadowsocks(string source)
    {
        if (!source.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Нужна ссылка ss:// для Shadowsocks.");
        string body = source[5..].Split('#', 2)[0];
        if (body.Contains('?')) throw new FormatException("SIP003-плагины и дополнительные параметры Shadowsocks пока не поддерживаются. Импортируй JSON Xray для поддерживаемых настроек.");
        if (!body.Contains('@')) body = Decode(body);
        int at = body.LastIndexOf('@'); if (at < 1) throw new FormatException("В ссылке Shadowsocks нужны данные доступа и адрес сервера.");
        string auth = Uri.UnescapeDataString(body[..at]); if (!auth.Contains(':')) auth = Decode(auth);
        int colon = auth.IndexOf(':'); if (colon < 1) throw new FormatException("Нужны метод шифрования и пароль Shadowsocks.");
        if (!Uri.TryCreate("ss://" + body[(at + 1)..].TrimEnd('/'), UriKind.Absolute, out var uri) || uri.Port is < 1 or > 65535 || string.IsNullOrEmpty(uri.Host)) throw new FormatException("В ссылке Shadowsocks нужен сервер и порт.");
        return new JsonObject { ["protocol"] = "shadowsocks", ["settings"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["address"] = uri.IdnHost.Trim('[', ']'), ["port"] = uri.Port, ["method"] = auth[..colon], ["password"] = auth[(colon + 1)..] }) }, ["streamSettings"] = new JsonObject { ["network"] = "tcp", ["security"] = "none" } };
    }
    private static JsonObject Hysteria(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("hy2" or "hysteria2") || string.IsNullOrEmpty(uri.Host)) throw new FormatException("Нужна ссылка hy2:// или hysteria2://. Для port hopping импортируй JSON Xray.");
        if (uri.AbsolutePath is not ("" or "/")) throw new FormatException("Неверный путь ссылки Hysteria2.");
        int port = uri.Port == -1 ? 443 : uri.Port; if (port is < 1 or > 65535) throw new FormatException("Неверный порт Hysteria2.");
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var p = item.Split('=', 2); if (!q.TryAdd(Uri.UnescapeDataString(p[0]), p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "")) throw new FormatException("Повторяющийся параметр Hysteria2."); }
        if (q.Keys.Any(k => !new[] { "sni", "insecure", "pinSHA256", "obfs", "obfs-password", "ech", "alpn" }.Contains(k, StringComparer.OrdinalIgnoreCase))) throw new FormatException("Неизвестный параметр Hysteria2. Расширенные настройки импортируй как JSON Xray.");
        if (q.GetValueOrDefault("insecure", "0") is not ("0" or "false")) throw new FormatException("Отключение проверки сертификата Hysteria2 не поддерживается.");
        var tls = new JsonObject { ["serverName"] = q.GetValueOrDefault("sni", uri.Host), ["allowInsecure"] = false, ["alpn"] = new JsonArray("h3") };
        if (q.TryGetValue("pinSHA256", out var pin)) tls["pinnedPeerCertSha256"] = pin;
        if (q.TryGetValue("ech", out var ech)) tls["echConfigList"] = ech;
        if (q.TryGetValue("alpn", out var alpn)) tls["alpn"] = new JsonArray(alpn.Split(',').Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        var stream = new JsonObject { ["network"] = "hysteria", ["security"] = "tls", ["tlsSettings"] = tls, ["hysteriaSettings"] = new JsonObject { ["version"] = 2, ["auth"] = Uri.UnescapeDataString(uri.UserInfo) } };
        if (q.TryGetValue("obfs", out var obfs) && !string.IsNullOrEmpty(obfs))
        {
            if (obfs != "salamander" || !q.TryGetValue("obfs-password", out var password) || password.Length < 4) throw new FormatException("Для ссылки поддерживается obfs=salamander с паролем. Gecko и дополнительные маски импортируй через JSON Xray.");
            stream["finalmask"] = new JsonObject { ["udp"] = new JsonArray(new JsonObject { ["type"] = "salamander", ["settings"] = new JsonObject { ["password"] = password } }) };
        }
        return new JsonObject { ["protocol"] = "hysteria", ["settings"] = new JsonObject { ["address"] = uri.IdnHost.Trim('[', ']'), ["port"] = port, ["version"] = 2 }, ["streamSettings"] = stream };
    }
    public static string Build(Profile profile, int socksPort) => new JsonObject
    {
        ["log"] = new JsonObject { ["loglevel"] = "none" },
        ["inbounds"] = new JsonArray(new JsonObject { ["tag"] = "local", ["listen"] = "127.0.0.1", ["port"] = socksPort, ["protocol"] = "socks", ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true } }),
        ["outbounds"] = new JsonArray(Outbound(profile.Source, profile.Protocol))
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
