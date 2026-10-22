using System.Text.Json.Nodes;

namespace OneTunnel.Core;

public sealed record TransportOptions
{
    public bool Fragment { get; init; }
    public string FragmentLength { get; init; } = "100-200";
    public string FragmentInterval { get; init; } = "10-20";
    public bool Noise { get; init; }
    public string NoiseLength { get; init; } = "50-150";
    public string NoiseDelay { get; init; } = "10-20";
    public bool Mux { get; init; }
    public int MuxConcurrency { get; init; } = 8;

    public void Validate(Profile profile)
    {
        static bool Range(string value, int min, int max)
        {
            var parts = (value ?? "").Split('-');
            if (parts.Length is < 1 or > 2) return false;
            int previous = min;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Any(c => c is < '0' or > '9') || !int.TryParse(part, out int n) || n < previous || n > max) return false;
                previous = n;
            }
            return true;
        }
        if (!Range(FragmentLength, 1, 1400) || !Range(FragmentInterval, 0, 100) || !Range(NoiseLength, 1, 1400) || !Range(NoiseDelay, 0, 100) || MuxConcurrency is < 1 or > 128)
            throw new FormatException("Размер: 1–1400 байт; задержка: 0–100 мс; MUX: 1–128 потоков. Диапазон записывается как 100-200.");
        if (!Fragment && !Noise && !Mux) return;
        if (profile.Protocol is not (Protocol.VLESS or Protocol.Hysteria2)) throw new FormatException("Эти настройки доступны для VLESS и Hysteria2. Для WG, AWG, SOCKS5 и Shadowsocks оставь их выключенными.");
        var outbound = ProxyCore.Outbound(profile.Source, profile.Protocol);
        var stream = outbound["streamSettings"];
        string network = stream?["network"]?.GetValue<string>() ?? "tcp";
        bool udp = profile.Protocol == Protocol.Hysteria2 || network is "kcp" or "mkcp" or "hysteria";
        if (Fragment && (profile.Protocol != Protocol.VLESS || udp || stream?["security"]?.GetValue<string>() is not ("tls" or "reality")))
            throw new FormatException("Фрагментация TLS требует VLESS с TLS/REALITY поверх TCP. Для UDP-транспортов используй шумы.");
        if (Fragment && stream?["xhttpSettings"]?["extra"]?["downloadSettings"] != null)
            throw new FormatException("Фрагментация раздельного XHTTP с downloadSettings пока не поддерживается.");
        if (Noise && !udp) throw new FormatException("Шумы применяются к UDP: Hysteria2 либо VLESS с транспортом KCP/Hysteria. Для TCP используй фрагментацию.");
        string flow = outbound["settings"]?["vnext"]?[0]?["users"]?[0]?["flow"]?.GetValue<string>() ?? "";
        if (Mux && (profile.Protocol != Protocol.VLESS || network is not ("tcp" or "raw" or "ws" or "websocket" or "httpupgrade") || flow.Length != 0))
            throw new FormatException("MUX доступен для VLESS TCP/WS/HTTPUpgrade без Vision. У XHTTP, gRPC и Hysteria собственное объединение потоков.");
    }
}
