using OneTunnel.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

try
{
if (args.Contains("--verify-auto-subscription")) {
    Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OneTunnel"));
    Environment.SetEnvironmentVariable("ONETUNNEL_VERIFY_AUTO_REFRESH","1");
    await SubscriptionEndpointDiagnostics.VerifySaved(); return;
}
if (args.Contains("--apply-fresh-subscription")) {
    Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OneTunnel"));
    await SubscriptionEndpointDiagnostics.ApplyFresh(); return;
}
if (args.Contains("--service-safety-watch")) {
    try { await ServiceSafetyWatch.Run(args[^3], int.Parse(args[^2]), long.Parse(args[^1])); }
    catch (Exception ex) { File.WriteAllText(Path.Combine(args[^3], "watch-error"), ex.GetType().Name + ": " + ex.Message); throw; }
    return;
}
if (args.Contains("--go-migration-compatibility")) { await GoMigrationCompatibility.Run(); return; }
var data = (args.Contains("--system-mode") || args.Contains("--system-watch") || args.Contains("--system-recover") || args.Contains("--kill-switch-check") || args.Contains("--kill-switch-smoke") || args.Contains("--kill-switch-worker")) && Environment.GetEnvironmentVariable("ONETUNNEL_DATA_DIR") is { Length: > 0 } configuredRoot ? configuredRoot : Path.Combine(Path.GetTempPath(), "OneTunnel-Tests-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR", data);
Storage.Initialize();
if (args.Contains("--export-protocol-fixtures")) { ProtocolParity.Export(); return; }
if (args.Contains("--export-go-profile-fixtures")) { GoProfileFixtures.Export(args[^1]); return; }
if (args.Contains("--verify-go-profile-fixtures")) { GoProfileFixtures.Verify(args[^1]); return; }
bool serviceLocal = args.Contains("--service-local-check")||args.Contains("--go-service-local-check");
if (serviceLocal)
{
    var state = new OneTunnel.Client.ServiceConnection(); await state.RefreshAsync();
    if (!state.Available || state.Active || state.NeedsRecovery) throw new Exception("Idle service required for local Go traffic fixtures.");
}
IConnectionController TestController(Connection? direct = null) => serviceLocal ? args.Contains("--go-service-local-check") ? new GoImportedController(new OneTunnel.Client.ServiceConnection()) : new OneTunnel.Client.ServiceConnection() : new DirectTestController(direct ?? new Connection());
if (args.Contains("--service-user-check")) { await ServiceChecks.UserCheck(); return; }
if (args.Contains("--service-probe-check")) { await ServiceChecks.Diagnose(); return; }
if (args.Contains("--compare-remote-cores")) { await RemoteCoreComparison.Run(); return; }
if (args.Contains("--endpoint-diagnostics")) { await SubscriptionEndpointDiagnostics.Run(); return; }
if (args.Contains("--fresh-subscription-diagnostics")) { await SubscriptionEndpointDiagnostics.Fresh(); return; }
if (args.Contains("--probe-fresh-subscription")) { await SubscriptionEndpointDiagnostics.ProbeFresh(); return; }
if (args.Contains("--verify-refreshed-profiles")) { await SubscriptionEndpointDiagnostics.VerifySaved(); return; }
if (args.Contains("--proxy-health-diagnostics")) { await SubscriptionEndpointDiagnostics.Health(); return; }
if (args.Contains("--route-ab-diagnostics")) { await SubscriptionEndpointDiagnostics.RouteAB(); return; }
if (args.Contains("--service-check")) { await ServiceChecks.Run(); return; }
if (args.Contains("--kill-switch-worker")) { await KillSwitchChecks.Worker(args[^2], args[^1]); return; }
if (args.Contains("--kill-switch-check") || args.Contains("--kill-switch-smoke")) { await KillSwitchChecks.Run(args.Contains("--kill-switch-smoke")); return; }
if (args.Contains("--tcp-profile-check")) { await DnsProviderChecks.Run("tcp"); return; }
if (args.Contains("--dns-profile-check")) { await DnsProviderChecks.Run(); return; }
if (args.Contains("--system-watch")) { await SystemModeChecks.Watch(int.Parse(args[^1])); return; }
if (args.Contains("--system-recover")) { if (Connection.NeedsRecovery) await new Connection().RecoverAsync(); return; }
if (args.Contains("--system-mode")) { await SystemModeChecks.Run(); return; }
if (args.Contains("--diagnose-providers")) { await ProviderDiagnostics.Run(args.Contains("--provider-ipv4"), args.Contains("--provider-doh"), args.Contains("--provider-ntp")); return; }
if (args.Contains("--verify-requested-providers")) { await ProviderDiagnostics.VerifyApplication(); return; }
if (args.Contains("--live-subscription"))
{
    var url = Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL") ?? throw new Exception("Subscription URL environment variable required");
    var batch = await Subscriptions.DownloadAsync(url);
    Console.WriteLine(batch.Summary);
    int valid = 0, invalid = 0;
    foreach (var p in batch.Profiles)
    {
        var file = Path.Combine(Storage.Runtime, "validation.xray.json");
        try { File.WriteAllText(file, ProxyCore.Build(p, 19767)); if (await Connection.RunAsync(Connection.XrayPath, ["run", "-test", "-config", file]) == 0) valid++; else invalid++; }
        finally { File.Delete(file); }
    }
    Console.WriteLine($"Native Xray config validation: valid={valid}, invalid={invalid}");
    if (invalid > 0) throw new Exception("Some subscription configurations were rejected by Xray");
    foreach (var group in batch.Profiles.GroupBy(p => p.Protocol + "/" + (ProxyCore.Outbound(p.Source, p.Protocol)["streamSettings"]?["network"]?.GetValue<string>() ?? "tcp")))
    {
        if (!args.Contains("--probe-servers")) continue;
        bool ok = false; int tried = 0;
        foreach (var p in group.Take(3))
        {
            var c = new Connection(); tried++;
            try { await c.StartAsync(p, false); var message = await c.ProbeAsync(); ok = message.Contains("HTTPS-запрос через локальный прокси выполнен"); if (ok) break; }
            catch (Exception ex) { Console.WriteLine("Probe failure kind: " + ex.GetType().Name + (ex is InvalidOperationException ? ": " + ex.Message : "")); }
            finally { await c.StopAsync(); }
        }
        Console.WriteLine($"Live probe: transport={group.Key}, attempts={tried}, verified={ok}");
    }
    return;
}
int checks = 0;
void Assert(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
SubscriptionMergeChecks.Run(Assert);
TransportOptionsChecks.Run(Assert);
void Reject(string value, string name, Protocol? type = null) { try { ProfileParser.Parse("test", value, type); throw new Exception("Accepted invalid input: " + name); } catch (FormatException) { Assert(true, name); } }
using (var frame = new MemoryStream())
{
    await CoreWire.WriteAsync(frame, new CoreRequest(1, "status"), CancellationToken.None); frame.Position = 0;
    Assert((await CoreWire.ReadAsync<CoreRequest>(frame, CancellationToken.None)).Command == "status", "bounded IPC frame round trip");
}
foreach (int length in new[] { -1, CoreProtocol.MaxFrameBytes + 1 })
{
    using var malformed = new MemoryStream(BitConverter.GetBytes(length));
    try { await CoreWire.ReadAsync<CoreRequest>(malformed, CancellationToken.None); throw new Exception("Accepted invalid frame"); }
    catch (InvalidDataException) { Assert(true, "reject invalid IPC length before allocation"); }
}
using (var unknown = new MemoryStream())
{
    var bytes = Encoding.UTF8.GetBytes("{\"Version\":1,\"Command\":\"status\",\"Executable\":\"bad.exe\"}");
    unknown.Write(BitConverter.GetBytes(bytes.Length)); unknown.Write(bytes); unknown.Position = 0;
    try { await CoreWire.ReadAsync<CoreRequest>(unknown, CancellationToken.None); throw new Exception("Accepted unknown command field"); }
    catch (System.Text.Json.JsonException) { Assert(true, "reject executable field in IPC schema"); }
}
string key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
string conf = $"[Interface]\nPrivateKey = {key}\nAddress = 10.9.0.2/32\nDNS = 1.1.1.1\n[Peer]\nPublicKey = {key}\nEndpoint = 127.0.0.1:51820\nAllowedIPs = 0.0.0.0/0, ::/0\n";
var wg = ProfileParser.Parse("WG", conf); Assert(wg.Protocol == Protocol.WireGuard, "WG import");
Assert(Connection.EnginePath(Protocol.WireGuard)==Connection.EnginePath(Protocol.AmneziaWG), "WG and AWG share AmneziaWG engine without changing profile type");
var awg = ProfileParser.Parse("AWG", conf.Replace("[Peer]", "Jc = 3\nJmin = 40\nJmax = 70\nH1 = 12-34\nI1 = <b 0x40>\n[Peer]")); Assert(awg.Protocol == Protocol.AmneziaWG, "AWG extended parameters import");
Reject(conf.Replace("DNS = 1.1.1.1", "PostUp = calc.exe"), "reject executable hooks");
Reject(conf.Replace("10.9.0.2/32", "10.9.0.2/99"), "reject invalid subnet");
Reject(conf.Replace(key, "secret"), "reject invalid tunnel keys");
Reject(conf + "[Peer]\nPublicKey = " + key, "reject multiple peers");
Reject(awg.Source, "reject AWG interpreted as WG", Protocol.WireGuard);
Reject("vless://not-a-uuid@example.com:443", "reject VLESS UUID");
Assert(ProfileParser.Parse("XHTTP", "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@example.com:443?type=xhttp").Protocol == Protocol.VLESS, "accept XHTTP VLESS links");
Reject("socks5://localhost:0", "reject zero port");
Reject("socks5://localhost:1080?tls=true", "reject ignored SOCKS options");
Reject("vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@example.com:443?security=reality", "reject REALITY without public key");
Reject("vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@example.com:443?type=tcp&type=ws", "reject duplicate options");
var socks = ProfileParser.Parse("SOCKS", "socks5://user:p%40ss@127.0.0.1:1080");
var outbound = ProfileParser.ProxyOutbound(socks.Source, socks.Protocol);
Assert(outbound["password"]!.GetValue<string>() == "p@ss", "decode SOCKS credentials");
var ipv6 = ProfileParser.Parse("IPv6", "socks5://[::1]:1080"); Assert(ProfileParser.ProxyOutbound(ipv6.Source, ipv6.Protocol)["server"]!.GetValue<string>() == "::1", "IPv6 endpoint");
Storage.Save([wg, awg, socks]); var loaded = Storage.Load();
Assert(loaded.Count == 3 && loaded[2].Source == socks.Source, "DPAPI storage roundtrip");
Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Storage.Root, "profiles.bin"))).Contains(key), "keys absent from encrypted file");
// Compatibility: migrate the encrypted 0.3 array without losing ids or secrets.
File.WriteAllBytes(Path.Combine(Storage.Root, "profiles.bin"), Storage.Protect(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { wg }), true));
Assert(Storage.Load().Single().Id == wg.Id && Storage.Load().Single().Group == "Без группы", "legacy profiles migrate into default group");
Storage.CollapsedGroups.Add("🇩🇪 Подписка"); Storage.Save([wg with { Group = "🇩🇪 Подписка" }]);
Assert(Storage.Load().Single().Group == "🇩🇪 Подписка" && Storage.CollapsedGroups.Contains("🇩🇪 Подписка"), "encrypted groups and collapse state roundtrip");
var full = wg with { Source = wg.Source };
TunnelPolicy.RequireFullDevice(full); Assert(true, "full WG policy accepts both address families and DNS");
try { TunnelPolicy.RequireFullDevice(wg with { Source = wg.Source.Replace("0.0.0.0/0, ::/0", "10.9.0.1/32") }); throw new Exception("split full mode accepted"); } catch (InvalidOperationException) { Assert(true, "full WG policy refuses split routes"); }
try { TunnelPolicy.RequireFullDevice(full with { Source = full.Source.Replace("DNS = 1.1.1.1", "") }); throw new Exception("missing DNS accepted"); } catch (InvalidOperationException) { Assert(true, "full WG mode refuses missing DNS"); }
var splitNative = wg with { Source = wg.Source.Replace("0.0.0.0/0, ::/0", "10.9.0.1/32") };
var preparedNative = TunnelPolicy.ForFullDevice(splitNative);
Assert(preparedNative.Source.Contains("AllowedIPs = 0.0.0.0/0") && splitNative.Source.Contains("AllowedIPs = 10.9.0.1/32") && preparedNative.Id == splitNative.Id, "full native mode expands only the temporary routes and enables native firewall");
var amneziaJson = new JsonObject { ["description"] = "AWG share", ["dns1"] = "1.1.1.1", ["containers"] = new JsonArray(new JsonObject { ["container"] = "amnezia-awg", ["awg"] = new JsonObject { ["last_config"] = new JsonObject { ["config"] = awg.Source.Replace("DNS = 1.1.1.1", "DNS = $PRIMARY_DNS, $SECONDARY_DNS") }.ToJsonString() } }) }.ToJsonString();
var amneziaBytes = Encoding.UTF8.GetBytes(amneziaJson);
using (var memory = new MemoryStream())
{
    var prefix = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)amneziaBytes.Length); memory.Write(prefix);
    using (var zlib = new System.IO.Compression.ZLibStream(memory, System.IO.Compression.CompressionLevel.SmallestSize, true)) zlib.Write(amneziaBytes);
    string share = "vpn://" + Convert.ToBase64String(memory.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var shared = ProfileParser.Parse("", share);
    Assert(shared.Protocol == Protocol.AmneziaWG && shared.Source == awg.Source, "compressed Amnezia share auto-detection");
}
var safetyConfig = JsonNode.Parse(ProfileParser.BuildProxyConfig(socks, 18734, true, 18735))!;
Assert(safetyConfig["inbounds"]![1]!["dns_mode"]!.GetValue<string>() == "hijack" && safetyConfig["inbounds"]![1]!["route_address"]!.AsArray().Count == 4, "TUN explicitly captures DNS and both address families");
Assert(safetyConfig["route"]!["rules"]![0]!["process_path"]![0]!.GetValue<string>() == Connection.XrayPath, "TUN bypass restricted to bundled Xray path");
var bootstrapProfile = ProfileParser.Parse("bootstrap regression", "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@bootstrap.invalid:443?type=xhttp&security=tls&sni=front.invalid");
var bootstrapConfig = JsonNode.Parse(ProxyCore.Build(bootstrapProfile, 18736))!;
await Connection.PrepareTunEndpoint(bootstrapConfig, _ => Task.FromResult(new[] { IPAddress.Parse("192.0.2.10") }));
Assert(bootstrapConfig["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!["host"]!.GetValue<string>() == "front.invalid" && bootstrapConfig["outbounds"]![0]!["settings"]!["vnext"]![0]!["address"]!.GetValue<string>() == "192.0.2.10", "XHTTP bootstrap preserves TLS-derived HTTP host while pinning endpoint IP");
Connection.BindTunUplink(bootstrapConfig, _ => "Regression uplink");
Assert(bootstrapConfig["outbounds"]![0]!["streamSettings"]!["sockopt"]!["interface"]!.GetValue<string>() == "Regression uplink", "Xray TUN sockets bind to the selected external adapter");
var lockMarker = Path.Combine(Storage.Root, "kill-switch.json");
File.WriteAllText(lockMarker, "{}");
Assert(Connection.NeedsRecovery, "persistent kill-switch marker requires recovery even without session journal");
try { await new Connection().StartAsync(socks, false); throw new Exception("Persistent lock was ignored"); } catch (InvalidOperationException) { Assert(File.Exists(lockMarker), "new connection cannot silently clear persistent traffic lock"); }
File.Delete(lockMarker);
byte[] protectedBytes = Storage.Protect(Encoding.UTF8.GetBytes("key material"), true); protectedBytes[^1] ^= 1;
try { Storage.Protect(protectedBytes, false); throw new Exception("Tampered DPAPI accepted"); } catch (System.ComponentModel.Win32Exception) { Assert(true, "reject altered DPAPI data"); }
string engine = Connection.EnginePath(Protocol.VLESS);
async Task CheckConfig(Profile p, bool tun, string label)
{
    var file = Path.Combine(data, "check.json"); File.WriteAllText(file, ProfileParser.BuildProxyConfig(p, 18734, tun));
    using var process = Process.Start(Connection.Info(engine, ["check", "-c", file]))!;
    var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync(); await output;
    if (process.ExitCode != 0) throw new Exception(label + ": " + await error);
    Assert(true, label);
}
var vless = ProfileParser.Parse("VLESS", "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:443?encryption=none&type=tcp");
await CheckConfig(socks, false, "engine validates SOCKS5 proxy config");
await CheckConfig(socks, true, "engine validates SOCKS5 TUN/DNS/IPv6 config");
await CheckConfig(socks with { Source = socks.Source.Replace("127.0.0.1", "example.com") }, true, "engine validates hostname-based TUN endpoint");
await CheckConfig(vless, false, "engine validates VLESS TCP config");
await CheckConfig(vless with { Source = vless.Source + "&security=tls&sni=example.com&fp=chrome" }, true, "engine validates VLESS TLS TUN config");
await CheckConfig(vless with { Source = vless.Source.Replace("type=tcp", "type=ws&path=%2Fvpn&host=example.com") + "&security=tls" }, false, "engine validates VLESS WS TLS config");
await CheckConfig(vless with { Source = vless.Source.Replace("type=tcp", "type=grpc&serviceName=tunnel") + "&security=tls" }, false, "engine validates VLESS gRPC TLS config");
await CheckConfig(vless with { Source = vless.Source + "&security=reality&flow=xtls-rprx-vision&sni=example.com&fp=chrome&pbk=ElBaNyq6eNlwdkAKIzcX1Bf0IuDAnKhT7Fo8WM-PeVc&sid=abcd" }, false, "engine validates VLESS REALITY Vision config");
await CheckConfig(vless, true, "legacy configuration validation");
string sample = "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:24443?encryption=none";
Assert(ProfileParser.Parse(new string('a', 99) + "🇷🇺", sample).Name.EndsWith("🇷🇺"), "name limit counts a country flag as one grapheme");
Reject(new string('a', 100) + "🇷🇺", "name exceeds 100 graphemes", Protocol.VLESS);
Assert(DisplayText.ImportedName(new string('a',99) + "👨‍👩‍👧‍👦extra").EndsWith("👨‍👩‍👧‍👦"), "import truncation preserves full ZWJ family emoji");
Assert(DisplayText.IsWellFormed(DisplayText.ImportedName(new string('a',99) + "🇩🇪")), "import never truncates a surrogate pair");
Assert(DisplayText.EmojiKey("❤️") == "2764" && DisplayText.EmojiKey("🇷🇺") == "1f1f7-1f1fa", "Twemoji variation and regional-indicator keys");
var batchPlain = Subscriptions.Parse(sample + "#first\n" + sample + "#alias\nsocks5://127.0.0.1:1080\ntrojan://unsupported");
Assert(batchPlain.Profiles.Count == 2 && batchPlain.Duplicates == 1 && batchPlain.Skipped == 1, "subscription text deduplicates endpoints and reports unsupported protocols");
var batch64 = Subscriptions.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(sample)));
Assert(batch64.Profiles.Count == 1, "Base64 subscription import");
var xhttpLink = ProfileParser.Parse("XHTTP", sample + "&type=xhttp&mode=packet-up&path=%2Ftest&extra=" + Uri.EscapeDataString("{\"noGRPCHeader\":true}"));
var xhttpOutbound = VlessConfig.Outbound(xhttpLink.Source);
Assert(xhttpOutbound["streamSettings"]!["xhttpSettings"]!["extra"]!["noGRPCHeader"]!.GetValue<bool>(), "XHTTP URI extra preserved");
var jsonSubscription = new JsonArray(new JsonObject { ["remarks"] = "Test config", ["outbounds"] = new JsonArray(xhttpOutbound.DeepClone(), new JsonObject { ["protocol"] = "freedom" }) }, new JsonObject { ["remarks"] = "Alias", ["outbounds"] = new JsonArray(xhttpOutbound.DeepClone()) });
var batchJson = Subscriptions.Parse(jsonSubscription.ToJsonString());
Assert(batchJson.Profiles.Count == 1 && batchJson.Duplicates == 1 && batchJson.Skipped == 0, "Xray JSON subscription preserves XHTTP and skips utility outbounds");
Reject(sample + "&security=tls&allowInsecure=true", "reject disabling TLS certificate checks");
var chained = (JsonObject)xhttpOutbound.DeepClone(); chained["proxySettings"] = new JsonObject { ["tag"] = "other" };
Reject(chained.ToJsonString(), "reject dependent proxy chains instead of silently connecting directly");
var unsafeConfig = (JsonObject)xhttpOutbound.DeepClone(); unsafeConfig["streamSettings"]!["tlsSettings"] = new JsonObject { ["keyLog"] = "secret.log" };
Reject(unsafeConfig.ToJsonString(), "reject file output from subscription settings");
foreach (var network in new[] { "tcp", "raw", "ws", "grpc", "httpupgrade", "xhttp", "kcp" })
{
    var profile = ProfileParser.Parse(network, sample + "&type=" + network);
    string file = Path.Combine(Storage.Runtime, "check.xray.json");
    File.WriteAllText(file, VlessConfig.Build(profile, 18735));
    Assert(await Connection.RunAsync(Connection.XrayPath, ["run", "-test", "-config", file]) == 0, "Xray validates VLESS " + network);
    File.Delete(file);
}
var hysteriaTransport = ProfileParser.Parse("Hysteria transport", sample + "&type=hysteria&security=tls&sni=example.com&auth=local-test");
string hysteriaFile = Path.Combine(Storage.Runtime, "hysteria-check.json"); File.WriteAllText(hysteriaFile, VlessConfig.Build(hysteriaTransport, 18735));
Assert(await Connection.RunAsync(Connection.XrayPath, ["run", "-test", "-config", hysteriaFile]) == 0, "Xray validates VLESS over Hysteria transport with TLS"); File.Delete(hysteriaFile);
Reject(sample + "&type=quic", "explain removed legacy QUIC transport");
Reject(sample + "&type=http", "reject removed legacy HTTP/2 transport");
Reject(sample + "&type=kcp&seed=legacy", "explain removed legacy mKCP seed");
string bridgeFile = Path.Combine(data, "bridge.json"); File.WriteAllText(bridgeFile, ProfileParser.BuildProxyConfig(vless, 18734, true, 18735));
Assert(await Connection.RunAsync(engine, ["check", "-c", bridgeFile]) == 0, "sing-box validates TUN to Xray bridge and uplink bypass");
string ssAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:p@ss:word"));
var ssProfile = ProfileParser.Parse("", $"ss://{ssAuth}@127.0.0.1:1088#Shadowsocks");
Assert(ssProfile.Protocol == Protocol.Shadowsocks && ProxyCore.Outbound(ssProfile.Source, ssProfile.Protocol)["settings"]!["servers"]![0]!["password"]!.GetValue<string>() == "p@ss:word", "Shadowsocks SIP002 decodes credential separators safely");
var ssLegacy = ProfileParser.Parse("", "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:pass@127.0.0.1:1088")));
Assert(ssLegacy.Protocol == Protocol.Shadowsocks, "legacy whole-link Shadowsocks Base64 import");
var hy = ProfileParser.Parse("", "hy2://user%3Apass@127.0.0.1/?sni=localhost#Hy2");
Assert(hy.Protocol == Protocol.Hysteria2 && ProxyCore.Outbound(hy.Source, hy.Protocol)["settings"]!["port"]!.GetValue<int>() == 443, "Hysteria2 alias defaults to port 443");
Assert(ProxyCore.Outbound(hy.Source, hy.Protocol)["streamSettings"]!["hysteriaSettings"]!["auth"]!.GetValue<string>() == "user:pass", "Hysteria2 userpass credentials preserved");
Reject("hy2://pass@127.0.0.1:443?insecure=1", "Hysteria2 TLS certificate verification remains enabled");
Reject($"ss://{ssAuth}@127.0.0.1:1088?plugin=unknown", "unsupported Shadowsocks plugins rejected explicitly");
var mixed = Subscriptions.Parse($"{hy.Source}\n{ssProfile.Source}\n{sample}");
Assert(mixed.Profiles.Count == 3 && mixed.Skipped == 0, "mixed Hysteria2 Shadowsocks VLESS subscription");
foreach (var p in new[] { hy, ssProfile, ProfileParser.Parse("", "hy2://pass@127.0.0.1:443?obfs=salamander&obfs-password=local-test") })
{
    string file = Path.Combine(Storage.Runtime, "extra-check.json"); File.WriteAllText(file, ProxyCore.Build(p, 18735));
    Assert(await Connection.RunAsync(Connection.XrayPath, ["run", "-test", "-config", file]) == 0, "Xray validates " + p.Protocol + " configuration"); File.Delete(file);
}

