namespace OneTunnel.Core;

public sealed record DesktopReply(bool Available, CoreResponse Response, long PeerStarted, string Message, AppliedProfiles? Prepared = null, DesktopState? Environment = null);
public sealed record ImportPreview(string Source, string Name, int? Protocol, bool Subscription, string Description);
public sealed record ProfileView(string ModeLabel, string ConnectionInfo, string ConnectionHint, string ConfigHelp, string FullHelp);
public sealed record TransportInput(bool Fragment, string FragmentLength, string FragmentInterval, bool Noise, string NoiseLength, string NoiseDelay, bool Mux, string MuxConcurrency);

public static class DesktopBackend
{
    public static Task<DesktopReply> CallAsync(string command, Guid? id = null, bool fullDevice = false) =>
        ProfileHost.CallAsync<DesktopReply>(command, new { ID = id?.ToString() ?? "", FullDevice = fullDevice }, 240);
    public static Task WatchAsync(Func<DesktopReply, Task> receive, CancellationToken cancellation) => ProfileHost.WatchAsync(receive, cancellation);
}
