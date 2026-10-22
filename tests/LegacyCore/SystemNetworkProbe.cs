using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OneTunnel.Core;

public static class SystemNetworkProbe
{
    [DllImport("iphlpapi.dll")] private static extern uint GetBestInterfaceEx(byte[] destination, out uint index);
    [DllImport("dnsapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DnsFlushResolverCache();
    internal static void FlushDnsCache() => DnsFlushResolverCache();
    public static string RouteInterface(IPAddress address)
    {
        bool ipv6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        var socketAddress = new byte[ipv6 ? 28 : 16];
        socketAddress[0] = (byte)(ipv6 ? 23 : 2);
        address.GetAddressBytes().CopyTo(socketAddress, ipv6 ? 8 : 4);
        if (GetBestInterfaceEx(socketAddress, out uint index) != 0) return "No route";
        return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(adapter =>
        {
            try { return (ipv6 ? adapter.GetIPProperties().GetIPv6Properties()?.Index : adapter.GetIPProperties().GetIPv4Properties()?.Index) == index; }
            catch (NetworkInformationException) { return false; }
        })?.Name ?? "Unknown interface";
    }
    public static async Task<string> RunAsync(bool requireTunRoutes = false, bool allowNativeIPv6Block = false)
    {
        if (requireTunRoutes && (!RouteInterface(IPAddress.Parse("1.1.1.1")).StartsWith("OneTunnel", StringComparison.Ordinal) || !allowNativeIPv6Block && !RouteInterface(IPAddress.Parse("2606:4700:4700::1111")).StartsWith("OneTunnel", StringComparison.Ordinal)))
            throw new InvalidOperationException("Маршруты всего устройства не установлены для IPv4/IPv6. Подключение не прошло системную проверку.");
        // Never use the loopback proxy for a system-mode check.
        using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await http.GetAsync("https://example.com/?onetunnel=" + Guid.NewGuid().ToString("N"));
        if (!response.IsSuccessStatusCode || !(await response.Content.ReadAsStringAsync()).Contains("Example Domain", StringComparison.Ordinal))
            throw new InvalidOperationException("Системный HTTPS-запрос не прошёл. Проверь маршруты и DNS туннеля.");
        string ipv4 = await FamilyAsync(AddressFamily.InterNetwork);
        string ipv6 = await FamilyAsync(AddressFamily.InterNetworkV6);
        return $"Системный HTTPS и разрешение имени работают. IPv4: {ipv4}. IPv6: {ipv6}. Отсутствие утечек DNS требует отдельного захвата трафика; этот тест его не подтверждает.";
    }
    internal static async Task<string> FamilyAsync(AddressFamily family)
    {
        try
        {
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            handler.ConnectCallback = async (_, cancellation) =>
            {
                var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    var address = IPAddress.Parse(family == AddressFamily.InterNetwork ? "1.1.1.1" : "2606:4700:4700::1111");
                    await socket.ConnectAsync(new IPEndPoint(address, 443), cancellation);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
            using var response = await http.GetAsync("https://one.one.one.one/cdn-cgi/trace");
            return response.IsSuccessStatusCode ? "HTTPS доступен" : "сервер проверки не ответил";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException) { return "недоступен (блокировка или отсутствие поддержки — утечка не проверена)"; }
    }
}
