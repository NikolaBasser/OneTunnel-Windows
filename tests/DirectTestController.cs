using OneTunnel.Core;

// Lets the same loopback traffic fixtures exercise either the retained direct
// implementation or the production Go service without changing the fixture.
internal sealed class DirectTestController(Connection inner) : IConnectionController
{
    public bool Available => true;
    public bool Active => inner.Active;
    public bool NeedsRecovery => Connection.NeedsRecovery && !inner.Active;
    public bool TrafficLocked => inner.TrafficLocked;
    public bool WholeDevice => inner.WholeDevice;
    public bool FullDeviceMode => inner.FullDeviceMode;
    public bool ClockSynchronized => inner.ClockSynchronized;
    public int LocalPort => inner.LocalPort;
    public string Status => inner.Status;
    public event Action? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
    public Task RefreshAsync() { inner.Poll(); return Task.CompletedTask; }
    public Task StartAsync(Profile p, bool fullDevice) => inner.StartAsync(p, fullDevice);
    public Task StopAsync() => inner.StopAsync();
    public Task RecoverAsync() => inner.RecoverAsync();
    public Task<string> ProbeAsync() => inner.ProbeAsync();
}
