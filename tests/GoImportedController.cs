using OneTunnel.Core;
using System.Diagnostics;
using System.Text.Json;

// End-to-end fixture: the real Go user host imports, validates and encrypts a
// profile, exits, and the installed Go service then transfers actual traffic.
internal sealed class GoImportedController(IConnectionController inner) : IConnectionController
{
    public bool Active=>inner.Active;
    public bool Available=>inner.Available;
    public bool NeedsRecovery=>inner.NeedsRecovery;
    public bool TrafficLocked=>inner.TrafficLocked;
    public bool WholeDevice=>inner.WholeDevice;
    public bool FullDeviceMode=>inner.FullDeviceMode;
    public bool ClockSynchronized=>inner.ClockSynchronized;
    public int LocalPort=>inner.LocalPort;
    public string Status=>inner.Status;
    public event Action? Changed {add=>inner.Changed+=value;remove=>inner.Changed-=value;}
    public Task RefreshAsync()=>inner.RefreshAsync();
    public Task StopAsync()=>inner.StopAsync();
    public Task RecoverAsync()=>inner.RecoverAsync();
    public Task<string> ProbeAsync()=>inner.ProbeAsync();
    public async Task StartAsync(Profile profile,bool fullDevice)
    {
        if(fullDevice)throw new Exception("This fixture must never change system routing");
        if(inner.Active){await inner.StartAsync(profile,false);return;}
        var root=Path.Combine(Storage.Root,"go-import-"+Guid.NewGuid().ToString("N"));
        var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"OneTunnel.Profiles.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["ONETUNNEL_DATA_DIR"]=root;
        using var host=Process.Start(info)!;host.BeginErrorReadLine();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await CoreWire.WriteAsync(host.StandardInput.BaseStream,new{Version=1,Command="editor.save",profile.Name,profile.Source,profile.Group,profile.Transport},timeout.Token);
            var response=await CoreWire.ReadAsync<Reply>(host.StandardOutput.BaseStream,timeout.Token);
            if(response.Error!=null)throw new Exception("Go import failed: "+response.Error);
            var saved=response.Value.GetProperty("Selected").Deserialize<Profile>()!;
            host.StandardInput.Close();await host.WaitForExitAsync(timeout.Token);
            if(host.ExitCode!=0||!File.Exists(Path.Combine(root,"profiles.bin")))throw new Exception("Go profile host failed to save and exit");
            await inner.StartAsync(saved,false);
            Console.WriteLine("PASS GO PROFILE PIPELINE: imported and encrypted "+profile.Protocol+" before service launch; user host exited safely");
        }
        finally{if(!host.HasExited)host.Kill();}
    }
    private sealed record Reply(int Version,JsonElement Value,string? Error);
}
