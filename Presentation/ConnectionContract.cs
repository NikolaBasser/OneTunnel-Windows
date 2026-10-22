namespace OneTunnel.Core;

public interface IConnectionController
{
    bool Active { get; }
    bool Available { get; }
    bool NeedsRecovery { get; }
    bool TrafficLocked { get; }
    bool WholeDevice { get; }
    bool FullDeviceMode { get; }
    bool ClockSynchronized { get; }
    int LocalPort { get; }
    string Status { get; }
    event Action? Changed;
    Task RefreshAsync();
    Task StartAsync(Profile profile, bool fullDevice);
    Task StopAsync();
    Task RecoverAsync();
    Task<string> ProbeAsync();
}

public sealed record CoreStatus(bool Active, bool NeedsRecovery, bool TrafficLocked, bool WholeDevice, bool FullDeviceMode, bool ClockSynchronized, int LocalPort, string Status, Guid? ProfileId);
public sealed record CoreRequest(int Version, string Command, Profile? Profile = null, bool FullDevice = false);
public sealed record CoreResponse(int Version, string Instance, CoreStatus State, string? Result = null, string? Error = null, ulong Revision = 0);

public static class CoreProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 5 * 1024 * 1024;
    public const string ServiceName = "OneTunnelCore";
    public const string PipeName = "OneTunnel.Core.v1";
}
