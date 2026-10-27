namespace OneTunnel.Core;

public sealed record DesktopContext(bool Busy, bool StorageReady, string SelectedID,bool CheckingGroup=false);
public sealed record DesktopActions(bool CanConnect=false, bool CanDisconnect=false, bool CanToggle=false, bool CanProbe=false, bool CanRecover=false, bool CanAdd=false, bool CanEdit=false, bool CanDelete=false, bool CanSelect=false, bool CanChangeMode=false, bool CanSave=false, bool CanInstallService=false, bool CanStartService=false, bool CanClose=true, string ConnectCaption="Подключить",bool CanCheckGroup=false,bool CanCancelGroup=false);
public sealed record DesktopDisplay(string Status="Проверка службы…", string ConnectionInfo="", string ConnectionHint="", string RecoveryHint="", bool ShowSession=false, int ModeIndex=0, bool ShowStatistics=false, string Uploaded="", string Downloaded="", string UploadRate="", string DownloadRate="", string Elapsed="", string StatisticsScopeHint="", string ErrorDiagnostic="");
public sealed record DesktopObservation(bool Available, CoreResponse Response, long PeerStarted, string Message, ulong CheckedAt);
public sealed record DesktopReply(bool Available, CoreResponse Response, long PeerStarted, string Message, AppliedProfiles? Prepared = null, DesktopState? Environment = null, DesktopActions? Actions=null, DesktopDisplay? Display=null, ulong CheckedAt=0);
public sealed record ImportPreview(string Source, string Name, int? Protocol, bool Subscription, string Description);
public sealed record ProfileView(string ModeLabel, string ConnectionInfo, string ConnectionHint, string ConfigHelp, string FullHelp);
public sealed record TransportInput(bool Fragment, string FragmentLength, string FragmentInterval, bool Noise, string NoiseLength, string NoiseDelay, bool Mux, string MuxConcurrency,string MuxProtocol="",string MuxMaxConnections="",string MuxMinStreams="",bool MuxPadding=false,string XUDPConcurrency="",string UDP443Policy="");

public static class DesktopBackend
{
    private static object? RetainedFacts(DesktopReply? retained) => retained is null ? null : new { retained.Response, retained.PeerStarted, retained.CheckedAt };
    public static Task<DesktopReply> CallAsync(string command, Action<DesktopReply> receive, Guid? id = null, bool fullDevice = false, DesktopContext? context=null, DesktopReply? retained=null) =>
        ProfileHost.CallAsync<DesktopReply>(command, new { ID = id?.ToString() ?? "", FullDevice = fullDevice, Context=context, Retained=RetainedFacts(retained) }, 240, received:receive);
    public static Task<DesktopReply> ContextAsync(DesktopContext context, Action<DesktopReply> receive, DesktopReply retained) =>
        ProfileHost.CallAsync<DesktopReply>("desktop.context", new { Context=context, Retained=RetainedFacts(retained) }, received:receive);
    public static Task<DesktopReply> ObservationAsync(DesktopObservation observation, Action<DesktopReply> receive, DesktopContext? context, DesktopReply retained) =>
        ProfileHost.CallAsync<DesktopReply>("desktop.observe", new { Observation=observation, Context=context, Retained=RetainedFacts(retained) }, received:receive);
    public static Task WatchAsync(Func<DesktopObservation, Task> receive, CancellationToken cancellation) => ProfileHost.WatchAsync(receive, cancellation);
}
