using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using OneTunnel.Core;

internal static class SubscriptionEndpointDiagnostics
{
    internal static async Task Run()
    {
        var file=Environment.GetEnvironmentVariable("ONETUNNEL_PROFILE_CACHE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OneTunnel","profiles.bin");
        using var doc=JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file),false));
        var list=doc.RootElement.ValueKind==JsonValueKind.Array?doc.RootElement:doc.RootElement.GetProperty("Profiles");
        var profiles=JsonSerializer.Deserialize<List<Profile>>(list)!;
        var rows=new List<object>();
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(6)};
        http.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
        var groups=new Dictionary<string,int>();
        var clock=await ShadowsocksNative.PlanClockAsync();
        Console.WriteLine($"Clock: correction_needed={clock.Synchronize}, HTTPS_offset_seconds={clock.OffsetSeconds:F1}");
        int index=0;
        foreach(var p in profiles.Where(p=>p.Protocol is Protocol.VLESS or Protocol.Hysteria2 or Protocol.Shadowsocks))
        {
            var outbound=ProxyCore.Outbound(p.Source,p.Protocol);
            var transport=outbound["streamSettings"]?["network"]?.GetValue<string>()??"tcp";
            var peer=p.Protocol==Protocol.VLESS?outbound["settings"]!["vnext"]![0]!:p.Protocol==Protocol.Hysteria2?outbound["settings"]!:outbound["settings"]!["servers"]![0]!;
            string host=peer["address"]!.GetValue<string>();int port=peer["port"]!.GetValue<int>();
            index++;
            IPAddress[] ips;
            try { ips=await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(7)); }
            catch { Console.WriteLine($"Endpoint {index}: {p.Protocol}/{transport}, OS_DNS_failed=True"); continue; }
            string group=string.Join(',',ips.Select(x=>x.ToString()).Order());
            if(!groups.ContainsKey(group))groups[group]=groups.Count+1;
            bool numeric=IPAddress.TryParse(host,out _);
            bool special=ips.Any(ip=>IPAddress.IsLoopback(ip)||ip.AddressFamily==AddressFamily.InterNetwork&&ip.GetAddressBytes() is var b&&(b[0] is 0 or 10 or 127||b[0]==192&&b[1]==168||b[0]==172&&b[1] is >=16 and <=31||b[0]==198&&b[1] is 18 or 19));
            string tcp="NotApplicable";
            if(p.Protocol!=Protocol.Hysteria2)
            {
                try{using var socket=new TcpClient();await socket.ConnectAsync(host,port).WaitAsync(TimeSpan.FromSeconds(2));tcp="Reachable";}
                catch(SocketException e){tcp=e.SocketErrorCode.ToString();}catch(TimeoutException){tcp="Timeout";}
            }
            string doh="NumericAddress";
            if(!numeric)
            {
                try{
                    var response=JsonNode.Parse(await http.GetStringAsync("https://cloudflare-dns.com/dns-query?name="+Uri.EscapeDataString(host)+"&type=A",CancellationToken.None));
                    var addresses=(response?["Answer"] as JsonArray)?.OfType<JsonObject>().Where(x=>x["type"]?.GetValue<int>()==1).Select(x=>IPAddress.Parse(x["data"]!.GetValue<string>())).ToArray()??[];
                    doh=addresses.Length==0?"NoAnswer":addresses.Any(ips.Contains)?"MatchesOS":"DiffersFromOS";
                }catch{doh="Unavailable";}
            }
            var row=new{Index=index,Protocol=p.Protocol.ToString(),Transport=transport,Port=port,DnsGroup=groups[group],NumericAddress=numeric,SpecialAddress=special,Ipv4=ips.Count(x=>x.AddressFamily==AddressFamily.InterNetwork),Ipv6=ips.Count(x=>x.AddressFamily==AddressFamily.InterNetworkV6),Tcp=tcp,DoH=doh};
            rows.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!,"endpoint-diagnostics.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }

    internal static async Task Fresh()
    {
        var file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OneTunnel","profiles.bin");
        using var doc=JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(file),false));
        var list=doc.RootElement.ValueKind==JsonValueKind.Array?doc.RootElement:doc.RootElement.GetProperty("Profiles");
        var saved=JsonSerializer.Deserialize<List<Profile>>(list)!;
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var batch=Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL") is {Length:>0} url
            ?await Subscriptions.DownloadAsync(url)
            :new ImportResult(JsonSerializer.Deserialize<List<Profile>>(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"fresh-profiles.bin")),false))!,0,0);
        Console.WriteLine($"Fresh subscription: imported={batch.Profiles.Count}, skipped={batch.Skipped}, duplicates={batch.Duplicates}");
        foreach(var group in batch.Profiles.GroupBy(p=>p.Protocol))
        {
            var identities=saved.Where(p=>p.Protocol==group.Key).Select(Subscriptions.Identity).ToHashSet();
            Console.WriteLine($"Fresh {group.Key}: count={group.Count()}, unchanged={group.Count(p=>identities.Contains(Subscriptions.Identity(p)))}");
        }
        var changes=new Dictionary<string,int>();
        void Diff(JsonNode? a, JsonNode? b,string path)
        {
            if(JsonNode.DeepEquals(a,b))return;
            if(a is JsonObject x&&b is JsonObject y){foreach(var key in x.Select(z=>z.Key).Union(y.Select(z=>z.Key)))Diff(x[key],y[key],path+"."+key);}
            else if(a is JsonArray xa&&b is JsonArray ya&&xa.Count==ya.Count){for(int i=0;i<xa.Count;i++)Diff(xa[i],ya[i],path+"[]");}
            else changes[path]=changes.GetValueOrDefault(path)+1;
        }
        foreach(var fresh in batch.Profiles)
        {
            var previous=saved.FirstOrDefault(p=>p.Protocol==fresh.Protocol&&p.Name==fresh.Name);
            if(previous!=null)Diff(ProxyCore.Outbound(previous.Source,previous.Protocol),ProxyCore.Outbound(fresh.Source,fresh.Protocol),fresh.Protocol.ToString());
        }
        foreach(var change in changes.OrderBy(x=>x.Key))Console.WriteLine($"Changed field: {change.Key}; profiles={change.Value}");
        Console.WriteLine($"Stable name matches: {batch.Profiles.Count(f=>saved.Any(s=>s.Protocol==f.Protocol&&s.Name==f.Name))}; ambiguous incoming names: {batch.Profiles.GroupBy(p=>(p.Protocol,p.Name)).Count(g=>g.Count()>1)}");
        Storage.PrivateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,"fresh-profiles.bin"),Storage.Protect(JsonSerializer.SerializeToUtf8Bytes(batch.Profiles),true));
    }

    internal static async Task ApplyFresh()
    {
        var client=new OneTunnel.Client.ServiceConnection();await client.RefreshAsync();
        if(!client.Available||client.Active||client.NeedsRecovery||client.TrafficLocked)throw new Exception("Idle service required; existing connection untouched.");
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var incoming=JsonSerializer.Deserialize<List<Profile>>(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"fresh-profiles.bin")),false))!;
        var path=Path.Combine(Storage.Root,"profiles.bin");
        var before=File.ReadAllBytes(path);var existing=Storage.Load();var collapsed=Storage.CollapsedGroups.ToHashSet();
        var freshIds=incoming.Select(Subscriptions.Identity).ToHashSet();
        var groups=existing.GroupBy(p=>p.Group).Select(g=>new{Group=g.Key,Matches=g.Count(p=>freshIds.Contains(Subscriptions.Identity(p)))}).Where(g=>g.Matches>0).ToList();
        if(groups.Count!=1||groups[0].Matches<2)throw new Exception("Subscription group identification is ambiguous; settings untouched.");
        var group=groups[0].Group;var merged=SubscriptionMerge.Refresh(existing,incoming,group);
        var untouched=existing.Where(p=>p.Group!=group||!incoming.Any(n=>n.Protocol==p.Protocol)).ToList();
        if(untouched.Any(p=>!merged.Profiles.Contains(p)))throw new Exception("Unrelated profile changed.");
        string backup=Path.Combine(directory,"profiles-before-refresh-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".bin");
        using(var stream=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(before);
        if(!File.ReadAllBytes(path).SequenceEqual(before))throw new Exception("Profiles changed during preparation; no update applied.");
        if(Environment.GetEnvironmentVariable("ONETUNNEL_SUBSCRIPTION_URL") is {Length:>0} source)Storage.SubscriptionSources[group]=source;
        Storage.Save(merged.Profiles);
        var after=Storage.Load();
        if(!after.SequenceEqual(merged.Profiles)||!Storage.CollapsedGroups.SetEquals(collapsed))throw new Exception("Profile refresh verification failed; encrypted backup preserved.");
        var summary=new{Before=existing.Count,After=after.Count,merged.Added,merged.Updated,merged.Removed,UnrelatedProfilesPreserved=untouched.Count,CollapsedGroupsPreserved=true,EncryptedBackupCreated=true};
        File.WriteAllText(Path.Combine(directory,"applied-refresh.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(summary));
    }

    internal static async Task VerifySaved()
    {
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OneTunnel","profiles.bin");
        using var doc=JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(path),false));
        var list=doc.RootElement.ValueKind==JsonValueKind.Array?doc.RootElement:doc.RootElement.GetProperty("Profiles");
        var profiles=JsonSerializer.Deserialize<List<Profile>>(list)!;
        var client=new OneTunnel.Client.ServiceConnection();await client.RefreshAsync();
        if(!client.Available||client.Active||client.NeedsRecovery)throw new Exception("Idle service required.");
        var results=new List<object>();
        foreach(var protocol in new[]{Protocol.Shadowsocks,Protocol.Hysteria2,Protocol.VLESS})
        {
            if(Environment.GetEnvironmentVariable("ONETUNNEL_VERIFY_AUTO_REFRESH")=="1")
            {
                profiles=Storage.Load();
                var group=profiles.First(p=>p.Protocol==protocol).Group;
                var batch=await Subscriptions.DownloadAsync(Storage.SubscriptionSources[group]);
                var merged=SubscriptionMerge.Refresh(profiles,batch.Profiles,group);
                Storage.Save(merged.Profiles);profiles=merged.Profiles;
                Console.WriteLine($"AUTO REFRESH: {protocol}, current_subscription_loaded=True");
            }
            bool verified=false;int tried=0;
            foreach(var profile in profiles.Where(p=>p.Protocol==protocol&&(protocol!=Protocol.VLESS||(ProxyCore.Outbound(p.Source,p.Protocol)["streamSettings"]?["network"]?.GetValue<string>()??"tcp")=="tcp")).Take(4))
            {
                tried++;
                try{await client.StartAsync(profile,false);await client.ProbeAsync();if(client.FullDeviceMode||client.TrafficLocked)throw new Exception("Unexpected system mode");verified=true;}
                catch(OneTunnel.Client.CoreCommandException e){Console.WriteLine($"SAVED CANDIDATE: {protocol}, candidate={tried}, error={e.Code}");}
                finally{await client.StopAsync();}
                if(verified)break;
            }
            results.Add(new{Protocol=protocol.ToString(),Https=verified,CandidatesTried=tried,FullDevice=false,KillSwitch=false});Console.WriteLine($"SAVED PROFILE: {protocol}, HTTPS={verified}, candidates={tried}, system_mode=False, kill_switch=False");
        }
        File.WriteAllText(Path.Combine(directory,"saved-profile-verification.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }

    internal static async Task Health()
    {
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var profiles=JsonSerializer.Deserialize<List<Profile>>(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"fresh-profiles.bin")),false))!;
        var client=new OneTunnel.Client.ServiceConnection();await client.RefreshAsync();
        if(!client.Available||client.Active||client.NeedsRecovery)throw new Exception("Idle service required.");
        foreach(var protocol in new[]{Protocol.VLESS,Protocol.Shadowsocks,Protocol.Hysteria2})
        {
            if(Environment.GetEnvironmentVariable("ONETUNNEL_HEALTH_PROTOCOL") is {Length:>0} only&&only!=protocol.ToString())continue;
            var profile=profiles.First(p=>p.Protocol==protocol&&(protocol!=Protocol.VLESS||(ProxyCore.Outbound(p.Source,p.Protocol)["streamSettings"]?["network"]?.GetValue<string>()??"tcp")=="tcp"));
            try
            {
                await client.StartAsync(profile,false);
                using var handler=new HttpClientHandler{UseProxy=true,Proxy=new WebProxy($"http://127.0.0.1:{client.LocalPort}"),AllowAutoRedirect=false};
                using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(8)};
                var targets=new[]{"https://example.com/","https://www.gstatic.com/generate_204","https://www.msftconnecttest.com/connecttest.txt","https://captive.apple.com/hotspot-detect.html"};
                await Task.WhenAll(targets.Select(async target=>{
                    try{using var response=await http.GetAsync(target);Console.WriteLine($"HEALTH {protocol}: target={new Uri(target).Host}, status={(int)response.StatusCode}");}
                    catch(Exception e){Console.WriteLine($"HEALTH {protocol}: target={new Uri(target).Host}, error={e.GetType().Name}");}
                }));
                var debug=Path.Combine(Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!,"probecheck.exe");
                if(File.Exists(debug))
                {
                    using var process=System.Diagnostics.Process.Start(Connection.Info(debug,[client.LocalPort.ToString()]))!;
                    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35));Console.Write(await output);await errors;
                }
            }
            finally{await client.StopAsync();}
        }
    }

    internal static async Task RouteAB()
    {
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var profiles=JsonSerializer.Deserialize<List<Profile>>(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"fresh-profiles.bin")),false))!;
        foreach(var protocol in new[]{Protocol.Shadowsocks,Protocol.VLESS,Protocol.Hysteria2})
        {
            var p=profiles.First(p=>p.Protocol==protocol&&(protocol!=Protocol.VLESS||(ProxyCore.Outbound(p.Source,p.Protocol)["streamSettings"]?["network"]?.GetValue<string>()??"tcp")=="tcp"));
            foreach(bool bind in new[]{true,false})
            {
                int Port(){using var l=new TcpListener(IPAddress.Loopback,0);l.Start();return ((IPEndPoint)l.LocalEndpoint).Port;}
                int local=Port();int bridge=Port();while(bridge==local)bridge=Port();
                bool native=ShadowsocksNative.CanUse(p),needs=ProxyCore.NeedsXray(p.Protocol)&&!native,clock=native&&ShadowsocksNative.RequiresAccurateTime(p);
                var proxy=JsonNode.Parse(ProfileParser.BuildProxyConfig(p,local,false,needs?bridge:null,new(clock)))!;
                proxy["route"]!["auto_detect_interface"]=bind;
                var plan=new PreparedConnection("",native,needs,clock,local,needs?bridge:null,needs?ProxyCore.Build(p,bridge):"",proxy.ToJsonString(),"{\"enabled\":true,\"server\":\"time.cloudflare.com\",\"server_port\":123,\"interval\":\"30m\"}");
                var log=new List<string>();var connection=new Connection(log.Add);bool ok=false;
                try{await connection.StartPreparedAsync(p,false,plan);ok=await ProviderDiagnostics.Probe(local)=="OK";}
                finally{await connection.StopAsync();}
                Console.WriteLine($"ROUTE A/B: {protocol}, bind_default_NIC={bind}, HTTPS={ok}, errors={ProviderDiagnostics.Classify(log)}");
            }
        }
    }

    internal static async Task ProbeFresh()
    {
        var directory=Environment.GetEnvironmentVariable("ONETUNNEL_DIAGNOSTIC_DIR")!;
        var profiles=JsonSerializer.Deserialize<List<Profile>>(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"fresh-profiles.bin")),false))!;
        var client=new OneTunnel.Client.ServiceConnection();await client.RefreshAsync();
        if(!client.Available||client.Active||client.NeedsRecovery)throw new Exception("Idle service required; existing connection untouched.");
        var results=new List<object>();int index=0;
        var only=Environment.GetEnvironmentVariable("ONETUNNEL_PROBE_INDEX");
        foreach(var profile in profiles.Where(p=>p.Protocol is Protocol.Hysteria2 or Protocol.Shadowsocks||p.Protocol==Protocol.VLESS&&(ProxyCore.Outbound(p.Source,p.Protocol)["streamSettings"]?["network"]?.GetValue<string>()??"tcp")=="tcp"))
        {
            index++;bool ok=false,clock=false;string failure="";
            if(only!=null&&!only.Split(',').Contains(index.ToString()))continue;
            for(int attempt=0;attempt<2&&!ok;attempt++)
            {
                try{await client.StartAsync(profile,false);clock=client.ClockSynchronized;await client.ProbeAsync();ok=true;failure="";}
                catch(OneTunnel.Client.CoreCommandException e){failure=e.Code;}
                finally{await client.StopAsync();}
            }
            results.Add(new{Index=index,Protocol=profile.Protocol.ToString(),Https=ok,ClockSynchronized=clock,Failure=failure});
            File.WriteAllText(Path.Combine(directory,only==null?"fresh-probe-results.json":"fresh-retry-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"FRESH endpoint {index}: {profile.Protocol}, HTTPS={ok}, internal_clock={clock}, error={failure}");
            if(!ok&&only!=null&&profile.Protocol==Protocol.VLESS)
            {
                var peer=ProxyCore.Outbound(profile.Source,profile.Protocol)["settings"]!["vnext"]![0]!;
                var host=peer["address"]!.GetValue<string>();int port=peer["port"]!.GetValue<int>();
                try{var addresses=await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(5));Console.WriteLine($"FAILED endpoint {index}: DNSv4={addresses.Count(a=>a.AddressFamily==AddressFamily.InterNetwork)}, DNSv6={addresses.Count(a=>a.AddressFamily==AddressFamily.InterNetworkV6)}");using var socket=new TcpClient();await socket.ConnectAsync(host,port).WaitAsync(TimeSpan.FromSeconds(3));Console.WriteLine($"FAILED endpoint {index}: raw_TCP=Reachable");}
                catch(SocketException e){Console.WriteLine($"FAILED endpoint {index}: raw_TCP_or_DNS={e.SocketErrorCode}");}catch(TimeoutException){Console.WriteLine($"FAILED endpoint {index}: raw_TCP=Timeout");}
                var logs=new List<string>();var legacy=new Connection(logs.Add);bool legacyOk=false;
                try{await legacy.StartAsync(profile,false);await legacy.ProbeAsync();legacyOk=true;}catch{}
                finally{await legacy.StopAsync();}
                Console.WriteLine($"FAILED endpoint {index}: legacy_HTTPS={legacyOk}, category={ProviderDiagnostics.Classify(logs)}");
                using var dnsHttp=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(6)};
                dnsHttp.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
                foreach(var query in new[]{("A",1),("AAAA",28)})
                {
                    try{var answer=JsonNode.Parse(await dnsHttp.GetStringAsync("https://cloudflare-dns.com/dns-query?name="+Uri.EscapeDataString(host)+"&type="+query.Item1));int count=(answer?["Answer"] as JsonArray)?.OfType<JsonObject>().Count(x=>x["type"]?.GetValue<int>()==query.Item2)??0;Console.WriteLine($"FAILED endpoint {index}: public_DNS_{query.Item1}={count}, status={answer?["Status"]?.GetValue<int>()}");}
                    catch{Console.WriteLine($"FAILED endpoint {index}: public_DNS_{query.Item1}=Unavailable");}
                }
            }
        }
    }
}
