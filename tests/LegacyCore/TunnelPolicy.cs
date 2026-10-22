using System.Text.RegularExpressions;

namespace OneTunnel.Core;

public static class TunnelPolicy
{
    public static Profile ForFullDevice(Profile profile)
    {
        if (profile.Protocol is not (Protocol.WireGuard or Protocol.AmneziaWG)) return profile;
        // Full-device is an explicit mode. Expand routes only in the temporary
        // service configuration; never rewrite the user's saved split profile.
        string address = Regex.Match(profile.Source, @"(?mi)^\s*Address\s*=\s*([^\r\n]+)").Groups[1].Value;
        string routes = address.Contains(':') ? "0.0.0.0/0, ::/0" : "0.0.0.0/0";
        string source = Regex.Replace(profile.Source, @"(?mi)^([ \t]*AllowedIPs[ \t]*=)[^\r\n]+", match => match.Groups[1].Value + " " + routes);
        var prepared = profile with { Source = source };
        RequireFullDevice(prepared);
        return prepared;
    }
    public static void RequireFullDevice(Profile profile)
    {
        if (profile.Protocol is not (Protocol.WireGuard or Protocol.AmneziaWG)) return;
        string source = string.Join('\n', profile.Source.Split('\n').Select(line => line.Split('#')[0]));
        var routes = Regex.Match(source, @"(?mi)^\s*AllowedIPs\s*=\s*(.+)$").Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
        bool hasIPv6 = Regex.Match(source, @"(?mi)^\s*Address\s*=\s*(.+)$").Groups[1].Value.Contains(':');
        if (!routes.Contains("0.0.0.0/0") || hasIPv6 && !routes.Contains("::/0"))
            throw new InvalidOperationException("Полные маршруты WG/AWG не настроены. Для IPv4 нужен 0.0.0.0/0, для настроенного IPv6 — ::/0.");
        // With one peer and an exact /0 route the native Windows WG/AWG engine
        // blocks all non-tunnel traffic, including IPv6 when it is unconfigured.
        if (!Regex.IsMatch(source, @"(?mi)^\s*DNS\s*=\s*\S+"))
            throw new InvalidOperationException("Для режима «Всё устройство» укажи DNS в WG/AWG-профиле, чтобы не использовать DNS провайдера Windows.");
    }
}
