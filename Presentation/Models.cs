namespace OneTunnel.Core;

// Presentation DTOs. Validation, import and persistence live exclusively in Go.
public enum Protocol { AmneziaWG, WireGuard, VLESS, SOCKS5, Hysteria2, Shadowsocks, HTTP, Trojan, TUIC, AnyTLS, NaiveProxy }
public sealed record Profile(Guid Id,string Name,Protocol Protocol,string Source)
{
    public ReliabilityOptions? Reliability {get;init;}
    public string Group {get;init;}="Без группы";
    public TransportOptions? Transport {get;init;}
    public NetworkOptions? Network {get;init;}
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
public sealed record ProfileDocument(int Version,List<Profile> Profiles,HashSet<string> CollapsedGroups,Dictionary<string,string> SubscriptionSources)
{
    public HashSet<Guid> FavoriteIDs {get;init;}=[];
}
public sealed record ProfileLibraryView([property: System.Text.Json.Serialization.JsonPropertyName("ids")] List<Guid> IDs);
public sealed record GroupSummary(string Name,int ProfileCount,bool IsSubscription,string Token);
public sealed record AppliedProfiles(ProfileDocument Document,Profile? Selected,int Added,int Updated,int Removed,int Duplicates,int Skipped,bool Batch,bool Offline,bool Retired);
public sealed record DesktopState(string Root,bool Administrator,bool NeedsRecovery,bool Awg,bool Xray,bool SingBox,int MaxImportBytes=0);

public sealed record ReliabilityOptions(bool AutoReconnect=false,bool UseFallbacks=false);
public sealed record GroupCheckRow(string ID,string Name,string Message,bool Success,bool NetworkTest,long Milliseconds);
public sealed record GroupCheckResult(string Group,string CheckedAt,List<GroupCheckRow> Rows);
public sealed record GroupResultsView(string Group,string CheckedAt,List<GroupResultDisplayRow> Rows,bool HasResults,bool Stale,bool CanSelectFastest,string Token,string Message);
public sealed record GroupResultDisplayRow(string ID,string Name,string Message,bool Success,bool NetworkTest,long Milliseconds,string TimingLabel);
public sealed record ProfileOrganizationView(string ID,string Name,string Group,string SuggestedName,string SuggestedGroup,string Token,List<string> ManualTargets,bool CanDuplicate,bool CanMove);

public sealed record DNSOptions(string Transport,string ServerIP,string ServerName,int Port,string Path);
public sealed record NetworkRule(string Kind,string Value,string Action);
public sealed record NetworkOptions(DNSOptions? DNS,List<NetworkRule> Rules);
public sealed record NetworkPreferencesView(string ID,string Token,string Message,string Mode,string ServerIP,string ServerName,string Port,string Path,string RulesText,bool Supported,bool CanSave);
public sealed record NetworkInput(string Mode,string ServerIP,string ServerName,string Port,string Path,string RulesText);
