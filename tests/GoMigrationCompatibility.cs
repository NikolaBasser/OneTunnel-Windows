using OneTunnel.Client;
using OneTunnel.Core;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;

internal static class GoMigrationCompatibility
{
    // Create a genuine legacy C# session/filter set under the unchanged service
    // owner/root, then recover it exclusively through the new Go public API.
    internal static async Task Run()
    {
        if (!Connection.IsAdministrator) throw new Exception("UAC required for legacy WFP compatibility test.");
        string sid=WindowsIdentity.GetCurrent().User!.Value;
        RuntimeIdentity.ConfigureServiceOwner(sid);
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"OneTunnel","Service",sid);
        Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR",root);
        var client=new ServiceConnection();await client.RefreshAsync();
        if(!client.Available||client.Active||client.NeedsRecovery||Connection.NeedsRecovery)throw new Exception("Idle Go service required; existing session untouched.");
        var nic=NetworkInterface.GetAllNetworkInterfaces().Single(n=>n.Name==SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")));
        var properties=nic.GetIPProperties();var source=properties.UnicastAddresses.First(x=>x.Address.AddressFamily==AddressFamily.InterNetwork).Address;
        int index=properties.GetIPv4Properties()!.Index;
        string installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"OneTunnel","Core","engines","sing-box.exe");
        using var fixture=await SocksSystemFixture.Start(installed);
        var legacy=new Connection();
        ServiceSafetyWatch.DirectoryPath=Environment.GetEnvironmentVariable("ONETUNNEL_SYSTEM_REPORT_DIR")!;
        ServiceSafetyWatch.Arm();
        try
        {
            await legacy.StartAsync(fixture.Profile,true);
            if(KillSwitch.InstalledFilterCount()==0)throw new Exception("Legacy WFP rules absent.");
            var session=JsonSerializer.Deserialize<Session>(File.ReadAllText(Path.Combine(root,"session.json")))!;
            await KillSwitchChecks.BreakTransport(session);
            if(!(await KillSwitchChecks.TcpDenied(source,index)).Denied)throw new Exception("Legacy direct traffic not blocked.");
            for(int i=0;i<30;i++){await client.RefreshAsync();if(client.NeedsRecovery)break;await Task.Delay(100);}
            if(!client.NeedsRecovery||!client.TrafficLocked)throw new Exception("Go failed to recognize legacy journal/protection.");
            await client.RecoverAsync();
            using var direct=KillSwitchChecks.PhysicalHttp(source,index);
            if(Connection.NeedsRecovery||KillSwitch.InstalledFilterCount()!=0||!await KillSwitchChecks.HttpWorks(direct))throw new Exception("Go did not remove legacy protection and restore network.");
            Console.WriteLine("PASS GO MIGRATION: genuine C# journal and persistent WFP filters recovered by native Go; physical HTTPS restored");
        }
        finally
        {
            await client.RefreshAsync();
            if(client.Available&&(client.Active||client.NeedsRecovery))await client.RecoverAsync();
            if(Connection.NeedsRecovery)await legacy.RecoverAsync();
            await ServiceSafetyWatch.Disarm(client);
        }
    }
}
