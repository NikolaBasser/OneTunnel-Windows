using OneTunnel.Core;

namespace OneTunnel.Client;

// Ordered transport and read-only Go presentation snapshots. No connection
// reducer, epoch/revision policy or action-availability rules belong here.
public sealed class ServiceConnection : IConnectionController
{
    private volatile DesktopReply snapshot = new(false, new(1,"",new(false,false,false,false,false,false,0,"Проверка службы…",null)),0,"");
    private DesktopContext? context;
    private static readonly DesktopActions InitialActions = new();
    private static readonly DesktopDisplay InitialDisplay = new();
    public bool Available => snapshot.Available;
    public bool Active => snapshot.Response.State.Active;
    public bool NeedsRecovery => snapshot.Response.State.NeedsRecovery;
    public bool TrafficLocked => snapshot.Response.State.TrafficLocked;
    public bool WholeDevice => snapshot.Response.State.WholeDevice;
    public bool FullDeviceMode => snapshot.Response.State.FullDeviceMode;
    public bool ClockSynchronized => snapshot.Response.State.ClockSynchronized;
    public int LocalPort => snapshot.Response.State.LocalPort;
    public Guid? ProfileId => snapshot.Response.State.ProfileId;
    public DesktopActions Actions => snapshot.Actions ?? InitialActions;
    public DesktopDisplay Display => snapshot.Display ?? InitialDisplay;
    public string Status => Display.Status;
    public AppliedProfiles? Prepared => snapshot.Prepared;
    public event Action? Changed;
    private void Apply(DesktopReply reply)
    {
        if(reply.Environment!=null) DesktopEnvironment.Accept(reply.Environment);
        snapshot=reply;
        Changed?.Invoke();
    }
    public void ReceiveAutomationReply(DesktopReply reply) => Apply(reply);
    public async Task SetContextAsync(DesktopContext value)
    {
        if(context==value) return; // Deduplicate identical UI inputs on the transport.
        context=value;
        try { await DesktopBackend.ContextAsync(value,Apply,snapshot); }
        catch { context=null; throw; }
    }
    private async Task<DesktopReply> Call(string command, Guid? id=null, bool fullDevice=false)
    {
        var reply=await DesktopBackend.CallAsync(command,Apply,id,fullDevice,context,snapshot);
        if(reply.Response.Error!=null) throw new CoreCommandException(reply.Response.Error,reply.Message);
        return reply;
    }
    public async Task RefreshAsync() { try { await Call("connection.status"); } catch { } }
    public async Task StartAsync(Profile profile,bool fullDevice) => await Call("connection.start",profile.Id,fullDevice);
    public async Task<string> ToggleAsync(Guid? id,bool fullDevice) => (await Call("connection.toggle",id,fullDevice)).Message;
    public async Task StopAsync() => await Call("connection.stop");
    public async Task RecoverAsync() => await Call("connection.recover");
    public async Task<string> ProbeAsync() => (await Call("connection.probe")).Response.Result ?? "";
    public async Task StartServiceAsync() => await Call("service.start");
    public async Task InstallServiceAsync() => await Call("service.install");
    public async Task ObserveAsync(CancellationToken cancellation)
    {
        while(!cancellation.IsCancellationRequested)
        {
            try { await DesktopBackend.WatchAsync(async observation => { await DesktopBackend.ObservationAsync(observation,Apply,context,snapshot); },cancellation); }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { return; }
            catch { try { await Call("desktop.unavailable"); } catch { } }
            try { await Task.Delay(1000,cancellation); } catch(OperationCanceledException) { return; }
        }
    }
}
public sealed class CoreCommandException(string code,string message) : InvalidOperationException(message)
{ public string Code {get;}=code; }
