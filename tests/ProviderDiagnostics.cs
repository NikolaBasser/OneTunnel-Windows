using OneTunnel.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

internal static class ProviderDiagnostics
{
    public static async Task VerifyApplication()
    {
        var batch = await Subscriptions.DownloadAsync(Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL")!);
        var profiles = batch.Profiles.Where(p=>p.Protocol is Protocol.Hysteria2 or Protocol.Shadowsocks).ToList();
        var report = new List<object>(); int index=0, passed=0;
        foreach(var p in profiles)
        {
            index++; bool ok=false, synchronized=false; string failure=""; int attempts=0;
            while(!ok && attempts<2)
            {
                attempts++; var c = new Connection();
                try { await c.StartAsync(p,false); synchronized=c.ClockSynchronized; await c.ProbeAsync(); ok=true; }
                catch(Exception ex) { failure=ex.GetType().Name; }
                finally { await c.StopAsync(); }
            }
            if(ok)passed++;
            Console.WriteLine($"APPLICATION endpoint {index}: protocol={p.Protocol}, verified={ok}, clock_synchronized={synchronized}, attempts={attempts}, failure={(ok?"none":failure)}");
            report.Add(new {Index=index,Protocol=p.Protocol.ToString(),Verified=ok,ClockSynchronized=synchronized,Attempts=attempts,Failure=ok?null:failure});
        }
        string? reportPath=Environment.GetEnvironmentVariable("ONETUNNEL_PROVIDER_REPORT");
        if(reportPath!=null) File.WriteAllText(reportPath,System.Text.Json.JsonSerializer.Serialize(new {Passed=passed,Total=profiles.Count,Results=report},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        if(passed!=profiles.Count) throw new Exception($"Provider verification: passed {passed}/{profiles.Count}");
    }
    public static async Task Run(bool ipv4 = false, bool doh = false, bool ntp = false)
    {
        var batch = await Subscriptions.DownloadAsync(Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL")!);
        var profiles = batch.Profiles.Where(p => p.Protocol is Protocol.Hysteria2 or Protocol.Shadowsocks).ToList();
        if(ntp) profiles=profiles.Where(p=>p.Protocol==Protocol.Shadowsocks).ToList();
        Console.WriteLine($"Provider diagnostic: {profiles.Count} endpoints; no credentials or endpoint addresses are printed.");
        int index = 0;
        var groups = new Dictionary<string,int>();
        foreach (var savedProfile in profiles)
        {
            var profile = savedProfile;
            index++;
            var ob = ProxyCore.Outbound(profile.Source, profile.Protocol);
            var peer = profile.Protocol == Protocol.Hysteria2 ? ob["settings"]! : ob["settings"]!["servers"]![0]!;
            string host = peer["address"]!.GetValue<string>(); int port = peer["port"]!.GetValue<int>();
            var ips = await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(8));
            string groupKey = string.Join(',', ips.Select(x=>x.ToString()).Order()); if(!groups.ContainsKey(groupKey)) groups[groupKey]=groups.Count+1;
            bool reserved = ips.Any(ip => IPAddress.IsLoopback(ip) || ip.GetAddressBytes() is var bytes && bytes.Length == 4 && (bytes[0] is 0 or 10 or 127 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >=16 and <=31));
            Console.WriteLine($"Endpoint {index}: DNS_group={groups[groupKey]}, private_or_loopback={reserved}");
            if(doh && !IPAddress.TryParse(host,out _))
            {
                using var http = new HttpClient{Timeout=TimeSpan.FromSeconds(10)}; http.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
                var json = JsonNode.Parse(await http.GetStringAsync("https://cloudflare-dns.com/dns-query?name="+Uri.EscapeDataString(host)+"&type=A"));
                var answers = (json?["Answer"] as JsonArray)?.OfType<JsonObject>().Where(a=>a["type"]?.GetValue<int>()==1).Select(a=>IPAddress.Parse(a["data"]!.GetValue<string>())).ToArray() ?? [];
                Console.WriteLine($"Endpoint {index}: DoH_count={answers.Length}, DoH_matches_OS={answers.Any(a=>ips.Contains(a))}");
                if(answers.Length>0) ips=answers;
            }
            Console.WriteLine($"Endpoint {index}: DNS_v4={ips.Count(a=>a.AddressFamily==AddressFamily.InterNetwork)}, DNS_v6={ips.Count(a=>a.AddressFamily==AddressFamily.InterNetworkV6)}, force_v4={ipv4}");
            if(ipv4 && ips.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork) is {} ip)
            {
                peer["address"] = ip.ToString();
                if (profile.Protocol == Protocol.Hysteria2 && string.IsNullOrEmpty(ob["streamSettings"]?["tlsSettings"]?["serverName"]?.GetValue<string>())) ob["streamSettings"]!["tlsSettings"]!["serverName"] = host;
                profile = profile with {Source=ob.ToJsonString()};
            }
            bool tcp = false;
            if (profile.Protocol == Protocol.Shadowsocks) { try { using var socket = new TcpClient(); await socket.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(3)); tcp = true; } catch { } }
            Console.WriteLine($"Endpoint {index}: {profile.Protocol}, TCP_reachable={tcp}, numeric_address={IPAddress.TryParse(host, out _)}");
            if(profile.Protocol==Protocol.Shadowsocks) { var password=peer["password"]!.GetValue<string>(); Console.WriteLine($"Endpoint {index}: SS_key_parts={password.Split(':').Length}, TCP_header_none={ob["streamSettings"]?["tcpSettings"]?["header"]?["type"]?.GetValue<string>() is null or "none"}"); }
            var logs = new ConcurrentQueue<string>();
            var c = new Connection(line => logs.Enqueue(line));
            if(!ntp)
            {
            try
            {
                await c.StartAsync(profile, false);
                Console.WriteLine($"Endpoint {index}: xray HTTPS={await Probe(c.LocalPort)}");
            }
            catch (Exception e) { Console.WriteLine($"Endpoint {index}: xray operation={e.GetType().Name}" + (e is InvalidOperationException ? ": " + e.Message : "")); }
            finally { await c.StopAsync(); }
            Console.WriteLine($"Endpoint {index}: xray reasons={Classify(logs)}");
            }
            await SingBox(profile, index, ntp);
        }
    }
    public static string Classify(IEnumerable<string> lines)
    {
        string all = string.Join('\n', lines).ToLowerInvariant();
        var labels = new Dictionary<string, string[]> {
            ["TLS_CERT"]=["failed to verify certificate", "unknown authority", "certificate is not valid", "certificate has expired", "certificate signed"],
            ["AUTH"]=["auth failed", "authentication failed", "invalid password", "unauthorized"],
            ["TIMEOUT"]=["timeout", "deadline exceeded", "timed out"],
            ["REFUSED"]=["refused", "actively refused"], ["EOF"]=["eof"],
            ["DECRYPT"]=["decrypt", "invalid response", "message authentication failed", "invalid header", "unexpected response"],
            ["DNS"]=["no such host", "name resolution", "dns query"],
            ["TLS_PROTOCOL"]=["tls handshake", "crypto_error", "protocol violation", "no application protocol"],
            ["CLOSED"]=["connection reset", "connection closed", "forcibly closed"]
        };
        return string.Join(',', labels.Where(p => p.Value.Any(all.Contains)).Select(p => p.Key));
    }
    public static async Task<string> Probe(int port)
    {
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{port}"), UseProxy = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(9) };
        try { using var response = await http.GetAsync("https://example.com/"); string content = await response.Content.ReadAsStringAsync(); return response.IsSuccessStatusCode && content.Contains("Example Domain") ? "OK" : "HTTP_" + (int)response.StatusCode; }
        catch (Exception e) { return e.GetType().Name; }
    }
    public static JsonObject Convert(Profile profile)
    {
        var original = ProxyCore.Outbound(profile.Source, profile.Protocol);
        var stream = original["streamSettings"]!;
        var peer = profile.Protocol == Protocol.Hysteria2 ? original["settings"]! : original["settings"]!["servers"]![0]!;
        var outbound = new JsonObject { ["tag"] = "proxy", ["server"] = peer["address"]!.DeepClone(), ["server_port"] = peer["port"]!.DeepClone() };
        if (profile.Protocol == Protocol.Hysteria2)
        {
            outbound["type"] = "hysteria2"; outbound["password"] = stream["hysteriaSettings"]!["auth"]!.DeepClone();
            var tls = new JsonObject { ["enabled"] = true, ["server_name"] = stream["tlsSettings"]?["serverName"]?.DeepClone() ?? peer["address"]!.DeepClone() };
            if (stream["tlsSettings"]?["alpn"] is { } alpn) tls["alpn"] = alpn.DeepClone();
            outbound["tls"] = tls;
            if (stream["finalmask"]?["udp"] is JsonArray masks && masks.Count > 0) throw new Exception("Alternate masked configuration needs explicit mapping");
        }
        else
        {
            outbound["type"] = "shadowsocks"; outbound["method"] = peer["method"]!.DeepClone(); outbound["password"] = peer["password"]!.DeepClone();
            if (peer["uot"]?.GetValue<bool>() == true) outbound["udp_over_tcp"] = new JsonObject { ["enabled"] = true, ["version"] = peer["UoTVersion"]?.DeepClone() ?? JsonValue.Create(2) };
        }
        return outbound;
    }
    private static async Task SingBox(Profile profile, int index, bool ntp)
    {
        var logs = new ConcurrentQueue<string>();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        string path = Path.Combine(Storage.Runtime, "provider-singbox.json");
        Process? process = null;
        using var job = new ProcessJob();
        try
        {
            var config = new JsonObject { ["log"] = new JsonObject { ["level"] = "debug", ["timestamp"] = false }, ["inbounds"] = new JsonArray(new JsonObject { ["type"] = "mixed", ["listen"] = "127.0.0.1", ["listen_port"] = port }), ["outbounds"] = new JsonArray(Convert(profile)), ["route"] = new JsonObject { ["final"] = "proxy" } };
            if(ntp) { var ip=(await Dns.GetHostAddressesAsync("time.cloudflare.com")).First(a=>a.AddressFamily==AddressFamily.InterNetwork); config["ntp"]=new JsonObject{["enabled"]=true,["server"]=ip.ToString(),["server_port"]=123,["interval"]="30m"}; }
            File.WriteAllText(path, config.ToJsonString());
            string engine = Connection.EnginePath(Protocol.SOCKS5);
            int check = await Connection.RunAsync(engine, ["check", "-c", path]);
            if (check != 0) { Console.WriteLine($"Endpoint {index}: singbox CONFIG_REJECTED"); return; }
            process = new Process { StartInfo = Connection.Info(engine, ["run", "-c", path]) };
            process.OutputDataReceived += (_, e) => { if(e.Data != null) logs.Enqueue(e.Data); }; process.ErrorDataReceived += (_, e) => { if(e.Data != null) logs.Enqueue(e.Data); };
            process.Start(); job.Add(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            await Task.Delay(ntp ? 1500 : 400);
            Console.WriteLine($"Endpoint {index}: singbox HTTPS={await Probe(port)}");
            if(ntp) Console.WriteLine($"Endpoint {index}: NTP_updated={logs.Any(s=>s.Contains("updated time", StringComparison.OrdinalIgnoreCase))}");
        }
        catch(Exception e) { Console.WriteLine($"Endpoint {index}: singbox operation={e.GetType().Name}"); }
        finally { if(process is {HasExited:false}) {process.Kill(true); await process.WaitForExitAsync();} process?.Dispose(); File.Delete(path); }
        Console.WriteLine($"Endpoint {index}: singbox reasons={Classify(logs)}");
    }
}
