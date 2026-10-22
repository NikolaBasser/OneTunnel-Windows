using OneTunnel.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class GoProfileFixtures
{
    internal static void Verify(string directory)
    {
        using var expected=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,"expected.json")));
        using var actual=JsonDocument.Parse(Storage.Protect(File.ReadAllBytes(Path.Combine(directory,"v1-go.bin")),false));
        var a=actual.RootElement;var e=expected.RootElement;
        if(a.GetProperty("Version").GetInt32()!=1||!a.GetProperty("Profiles").Deserialize<List<Profile>>()!.SequenceEqual(e.GetProperty("Profiles").Deserialize<List<Profile>>()!)||!a.GetProperty("CollapsedGroups").Deserialize<HashSet<string>>()!.SetEquals(e.GetProperty("CollapsedGroups").Deserialize<HashSet<string>>()!)||a.GetProperty("SubscriptionSources").GetRawText()=="null")throw new Exception("Go DPAPI rewrite loses C# settings");
        var origins=a.GetProperty("SubscriptionSources").Deserialize<Dictionary<string,string>>()!;var expectedOrigins=e.GetProperty("SubscriptionSources").Deserialize<Dictionary<string,string>>()!;
        if(origins.Count!=expectedOrigins.Count||origins.Any(pair=>expectedOrigins.GetValueOrDefault(pair.Key)!=pair.Value))throw new Exception("Origins lost");
        Console.WriteLine("PASS: legacy C# decrypts Go-written DPAPI profiles with all settings intact.");
    }
    internal static void Export(string directory)
    {
        Storage.PrivateDirectory(directory);
        string key=Convert.ToBase64String(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
        string conf=$"[Interface]\nPrivateKey = {key}\nAddress = 10.9.0.2/32\n[Peer]\nPublicKey = {key}\nEndpoint = 127.0.0.1:51820\nAllowedIPs = 10.9.0.1/32";
        var profiles=new[]{ProfileParser.Parse("🇩🇪 WG",conf),ProfileParser.Parse("AWG",conf.Replace("[Peer]","Jc = 3\n[Peer]")),ProfileParser.Parse("VLESS","vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:443?security=tls&sni=localhost"),ProfileParser.Parse("Socks","socks5://user:fixture@127.0.0.1:1080"),ProfileParser.Parse("Hy2","hy2://fixture@127.0.0.1:443?sni=localhost"),ProfileParser.Parse("SS","ss://"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-128-gcm:fixture"))+"@127.0.0.1:1088")};
        profiles[2]=profiles[2] with {Group="🇩🇪 Подписка",Transport=new TransportOptions{Fragment=true}};
        var document=new{Version=1,Profiles=profiles,CollapsedGroups=new[]{"🇩🇪 Подписка"},SubscriptionSources=new Dictionary<string,string>{{"🇩🇪 Подписка","https://subscription.example/test"}}};
        File.WriteAllBytes(Path.Combine(directory,"v1.bin"),Storage.Protect(JsonSerializer.SerializeToUtf8Bytes(document),true));
        File.WriteAllBytes(Path.Combine(directory,"array.bin"),Storage.Protect(JsonSerializer.SerializeToUtf8Bytes(new[]{new{profiles[0].Id,profiles[0].Name,profiles[0].Protocol,profiles[0].Source}}),true));
        File.WriteAllBytes(Path.Combine(directory,"future.bin"),Storage.Protect(JsonSerializer.SerializeToUtf8Bytes(new{Version=99,Profiles=profiles}),true));
        File.WriteAllText(Path.Combine(directory,"expected.json"),JsonSerializer.Serialize(document));
        Console.WriteLine("Synthetic C# DPAPI fixtures exported; no user profiles read.");
    }
}
