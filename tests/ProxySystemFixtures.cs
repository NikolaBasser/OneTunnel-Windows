using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using OneTunnel.Core;

// Local protocol server -> local SOCKS fixture -> bound physical uplink. This
// validates TUN/fault behavior without presenting an offline provider as working.
internal sealed class ProxySystemFixtures(Profile socks) : IDisposable
{
    private readonly ProcessJob job = new();
    private readonly List<Process> servers = [];
    public HashSet<Guid> ProfileIds { get; } = [];
    public async Task<Profile> Create(Protocol protocol, string transport)
    {
        int port; using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; }
        string uuid = Guid.NewGuid().ToString(), password = Guid.NewGuid().ToString("N");
        Profile profile;
        JsonObject inbound;
        if (protocol == Protocol.VLESS)
        {
            profile = ProfileParser.Parse("Local system fixture", $"vless://{uuid}@127.0.0.1:{port}?encryption=none&type={transport}" + (transport == "grpc" ? "&serviceName=fixture" : transport is "ws" or "xhttp" or "httpupgrade" ? "&path=%2Ffixture" : ""));
            inbound = new JsonObject { ["protocol"] = "vless", ["settings"] = new JsonObject { ["decryption"] = "none", ["clients"] = new JsonArray(new JsonObject { ["id"] = uuid }) }, ["streamSettings"] = VlessConfig.Outbound(profile.Source)["streamSettings"]!.DeepClone() };
        }
        else if (protocol == Protocol.Shadowsocks)
        {
            const string method = "aes-128-gcm";
            profile = ProfileParser.Parse("Local system fixture", "ss://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(method + ":" + password)) + $"@127.0.0.1:{port}");
            inbound = new JsonObject { ["protocol"] = "shadowsocks", ["settings"] = new JsonObject { ["method"] = method, ["password"] = password, ["network"] = "tcp,udp" } };
        }
        else if (protocol == Protocol.Hysteria2)
        {
            profile = ProfileParser.Parse("Local system fixture", $"hy2://{password}@127.0.0.1:{port}?sni=localhost");
            var outbound = ProxyCore.Outbound(profile.Source, protocol);
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            JsonArray Lines(string text) => new(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => (JsonNode?)JsonValue.Create(s.Trim())).ToArray());
            var stream = (JsonObject)outbound["streamSettings"]!.DeepClone();
            stream["tlsSettings"] = new JsonObject { ["alpn"] = new JsonArray("h3"), ["certificates"] = new JsonArray(new JsonObject { ["certificate"] = Lines(certificate.ExportCertificatePem()), ["key"] = Lines(rsa.ExportPkcs8PrivateKeyPem()) }) };
            outbound["streamSettings"]!["tlsSettings"]!["pinnedPeerCertSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData));
            profile = ProfileParser.Parse("Local system fixture", outbound.ToJsonString());
            inbound = new JsonObject { ["protocol"] = "hysteria", ["settings"] = new JsonObject { ["version"] = 2, ["clients"] = new JsonArray(new JsonObject { ["auth"] = password }) }, ["streamSettings"] = stream };
        }
        else throw new InvalidOperationException("No fixture for this protocol.");
        inbound["listen"] = "127.0.0.1"; inbound["port"] = port;
        var upstream = new Uri(socks.Source);
        var config = new JsonObject { ["log"] = new JsonObject { ["loglevel"] = "none" }, ["inbounds"] = new JsonArray(inbound), ["outbounds"] = new JsonArray(new JsonObject { ["protocol"] = "socks", ["settings"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["address"] = "127.0.0.1", ["port"] = upstream.Port }) } }) };
        string file = Path.Combine(Storage.Runtime, "fixture-" + profile.Id.ToString("N") + ".json"); File.WriteAllText(file, config.ToJsonString());
        string engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OneTunnel", "Core", "engines", "xray.exe");
        if (await Connection.RunAsync(engine, ["run", "-test", "-config", file]) != 0) throw new Exception("Local protocol fixture rejected.");
        var process = Process.Start(Connection.Info(engine, ["run", "-config", file]))!; job.Add(process); servers.Add(process);
        _ = process.StandardOutput.ReadToEndAsync(); _ = process.StandardError.ReadToEndAsync();
        await Task.Delay(500); if (process.HasExited) throw new Exception("Local protocol fixture stopped.");
        ProfileIds.Add(profile.Id); return profile;
    }
    public void Dispose() { job.Dispose(); foreach (var server in servers) server.Dispose(); }
}
