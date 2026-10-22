using OneTunnel.Core;

namespace OneTunnel.Client;

// View state and transport glue only. Go authenticates/controls the service,
// prepares profiles, evaluates errors and owns connection sequencing.
public sealed class ServiceConnection : IConnectionController
{
    private CoreStatus state = new(false, false, false, false, false, false, 0, "Проверка службы…", null);
    private string instance = "";
    private ulong revision;
    private long epoch;
    private readonly object gate = new();
    public bool Available { get; private set; }
    public bool Active => state.Active;
    public bool NeedsRecovery => state.NeedsRecovery;
    public bool TrafficLocked => state.TrafficLocked;
    public bool WholeDevice => state.WholeDevice;
    public bool FullDeviceMode => state.FullDeviceMode;
    public bool ClockSynchronized => state.ClockSynchronized;
    public int LocalPort => state.LocalPort;
    public string Status => Available ? state.Status : "Служба недоступна · состояние сети не подтверждено";
    public Guid? ProfileId => state.ProfileId;
    public AppliedProfiles? Prepared { get; private set; }
    public event Action? Changed;
    private void Apply(DesktopReply reply)
    {
        bool notify = false;
        lock(gate)
        {
        if (reply.Environment != null) DesktopEnvironment.Accept(reply.Environment);
        if (reply.Prepared != null) Prepared = reply.Prepared;
        if (!reply.Available) { Available = false; notify = true; }
        else if (!(reply.PeerStarted < epoch || reply.PeerStarted == epoch && reply.Response.Instance == instance && reply.Response.Revision > 0 && reply.Response.Revision < revision))
        {
        if (epoch != reply.PeerStarted || instance != reply.Response.Instance) revision = 0;
        epoch = reply.PeerStarted; instance = reply.Response.Instance; if (reply.Response.Revision > 0) revision = reply.Response.Revision;
        state = reply.Response.State; Available = true; notify = true;
        }
        }
        if (notify) Changed?.Invoke();
    }
    private async Task<DesktopReply> Call(string command, Guid? id = null, bool fullDevice = false)
    {
        var reply = await DesktopBackend.CallAsync(command, id, fullDevice);
        Apply(reply);
        if (reply.Response.Error != null) throw new CoreCommandException(reply.Response.Error, reply.Message);
        return reply;
    }
    public async Task RefreshAsync() { try { await Call("connection.status"); } catch { } }
    public async Task StartAsync(Profile profile, bool fullDevice) { Prepared = null; await Call("connection.start", profile.Id, fullDevice); }
    public async Task StopAsync() => await Call("connection.stop");
    public async Task RecoverAsync() => await Call("connection.recover");
    public async Task<string> ProbeAsync() => (await Call("connection.probe")).Response.Result ?? "Проверка не завершена.";
    public async Task StartServiceAsync() => await Call("service.start");
    public async Task InstallServiceAsync() => await Call("service.install");
    public async Task ObserveAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try { await DesktopBackend.WatchAsync(reply => { Apply(reply); return Task.CompletedTask; }, cancellation); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch { Available = false; Changed?.Invoke(); }
            try { await Task.Delay(1000, cancellation); } catch (OperationCanceledException) { return; }
        }
    }
}

public sealed class CoreCommandException(string code, string message) : InvalidOperationException(message)
{ public string Code { get; } = code; }
