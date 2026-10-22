using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using OneTunnel.Core;

internal sealed record Ipv6KillCanary(IPAddress Source, IPEndPoint Target, int InterfaceIndex)
{
    public static async Task<Ipv6KillCanary?> Find(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var source = properties.UnicastAddresses.FirstOrDefault(a => a.Address.IsIPv6LinkLocal)?.Address;
        var gateway = properties.GatewayAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
        if (source == null || gateway == null) return null;
        string arp = await Output("arp.exe", ["-a", gateway.ToString()]);
        var match = Regex.Match(arp, @"(?mi)^\s*" + Regex.Escape(gateway.ToString()) + @"\s+([0-9a-f-]{17})\s");
        if (!match.Success) return null;
        int index = properties.GetIPv6Properties()!.Index;
        string neighbors = await Output("netsh.exe", ["interface", "ipv6", "show", "neighbors", "interface=" + index]);
        string? line = neighbors.Split('\n').FirstOrDefault(l => l.Contains(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        IPAddress peer;
        var address = Regex.Match(line ?? "", @"(?i)fe80:[0-9a-f:]+");
        if (address.Success) peer = IPAddress.Parse(address.Value);
        else
        {
            // A single probe to the IPv4 gateway's conventional EUI-64 link-local
            // address; no subnet scan and no dependency on global IPv6 service.
            byte[] mac = Convert.FromHexString(match.Groups[1].Value.Replace("-", ""));
            byte[] bytes = [0xfe, 0x80, 0, 0, 0, 0, 0, 0, (byte)(mac[0] ^ 2), mac[1], mac[2], 0xff, 0xfe, mac[3], mac[4], mac[5]];
            peer = new IPAddress(bytes);
        }
        peer.ScopeId = index;
        return new(source, new IPEndPoint(peer, 80), index);
    }
    public async Task<(string Status, int Port)> Probe()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)31, InterfaceIndex);
        socket.Bind(new IPEndPoint(Source, 0)); int port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        try { await socket.ConnectAsync(Target).WaitAsync(TimeSpan.FromSeconds(2)); return ("Reachable", port); }
        catch (SocketException ex) { return (ex.SocketErrorCode.ToString(), port); }
        catch (TimeoutException) { return ("Timeout", port); }
    }
    private static async Task<string> Output(string command, string[] arguments)
    {
        using var process = Process.Start(Connection.Info(Path.Combine(Environment.SystemDirectory, command), arguments))!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await error; return await output;
    }
}
