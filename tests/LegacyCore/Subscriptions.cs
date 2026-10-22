using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneTunnel.Core;
public sealed record ImportResult(List<Profile> Profiles, int Duplicates, int Skipped)
{
    public string Summary => $"Профилей: {Profiles.Count}. Повторов: {Duplicates}. Неподдерживаемых записей: {Skipped}.";
}
public static class Subscriptions
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public static bool IsUrl(string source) => Uri.TryCreate(source.Trim(), UriKind.Absolute, out var u) && u.Scheme is "https" or "http";
    public static async Task<ImportResult> DownloadAsync(string source)
    {
        if (!Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo != "") throw new InvalidOperationException("Для подписки нужна HTTPS-ссылка без встроенного логина и пароля.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("v2rayN/7.0 OneTunnel/0.9.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain;q=0.9");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (response.Headers.Location is not { } location) throw new InvalidOperationException("Подписка вернула перенаправление без адреса.");
                    uri = new Uri(uri, location);
                    if (uri.Scheme != "https" || uri.UserInfo != "") throw new InvalidOperationException("Небезопасное перенаправление подписки отклонено.");
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Подписка недоступна (HTTP {(int)response.StatusCode}). Проверь срок действия ссылки.");
                if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidOperationException("Подписка больше 4 МБ.");
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream(); var buffer = new byte[16384];
                while (true) { int count = await input.ReadAsync(buffer, timeout.Token); if (count == 0) break; if (output.Length + count > MaxBytes) throw new InvalidOperationException("Подписка больше 4 МБ."); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); }
                return Parse(Encoding.UTF8.GetString(output.ToArray()));
            }
            throw new InvalidOperationException("Слишком много перенаправлений подписки.");
        }
        catch (OperationCanceledException) { throw new InvalidOperationException("Подписка не ответила за 30 секунд. Повтори загрузку."); }
        catch (HttpRequestException) { throw new InvalidOperationException("Не удалось загрузить подписку. Проверь сеть, адрес и сертификат сервера."); }
    }
    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Canonical(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonical).ToArray()),
        _ => node?.DeepClone()
    };
    public static string Identity(Profile profile) => ProxyCore.NeedsXray(profile.Protocol) ? Canonical(ProxyCore.Outbound(profile.Source, profile.Protocol))!.ToJsonString() : profile.Protocol == Protocol.SOCKS5 ? Canonical(ProfileParser.ProxyOutbound(profile.Source, profile.Protocol))!.ToJsonString() : profile.Source.Trim();
    public static ImportResult Parse(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > MaxBytes) throw new FormatException("Подписка больше 4 МБ.");
        payload = payload.Trim().TrimStart('\uFEFF');
        var result = new List<Profile>(); var seen = new HashSet<string>(StringComparer.Ordinal); int duplicates = 0, skipped = 0, candidates = 0;
        string Name(string value) => DisplayText.ImportedName(value);
        void Add(string name, string source, Protocol? protocol = null)
        {
            if (++candidates > 2000) throw new FormatException("В подписке больше 2000 записей.");
            try { var p = ProfileParser.Parse(Name(name), source, protocol); if (seen.Add(Identity(p))) result.Add(p); else duplicates++; }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException) { skipped++; }
        }
        void Config(JsonNode? node)
        {
            if (node is not JsonObject obj) { skipped++; return; }
            if (obj["protocol"]?.GetValue<string>() is "vless" or "shadowsocks" or "hysteria") { Add("", obj.ToJsonString()); return; }
            if (obj["outbounds"] is not JsonArray outbounds) { skipped++; return; }
            string name = obj["remarks"]?.GetValue<string>() ?? "";
            var endpoints = outbounds.OfType<JsonObject>().Where(o => o["protocol"]?.GetValue<string>() is "vless" or "shadowsocks" or "hysteria").ToList();
            foreach (var endpoint in endpoints)
            {
                string title = endpoints.Count > 1 ? name + " · " + (endpoint["tag"]?.GetValue<string>() ?? "VLESS") : name;
                Add(title, endpoint.ToJsonString());
            }
            skipped += outbounds.OfType<JsonObject>().Count(o => o["protocol"]?.GetValue<string>() is not ("vless" or "hysteria" or "shadowsocks" or "freedom" or "blackhole" or "dns"));
        }
        if (!payload.StartsWith('{') && !payload.StartsWith('[') && !payload.Contains("://"))
        {
            try { string encoded = string.Concat(payload.Where(c => !char.IsWhiteSpace(c))).Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '='); payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Trim().TrimStart('\uFEFF'); }
            catch (FormatException) { throw new FormatException("Неизвестный формат подписки. Поддерживаются JSON Xray, список ссылок и Base64."); }
        }
        if (payload.StartsWith('{') || payload.StartsWith('['))
        {
            try { var json = JsonNode.Parse(payload, documentOptions: new JsonDocumentOptions { MaxDepth = 48 }); if (json is JsonArray array) foreach (var item in array) Config(item); else Config(json); }
            catch (JsonException) { throw new FormatException("Подписка содержит неверный JSON."); }
            catch (InvalidOperationException) { throw new FormatException("Неверная структура JSON-подписки."); }
        }
        else foreach (var line in payload.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#')) continue;
            if (new[] { "vless://", "socks5://", "ss://", "hy2://", "hysteria2://" }.Any(s => line.StartsWith(s, StringComparison.OrdinalIgnoreCase))) Add("", line); else skipped++;
        }
        if (result.Count == 0) throw new FormatException("В подписке не найдено совместимых профилей. Нужны JSON Xray, список ссылок или Base64.");
        return new(result, duplicates, skipped);
    }
}
