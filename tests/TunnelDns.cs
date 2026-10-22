using System.Net;

namespace OneTunnel.Core;

public sealed record TunnelDns(string Address, string ServerName)
{
    public static TunnelDns[] Candidates { get; } = [new("1.1.1.1", "cloudflare-dns.com"), new("8.8.8.8", "dns.google"), new("9.9.9.9", "dns.quad9.net")];
    public static async Task<bool> CheckAsync(TunnelDns dns, Uri proxy)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy(proxy), AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            byte[] query = [0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 7, 101, 120, 97, 109, 112, 108, 101, 3, 99, 111, 109, 0, 0, 1, 0, 1];
            string encoded = Convert.ToBase64String(query).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{dns.Address}/dns-query?dns={encoded}");
            request.Headers.Host = dns.ServerName;
            request.Headers.Accept.ParseAdd("application/dns-message");
            using var response = await http.SendAsync(request);
            byte[] body = await response.Content.ReadAsByteArrayAsync();
            return response.IsSuccessStatusCode && body.Length >= 12 && (body[2] & 0x80) != 0 && (body[3] & 15) == 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
    public static async Task<TunnelDns> SelectAsync(Uri proxy)
    {
        foreach (var candidate in Candidates) if (await CheckAsync(candidate, proxy)) return candidate;
        throw new InvalidOperationException("Через выбранный сервер не отвечает защищённый DNS. Системный туннель не запущен; попробуй другой сервер.");
    }
}