int FreePort() { using var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); return ((IPEndPoint)l.LocalEndpoint).Port; }
using var target = new TcpListener(IPAddress.Loopback, 0); target.Start(); int targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
using var cancel = new CancellationTokenSource();
var serve = Task.Run(async () =>
{
    while (!cancel.IsCancellationRequested)
    {
        TcpClient client; try { client = await target.AcceptTcpClientAsync(cancel.Token); } catch (OperationCanceledException) { break; }
        _ = Task.Run(async () => { using (client) { var stream = client.GetStream(); byte[] one = new byte[1]; var request = new StringBuilder(); while (request.Length < 8192 && !request.ToString().EndsWith("\r\n\r\n")) { await stream.ReadExactlyAsync(one); request.Append((char)one[0]); } var body = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 17\r\nConnection: close\r\n\r\nOneTunnel test OK"); await stream.WriteAsync(body); } });
    }
});
async Task EndToEnd(Protocol protocol, bool auth)
{
    int port = FreePort(); string uuid = "6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a";
    var inbound = new JsonObject { ["type"] = protocol == Protocol.SOCKS5 ? "socks" : "vless", ["listen"] = "127.0.0.1", ["listen_port"] = port };
    if (protocol == Protocol.VLESS) inbound["users"] = new JsonArray(new JsonObject { ["uuid"] = uuid });
    else if (auth) inbound["users"] = new JsonArray(new JsonObject { ["username"] = "alice", ["password"] = "p@ss" });
    var server = new JsonObject { ["log"] = new JsonObject { ["level"] = "error" }, ["inbounds"] = new JsonArray(inbound), ["outbounds"] = new JsonArray(new JsonObject { ["type"] = "direct" }) };
    string file = Path.Combine(data, "server.json"); File.WriteAllText(file, server.ToJsonString());
    using var process = Process.Start(Connection.Info(engine, ["run", "-c", file]))!;
    using var serverJob = new ProcessJob(); serverJob.Add(process);
    process.BeginErrorReadLine(); process.BeginOutputReadLine();
    var connection = TestController();
    try
    {
        await Task.Delay(350);
        string source = protocol == Protocol.SOCKS5 ? $"socks5://{(auth ? "alice:p%40ss@" : "")}127.0.0.1:{port}" : $"vless://{uuid}@127.0.0.1:{port}?encryption=none";
        var p = ProfileParser.Parse("E2E", source);
        await connection.StartAsync(p, false);
        Assert(connection.Active && connection.LocalPort > 0 && !connection.TrafficLocked && !KillSwitch.NeedsRecovery, $"{protocol} {(auth ? "auth " : "")}starts");
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{connection.LocalPort}"), UseProxy = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var result = await http.GetStringAsync($"http://127.0.0.1:{targetPort}/");
        Assert(result == "OneTunnel test OK", $"{protocol} {(auth ? "auth " : "")}transfers real traffic through two engines");
        try { await connection.StartAsync(p, false); throw new Exception("Duplicate start allowed"); } catch (InvalidOperationException) { }
        Assert(connection.Active, "rejected duplicate start preserves existing session");
        if (serviceLocal) { await connection.StopAsync(); Assert(!connection.Active && !connection.NeedsRecovery, $"{protocol} Go service shutdown completes cleanup"); return; }
        var journal = System.Text.Json.JsonSerializer.Deserialize<Session>(File.ReadAllText(Path.Combine(Storage.Root, "session.json")))!;
        Assert(File.Exists(journal.ConfigPath), "runtime config exists only during session");
        await connection.StopAsync();
        Assert(!connection.Active && !File.Exists(journal.ConfigPath) && !Connection.NeedsRecovery, $"{protocol} shutdown cleans process/config/journal");
    }
    finally { await connection.StopAsync(); if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
}
async Task XrayTransport(string network, string options = "")
{
    int port = FreePort(); string uuid = "6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a";
    var profile = ProfileParser.Parse("Transport test", $"vless://{uuid}@127.0.0.1:{port}?encryption=none&type={network}" + (network == "grpc" ? "&serviceName=test" : network is "ws" or "xhttp" or "httpupgrade" ? "&path=%2Ftest" : ""));
    var stream = VlessConfig.Outbound(profile.Source)["streamSettings"]!.DeepClone();
    if (options.Contains("fragment"))
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        JsonArray Lines(string text) => new(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => (JsonNode?)JsonValue.Create(s.Trim())).ToArray());
        stream["security"] = "tls";
        stream["tlsSettings"] = new JsonObject { ["certificates"] = new JsonArray(new JsonObject { ["certificate"] = Lines(certificate.ExportCertificatePem()), ["key"] = Lines(rsa.ExportPkcs8PrivateKeyPem()) }) };
        var client = VlessConfig.Outbound(profile.Source); client["streamSettings"]!["security"] = "tls";
        client["streamSettings"]!["tlsSettings"] = new JsonObject { ["serverName"] = "localhost", ["pinnedPeerCertSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData)) };
        profile = ProfileParser.Parse("TLS fixture", client.ToJsonString());
    }
    if (options.Length > 0) profile = profile with { Transport = new TransportOptions { Fragment = options.Contains("fragment"), Mux = options.Contains("mux") } };
    var server = new JsonObject { ["log"] = new JsonObject { ["loglevel"] = "none" }, ["inbounds"] = new JsonArray(new JsonObject { ["listen"] = "127.0.0.1", ["port"] = port, ["protocol"] = "vless", ["settings"] = new JsonObject { ["decryption"] = "none", ["clients"] = new JsonArray(new JsonObject { ["id"] = uuid }) }, ["streamSettings"] = stream }), ["outbounds"] = new JsonArray(new JsonObject { ["protocol"] = "freedom" }) };
    string file = Path.Combine(data, "xray-server.json"); File.WriteAllText(file, server.ToJsonString());
    using var serverProcess = Process.Start(Connection.Info(Connection.XrayPath, ["run", "-config", file]))!;
    using var serverJob = new ProcessJob(); serverJob.Add(serverProcess); serverProcess.BeginOutputReadLine(); serverProcess.BeginErrorReadLine();
    var c = TestController();
    try
    {
        await Task.Delay(450); await c.StartAsync(profile, false);
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{c.LocalPort}"), UseProxy = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        Assert(await http.GetStringAsync($"http://127.0.0.1:{targetPort}/") == "OneTunnel test OK", $"VLESS {network} transfers real traffic through sing-box + Xray client + Xray server");
        if (options.Length > 0)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => http.GetStringAsync($"http://127.0.0.1:{targetPort}/")));
            Assert(responses.All(text => text == "OneTunnel test OK"), $"VLESS {network} {options}: eight parallel requests with verified TLS where applicable");
        }
        if (serviceLocal) { await c.StopAsync(); Assert(!c.Active && !c.NeedsRecovery, "Go service Xray and bridge shutdown completes cleanup"); return; }
        var journal = System.Text.Json.JsonSerializer.Deserialize<Session>(File.ReadAllText(Path.Combine(Storage.Root, "session.json")))!;
        await c.StopAsync();
        Assert(!File.Exists(journal.ConfigPath + ".xray.json") && !Connection.NeedsRecovery, "Xray and bridge shutdown remove both runtime configs");
    }
    finally { await c.StopAsync(); if (!serverProcess.HasExited) { serverProcess.Kill(true); await serverProcess.WaitForExitAsync(); } File.Delete(file); }
}
async Task ExtraProtocol(Protocol protocol, string variant, bool noise = false)
{
    int port = FreePort(); string password = variant.StartsWith("2022") ? Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()) : "local-test-password";
    string link = protocol == Protocol.Shadowsocks ? "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(variant + ":" + password)) + $"@127.0.0.1:{port}" : $"hy2://{password}@127.0.0.1:{port}?sni=localhost" + (variant == "salamander" ? "&obfs=salamander&obfs-password=local-obfs" : "");
    var p = ProfileParser.Parse("Extra test", link); var outbound = ProxyCore.Outbound(p.Source, p.Protocol);
    JsonObject inbound;
    if (protocol == Protocol.Shadowsocks)
        inbound = new JsonObject { ["listen"] = "127.0.0.1", ["port"] = port, ["protocol"] = "shadowsocks", ["settings"] = new JsonObject { ["method"] = variant, ["password"] = password, ["network"] = "tcp,udp" } };
    else
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        JsonArray Lines(string text) => new(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => (JsonNode?)JsonValue.Create(s.Trim())).ToArray());
        var serverStream = (JsonObject)outbound["streamSettings"]!.DeepClone();
        serverStream["tlsSettings"] = new JsonObject { ["alpn"] = new JsonArray("h3"), ["certificates"] = new JsonArray(new JsonObject { ["certificate"] = Lines(certificate.ExportCertificatePem()), ["key"] = Lines(rsa.ExportPkcs8PrivateKeyPem()) }) };
        outbound["streamSettings"]!["tlsSettings"]!["pinnedPeerCertSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.RawData));
        p = ProfileParser.Parse("Extra test", outbound.ToJsonString());
        inbound = new JsonObject { ["listen"] = "127.0.0.1", ["port"] = port, ["protocol"] = "hysteria", ["settings"] = new JsonObject { ["version"] = 2, ["clients"] = new JsonArray(new JsonObject { ["auth"] = password }) }, ["streamSettings"] = serverStream };
    }
    if (noise) p = p with { Transport = new TransportOptions { Noise = true } };
    var server = new JsonObject { ["log"] = new JsonObject { ["loglevel"] = "none" }, ["inbounds"] = new JsonArray(inbound), ["outbounds"] = new JsonArray(new JsonObject { ["protocol"] = "freedom" }) };
    string file = Path.Combine(Storage.Runtime, "extra-server.json"); File.WriteAllText(file, server.ToJsonString());
    using var serverProcess = Process.Start(Connection.Info(Connection.XrayPath, ["run", "-config", file]))!;
    using var serverJob = new ProcessJob(); serverJob.Add(serverProcess); serverProcess.BeginErrorReadLine(); serverProcess.BeginOutputReadLine();
    var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var c = TestController(new Connection(line => diagnostics.Enqueue(line), () => Task.FromResult(new ClockPlan(false))));
    try
    {
        await Task.Delay(500); await c.StartAsync(p, false);
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{c.LocalPort}"), UseProxy = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        Assert(await http.GetStringAsync($"http://127.0.0.1:{targetPort}/") == "OneTunnel test OK", $"{protocol} {variant} transfers real traffic with certificate verification where applicable");
        if (noise) Assert(c.Active && !c.TrafficLocked, "Hysteria2 UDP noise preserves authenticated traffic without system routing");
    }
    catch { Console.Error.WriteLine(string.Join('\n', diagnostics.TakeLast(12))); throw; }
    finally { await c.StopAsync(); if (!serverProcess.HasExited) { serverProcess.Kill(true); await serverProcess.WaitForExitAsync(); } File.Delete(file); }
}
async Task ClockCorrection()
{
    await using var ntp = new TestNtp(120);
    int port=FreePort();
    string serverKey=Convert.ToBase64String(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
    string userKey=Convert.ToBase64String(Enumerable.Range(33,32).Select(i=>(byte)i).ToArray());
    var server = new JsonObject {
        ["log"]=new JsonObject{["level"]="error"},
        ["ntp"]=new JsonObject{["enabled"]=true,["server"]="127.0.0.1",["server_port"]=ntp.Port,["interval"]="30m"},
        ["inbounds"]=new JsonArray(new JsonObject{["type"]="shadowsocks",["listen"]="127.0.0.1",["listen_port"]=port,["method"]="2022-blake3-aes-256-gcm",["password"]=serverKey,["users"]=new JsonArray(new JsonObject{["name"]="unit",["password"]=userKey})}),
        ["outbounds"]=new JsonArray(new JsonObject{["type"]="direct"})};
    string file=Path.Combine(Storage.Runtime,"clock-server.json"); File.WriteAllText(file,server.ToJsonString());
    string exe=Connection.EnginePath(Protocol.SOCKS5);
    Assert(await Connection.RunAsync(exe,["check","-c",file])==0,"SS2022 multi-user NTP test server validates");
    using var process=Process.Start(Connection.Info(exe,["run","-c",file]))!;
    using var job=new ProcessJob();job.Add(process);process.BeginErrorReadLine();process.BeginOutputReadLine();
    var c=new Connection(clockPlanner:()=>Task.FromResult(new ClockPlan(true,"127.0.0.1",ntp.Port)));
    var before=DateTimeOffset.UtcNow;
    try
    {
        await Task.Delay(400);
        string auth=Convert.ToBase64String(Encoding.UTF8.GetBytes("2022-blake3-aes-256-gcm:"+serverKey+":"+userKey));
        var p=ProfileParser.Parse("Clock test",$"ss://{auth}@127.0.0.1:{port}");await c.StartAsync(p,false);
        using var handler=new HttpClientHandler{Proxy=new WebProxy($"http://127.0.0.1:{c.LocalPort}"),UseProxy=true};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
        Assert(await http.GetStringAsync($"http://127.0.0.1:{targetPort}/")=="OneTunnel test OK","SS2022 multi-key connection works with NTP time 120 seconds ahead of Windows");
        Assert(c.ClockSynchronized && ntp.Requests>=2 && (DateTimeOffset.UtcNow-before).TotalSeconds<20,"both engines use internal corrected time without changing Windows clock");
    }
    finally { await c.StopAsync();if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}File.Delete(file); }
}
try
{
    if (!args.Contains("--extra-only"))
    {
    await EndToEnd(Protocol.SOCKS5, false);
    await EndToEnd(Protocol.SOCKS5, true);
    await EndToEnd(Protocol.VLESS, false);
    foreach (var network in new[] { "raw", "ws", "grpc", "httpupgrade", "xhttp", "kcp" }) await XrayTransport(network);
    }
    await ExtraProtocol(Protocol.Shadowsocks, "aes-128-gcm");
    if (!serviceLocal) await ExtraProtocol(Protocol.Shadowsocks, "2022-blake3-aes-256-gcm");
    await ExtraProtocol(Protocol.Hysteria2, "plain");
    await ExtraProtocol(Protocol.Hysteria2, "salamander");
    if (serviceLocal)
    {
        await XrayTransport("raw", "fragment");
        await XrayTransport("raw", "mux");
        await XrayTransport("ws", "fragment-mux");
        await ExtraProtocol(Protocol.Hysteria2, "plain", true);
        await ExtraProtocol(Protocol.Hysteria2, "salamander", true);
    }
    if (!serviceLocal) await ClockCorrection();
    var recovery = new Connection(); await recovery.RecoverAsync(); Assert(!Connection.NeedsRecovery, "idempotent recovery");
    var marker = Path.Combine(Storage.Runtime, "OneTunnel123456789abc.json"); File.WriteAllText(marker, "{}");
    File.WriteAllText(Path.Combine(Storage.Root, "session.json"), System.Text.Json.JsonSerializer.Serialize(new Session("proxy", "untrusted", marker, "OneTunnel123456789abc", int.MaxValue, 1)));
    try { await recovery.StartAsync(socks, false); throw new Exception("Stale session allowed"); } catch (InvalidOperationException) { }
    Assert(Connection.NeedsRecovery && File.Exists(marker), "start does not erase stale recovery journal");
    await recovery.RecoverAsync(); Assert(!Connection.NeedsRecovery && !File.Exists(marker), "recover terminated process and stale config");
    File.WriteAllText(Path.Combine(Storage.Root, "session.json"), System.Text.Json.JsonSerializer.Serialize(new Session("WireGuard", "untrusted.exe", marker, "../malformed", 0, 0)));
    try { await recovery.RecoverAsync(); throw new Exception("Malformed recovery accepted"); } catch (InvalidOperationException) { }
    Assert(!recovery.Active && Connection.NeedsRecovery, "reject malformed journal without accepting unsafe session");
    File.Delete(Path.Combine(Storage.Root, "session.json"));
    Console.WriteLine($"ALL {checks} CHECKS PASSED. Test data: {data}");
}
finally { cancel.Cancel(); target.Stop(); await serve; }
}
catch (Exception ex) { Console.Error.WriteLine($"TEST FAILURE: {ex.GetType().Name}: {ex.Message}"); Environment.ExitCode = 1; }
