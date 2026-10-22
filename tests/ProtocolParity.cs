using OneTunnel.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ProtocolParity
{
    // Synthetic inputs only. Never export user profiles or subscription data.
    public static void Export()
    {
        const string id = "6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a";
        const string xrayPath = @"C:\OneTunnel\engines\xray.exe";
        string vless = $"vless://{id}@example.com:443?encryption=none";
        string key = Convert.ToBase64String(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
        string wg = $"[Interface]\nPrivateKey = {key}\nAddress = 10.9.0.2/32\nDNS = 1.1.1.1\n[Peer]\nPublicKey = {key}\nEndpoint = example.com:51820\nAllowedIPs = 10.9.0.1/32";
        var inputs = new List<(Protocol Kind,string Source)> { (Protocol.WireGuard,wg), (Protocol.AmneziaWG,wg.Replace("[Peer]","Jc = 3\nH1 = 12-34\nI1 = <b 0x40>\n[Peer]")), (Protocol.WireGuard,wg.Replace("10.9.0.2/32","10.9.0.2/32, fd00::2/128")), (Protocol.SOCKS5,"socks5://user:p%40ss%3A%2B@127.0.0.1:1080"), (Protocol.SOCKS5,"socks5://[::1]:1080"), (Protocol.SOCKS5,"socks5://user:@example.com:1080") };
        foreach (string network in new[] { "tcp","raw","ws","websocket","grpc","httpupgrade","xhttp","splithttp","kcp","mkcp","hysteria" })
            inputs.Add((Protocol.VLESS,vless+$"&type={network}&security=tls&sni=front.example&path=%2Fvpn&host=http.example&serviceName=tunnel&authority=authority&alpn=h2,http%2F1.1"));
        inputs.Add((Protocol.VLESS,vless+"&type=tcp&security=reality&flow=xtls-rprx-vision&pbk=public-key&sid=abcd&pqv=verify"));
        inputs.Add((Protocol.VLESS,vless+"&type=ws&ed=2048&eh=Sec-WebSocket-Protocol"));
        inputs.Add((Protocol.VLESS,vless+"&headerType=http&path=%2Fa&host=cover.example"));
        inputs.Add((Protocol.VLESS,vless+"&type=grpc&multiMode=multi"));
        inputs.Add((Protocol.VLESS,vless.Replace("example.com","пример.рф")+"&security=tls"));
        inputs.Add((Protocol.SOCKS5,"socks5://пример.рф:1080"));
        inputs.Add((Protocol.VLESS,vless+"&type=xhttp&mode=packet-up&extra="+Uri.EscapeDataString("{\"noGRPCHeader\":true,\"downloadSettings\":{\"address\":\"download.example\",\"port\":443,\"security\":\"tls\",\"tlsSettings\":{\"serverName\":\"download.example\"}}}")));
        foreach (string method in new[] { "aes-128-gcm","aes-256-gcm","chacha20-poly1305","xchacha20-poly1305","2022-blake3-aes-128-gcm","2022-blake3-aes-256-gcm","2022-blake3-chacha20-poly1305" })
        {
            string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(method+":test:pass+word"));
            inputs.Add((Protocol.Shadowsocks,"ss://"+auth+"@example.com:443#Test"));
        }
        inputs.Add((Protocol.Shadowsocks,"ss://"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-128-gcm:test@example.com:443"))));
        inputs.Add((Protocol.Hysteria2,"hy2://auth%3Apass%2Bword@example.com?sni=front.example&alpn=h3,h2&pinSHA256=test-pin&ech=test-ech"));
        inputs.Add((Protocol.Hysteria2,"hysteria2://auth@example.com:24443?obfs=salamander&obfs-password=password"));
        // Raw advanced JSON must preserve unknown engine-supported options.
        foreach (var item in inputs.Where(x=>x.Kind is Protocol.VLESS or Protocol.Hysteria2 or Protocol.Shadowsocks).ToArray())
            inputs.Add((item.Kind,ProxyCore.Outbound(item.Source,item.Kind).ToJsonString()));
        var uot = ProxyCore.Outbound("ss://aes-128-gcm:pass@example.com:443",Protocol.Shadowsocks);
        uot["settings"]!["servers"]![0]!["uot"]=true; uot["settings"]!["servers"]![0]!["UoTVersion"]=2;
        inputs.Add((Protocol.Shadowsocks,uot.ToJsonString()));
        var extended = (JsonObject)uot.DeepClone(); extended["streamSettings"]!["sockopt"]=new JsonObject{["tcpFastOpen"]=true};inputs.Add((Protocol.Shadowsocks,extended.ToJsonString()));
        var rows = new List<object>();
        foreach(var item in inputs) foreach(bool full in new[]{false,true})
        {
            var profile=ProfileParser.Parse("fixture",item.Source,item.Kind);if(full)profile=TunnelPolicy.ForFullDevice(profile);
            bool native=ShadowsocksNative.CanUse(profile),proxy=ProxyCore.IsProxy(item.Kind),needs=ProxyCore.NeedsXray(item.Kind)&&!native;
            var client=proxy?JsonNode.Parse(ProfileParser.BuildProxyConfig(profile,18734,full,needs?18735:null,new(false))):null;
            if(full&&needs)client!["route"]!["rules"]![0]!["process_path"]![0]=xrayPath;
            rows.Add(new {Kind=(int)item.Kind,Source=item.Source,Full=full,NativeSource=proxy?"":profile.Source,NativeShadowsocks=native,NeedsXray=needs,AccurateTime=native&&ShadowsocksNative.RequiresAccurateTime(profile),Proxy=client,Xray=needs?JsonNode.Parse(ProxyCore.Build(profile,18735)):null});
        }
        string target=Path.GetFullPath("GoCore/internal/protocol/testdata/parity.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Exported {rows.Count} synthetic C# protocol parity cases.");
    }
}
