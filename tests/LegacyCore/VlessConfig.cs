using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneTunnel.Core;
public static class VlessConfig
{
    private static readonly HashSet<string> Networks = new(["tcp", "raw", "ws", "websocket", "grpc", "httpupgrade", "xhttp", "splithttp", "kcp", "mkcp", "hysteria"], StringComparer.Ordinal);
    public static JsonObject Outbound(string source)
    {
        if (source.TrimStart().StartsWith('{'))
        {
            try { return Validate((JsonNode.Parse(source, documentOptions: new JsonDocumentOptions { MaxDepth = 48 }) as JsonObject) ?? throw new FormatException()); }
            catch (JsonException) { throw new FormatException("Неверный JSON-конфиг VLESS."); }
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("vless", StringComparison.OrdinalIgnoreCase) || uri.Port is < 1 or > 65535 || !Guid.TryParse(Uri.UnescapeDataString(uri.UserInfo), out var id)) throw new FormatException("Нужна VLESS-ссылка с UUID, адресом сервера и портом.");
        if (uri.AbsolutePath is not ("" or "/")) throw new FormatException("Путь транспорта укажи в параметре path.");
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            if (!q.TryAdd(Uri.UnescapeDataString(pair[0]), pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "")) throw new FormatException("Повторяющийся параметр VLESS-ссылки.");
        }
        var known = new HashSet<string>(["encryption", "security", "type", "flow", "sni", "fp", "pbk", "sid", "spx", "alpn", "path", "host", "serviceName", "mode", "extra", "headerType", "seed", "authority", "multiMode", "allowInsecure", "insecure", "ed", "eh", "pcs", "pqv", "ech", "publicKey", "password", "auth"], StringComparer.OrdinalIgnoreCase);
        if (q.Keys.Any(k => !known.Contains(k))) throw new FormatException("В VLESS-ссылке есть неизвестные параметры. Импортируй исходный JSON Xray, чтобы сохранить расширенные настройки.");
        string network = q.GetValueOrDefault("type", "tcp"), security = q.GetValueOrDefault("security", "none");
        var stream = new JsonObject { ["network"] = network, ["security"] = security };
        var user = new JsonObject { ["id"] = id.ToString(), ["encryption"] = q.GetValueOrDefault("encryption", "none"), ["flow"] = q.GetValueOrDefault("flow", "") };
        JsonArray Alpn() => new(q.GetValueOrDefault("alpn", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        if (q.GetValueOrDefault("allowInsecure", "false") is "1" or "true" || q.GetValueOrDefault("insecure", "false") is "1" or "true") throw new FormatException("Отключение проверки сертификата не поддерживается.");
        if (security == "tls")
        {
            var tls = new JsonObject { ["serverName"] = q.GetValueOrDefault("sni", uri.Host), ["fingerprint"] = q.GetValueOrDefault("fp", "chrome"), ["allowInsecure"] = false };
            if (q.ContainsKey("alpn")) tls["alpn"] = Alpn();
            if (q.TryGetValue("pcs", out var pcs)) tls["pinnedPeerCertSha256"] = pcs;
            if (q.TryGetValue("ech", out var ech)) tls["echConfigList"] = ech;
            stream["tlsSettings"] = tls;
        }
        if (security == "reality")
        {
            var reality = new JsonObject { ["serverName"] = q.GetValueOrDefault("sni", uri.Host), ["fingerprint"] = q.GetValueOrDefault("fp", "chrome"), ["publicKey"] = q.GetValueOrDefault("pbk", q.GetValueOrDefault("publicKey", q.GetValueOrDefault("password", ""))), ["shortId"] = q.GetValueOrDefault("sid", ""), ["spiderX"] = q.GetValueOrDefault("spx", "/") };
            if (q.TryGetValue("pqv", out var pqv)) reality["mldsa65Verify"] = pqv;
            stream["realitySettings"] = reality;
        }
        string path = q.GetValueOrDefault("path", "/"), host = q.GetValueOrDefault("host", "");
        if (network is "xhttp" or "splithttp")
        {
            var xhttp = new JsonObject { ["path"] = path, ["host"] = host, ["mode"] = q.GetValueOrDefault("mode", "auto") };
            if (q.TryGetValue("extra", out var extra))
            {
                try { xhttp["extra"] = JsonNode.Parse(extra) as JsonObject ?? throw new FormatException("extra должен содержать JSON-объект."); }
                catch (JsonException) { throw new FormatException("Неверный JSON в параметре extra XHTTP."); }
            }
            stream["xhttpSettings"] = xhttp;
        }
        if (network is "ws" or "websocket")
        {
            var ws = new JsonObject { ["path"] = path, ["headers"] = new JsonObject { ["Host"] = host } };
            if (q.TryGetValue("ed", out var ed)) { if (!int.TryParse(ed, out int amount) || amount < 0) throw new FormatException("Неверный ed WebSocket."); ws["maxEarlyData"] = amount; }
            if (q.TryGetValue("eh", out var eh)) ws["earlyDataHeaderName"] = eh;
            stream["wsSettings"] = ws;
        }
        if (network == "httpupgrade") stream["httpupgradeSettings"] = new JsonObject { ["host"] = host, ["path"] = path };
        if (network == "grpc") stream["grpcSettings"] = new JsonObject { ["serviceName"] = q.GetValueOrDefault("serviceName", ""), ["authority"] = q.GetValueOrDefault("authority", ""), ["multiMode"] = q.GetValueOrDefault("multiMode", q.GetValueOrDefault("mode", "")) is "true" or "multi" };
        if (network is "tcp" or "raw")
        {
            string header = q.GetValueOrDefault("headerType", "none");
            var tcp = new JsonObject { ["header"] = new JsonObject { ["type"] = header } };
            if (header == "http") tcp["header"]!["request"] = new JsonObject { ["path"] = new JsonArray(path), ["headers"] = new JsonObject { ["Host"] = new JsonArray(host) } };
            stream["tcpSettings"] = tcp;
        }
        if (network is "kcp" or "mkcp")
        {
            if (!string.IsNullOrEmpty(q.GetValueOrDefault("seed")) || q.GetValueOrDefault("headerType", "none") != "none") throw new FormatException("Старые seed/header mKCP удалены из Xray. Импортируй актуальный JSON с finalmask.");
            stream["kcpSettings"] = new JsonObject();
        }
        if (network == "quic") throw new FormatException("Старый транспорт QUIC удалён из Xray; используй актуальный XHTTP поверх HTTP/3.");
        if (network == "hysteria") stream["hysteriaSettings"] = new JsonObject { ["version"] = 2, ["auth"] = q.GetValueOrDefault("auth", q.GetValueOrDefault("password", "")) };
        return Validate(new JsonObject { ["protocol"] = "vless", ["settings"] = new JsonObject { ["vnext"] = new JsonArray(new JsonObject { ["address"] = uri.IdnHost.Trim('[', ']'), ["port"] = uri.Port, ["users"] = new JsonArray(user) }) }, ["streamSettings"] = stream });
    }
    public static JsonObject Validate(JsonObject original)
    {
        string S(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        if (S(original["protocol"]) != "vless") throw new FormatException("Нужен outbound протокола VLESS.");
        if (original.ContainsKey("proxySettings") || original.ContainsKey("dialerProxy")) throw new FormatException("Цепочки прокси из подписки пока не поддерживаются.");
        var settings = original["settings"] as JsonObject ?? throw new FormatException("В JSON VLESS отсутствует settings.");
        if (settings["vnext"] is not JsonArray peers || peers.Count != 1 || peers[0] is not JsonObject peer || peer["users"] is not JsonArray users || users.Count != 1 || users[0] is not JsonObject user) throw new FormatException("Один профиль должен содержать один VLESS-сервер и одного пользователя.");
        if (string.IsNullOrWhiteSpace(S(peer["address"])) || peer["port"] is not JsonValue portValue || !portValue.TryGetValue<int>(out int port) || port is < 1 or > 65535 || !Guid.TryParse(S(user["id"]), out _)) throw new FormatException("В JSON VLESS неверный UUID, адрес или порт.");
        var stream = original["streamSettings"] as JsonObject ?? new JsonObject();
        string network = S(stream["network"]); if (network == "") network = "tcp";
        if (!Networks.Contains(network)) throw new FormatException("Этот транспорт не поддерживается установленным Xray-core. Устаревшие HTTP/2 и XTLS не преобразуются автоматически.");
        string security = S(stream["security"]); if (security == "") security = "none";
        if (security is not ("none" or "tls" or "reality")) throw new FormatException("Поддерживаются none, TLS и REALITY; устаревший XTLS не поддерживается.");
        if (network == "hysteria" && security != "tls") throw new FormatException("Транспорт Hysteria требует TLS.");
        if (security == "reality" && network is not ("tcp" or "raw" or "xhttp" or "splithttp" or "grpc")) throw new FormatException("REALITY совместим с TCP/RAW, XHTTP и gRPC.");
        if (security == "reality" && string.IsNullOrWhiteSpace(S(stream["realitySettings"]?["publicKey"])) && string.IsNullOrWhiteSpace(S(stream["realitySettings"]?["password"]))) throw new FormatException("Для REALITY нужен публичный ключ.");
        if (S(user["flow"]) is not ("" or "xtls-rprx-vision" or "xtls-rprx-vision-udp443")) throw new FormatException("Неизвестный flow VLESS.");
        if (S(user["flow"]) != "" && (network is not ("tcp" or "raw") || security == "none")) throw new FormatException("Vision требует TCP/RAW и TLS/REALITY.");
        CheckSafe(stream); CheckSafe(settings);
        return new JsonObject { ["tag"] = "proxy", ["protocol"] = "vless", ["settings"] = settings.DeepClone(), ["streamSettings"] = stream.DeepClone() };
    }
    public static void CheckSafe(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (new[] { "certificateFile", "keyFile", "keyLog", "masterKeyLog", "secretsLog", "dialerProxy", "proxySettings" }.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new FormatException("Настройки файлов, отладочной записи ключей и цепочек прокси не импортируются.");
                if (key.Equals("allowInsecure", StringComparison.OrdinalIgnoreCase) && value is JsonValue v && v.TryGetValue<bool>(out bool insecure) && insecure) throw new FormatException("Отключение проверки сертификата не поддерживается.");
                CheckSafe(value);
            }
        }
        else if (node is JsonArray array) foreach (var item in array) CheckSafe(item);
    }
    public static string Build(Profile profile, int socksPort) => new JsonObject
    {
        ["log"] = new JsonObject { ["loglevel"] = "none" },
        ["inbounds"] = new JsonArray(new JsonObject { ["tag"] = "local", ["listen"] = "127.0.0.1", ["port"] = socksPort, ["protocol"] = "socks", ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true } }),
        ["outbounds"] = new JsonArray(Outbound(profile.Source))
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
