namespace OneTunnel.Core;

// Presentation DTOs. Validation, import and persistence live exclusively in Go.
public enum Protocol { AmneziaWG, WireGuard, VLESS, SOCKS5, Hysteria2, Shadowsocks, HTTP, Trojan, TUIC, AnyTLS, NaiveProxy }
public sealed record Profile(Guid Id,string Name,Protocol Protocol,string Source)
{
    public ReliabilityOptions? Reliability {get;init;}
    public string Group {get;init;}="Без группы";
    public TransportOptions? Transport {get;init;}
    public string Label=>$"{Name}  ·  {Protocol}";
    public override string ToString()=>Label;
}
public sealed record TransportOptions
{
    public bool Fragment {get;init;}
    public string FragmentLength {get;init;}="";
    public string FragmentInterval {get;init;}="";
    public bool Noise {get;init;}
    public string NoiseLength {get;init;}="";
    public string NoiseDelay {get;init;}="";
    public bool Mux {get;init;}
    public int MuxConcurrency {get;init;}

}
public sealed record ImportResult(List<Profile> Profiles,int Duplicates,int Skipped);
public sealed record SubscriptionMergeResult(List<Profile> Profiles,int Added,int Updated,int Removed);
public sealed record SubscriptionSummary(string Group,int ProfileCount,string UpdatedAt,string Usage,string Expiry,bool CanRefresh);
public sealed record SubscriptionChange(string Name,string Protocol,string Kind);
public sealed record SubscriptionPreview(string Token,string Group,int Added,int Updated,int Removed,int Duplicates,int Skipped,List<SubscriptionChange> Changes,string Usage,string Expiry,string ExpiresAt,bool CanApply);
public sealed record ProfileDocument(int Version,List<Profile> Profiles,HashSet<string> CollapsedGroups,Dictionary<string,string> SubscriptionSources);
public sealed record AppliedProfiles(ProfileDocument Document,Profile? Selected,int Added,int Updated,int Removed,int Duplicates,int Skipped,bool Batch,bool Offline,bool Retired);
public sealed record DesktopState(string Root,bool Administrator,bool NeedsRecovery,bool Awg,bool Xray,bool SingBox,int MaxImportBytes=0);

public sealed record ReliabilityOptions(bool AutoReconnect=false,bool UseFallbacks=false);
public sealed record GroupCheckRow(string ID,string Name,string Message,bool Success,bool NetworkTest,long Milliseconds);
public sealed record GroupCheckResult(string Group,string CheckedAt,List<GroupCheckRow> Rows);
