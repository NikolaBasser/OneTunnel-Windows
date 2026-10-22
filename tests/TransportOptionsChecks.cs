using OneTunnel.Core;

internal static class TransportOptionsChecks
{
    internal static void Run(Action<bool, string> assert)
    {
        const string link = "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:443?security=tls&sni=localhost";
        var vless = ProfileParser.Parse("Fixture", link);
        new TransportOptions { Fragment = true, Mux = true }.Validate(vless);
        assert(true, "fragmentation and MUX accepted for TLS VLESS without Vision");
        void Reject(TransportOptions options, Profile profile, string label)
        {
            try { options.Validate(profile); throw new Exception("Invalid transport options accepted"); }
            catch (FormatException) { assert(true, label); }
        }
        Reject(new() { Mux = true }, ProfileParser.Parse("Vision", link + "&flow=xtls-rprx-vision"), "MUX rejects Vision before connection");
        Reject(new() { Mux = true }, ProfileParser.Parse("XHTTP", link + "&type=xhttp"), "external MUX rejects XHTTP before connection");
        Reject(new() { Noise = true }, vless, "UDP noise rejects TCP transport");
        Reject(new() { Fragment = true }, ProfileParser.Parse("Hy2", "hy2://fixture@127.0.0.1:443?sni=localhost"), "TLS fragmentation rejects UDP transport");
        Reject(new() { MuxConcurrency = 129 }, vless, "bounded MUX concurrency");
        Reject(new() { FragmentLength = "200-100" }, vless, "inverted fragment range rejected");
        Reject(new() { NoiseLength = "1-9999999" }, vless, "unbounded noise allocation rejected");
    }
}
