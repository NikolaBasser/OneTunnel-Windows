namespace OneTunnel.Core;

public static class ProfileBackend
{
    public static void Validate(Profile profile)=>ProfileHost.Call<object>("validate",new{Profile=profile});
    public static string Group(string group,string name,string source,bool subscription)=>ProfileHost.Call<string>("group",new{Group=group,Name=name,Source=source,Subscription=subscription});
    public static string ReadFile(string path)=>ProfileHost.Call<string>("file.read",new{Path=path});
    public static Task<AppliedProfiles> SaveEditorAsync(string name,string source,string group,Guid? id,TransportOptions? transport)=>Task.Run(()=>ProfileHost.Call<AppliedProfiles>("editor.save",new{Name=name,Source=source,Group=group,ID=id?.ToString()??"",Transport=transport}));
    public static AppliedProfiles Apply(ImportResult batch,string source,string group)=>ProfileHost.Call<AppliedProfiles>("subscription.apply",new{Incoming=batch.Profiles,Source=source,Group=group});
    public static Task<AppliedProfiles> PrepareAsync(Guid id)=>Task.Run(()=>ProfileHost.Call<AppliedProfiles>("subscription.prepare",new{ID=id.ToString()}));
    public static ProfileDocument Delete(Guid id)=>ProfileHost.Call<ProfileDocument>("delete",new{ID=id.ToString()});
    public static ProfileDocument SetGroupExpanded(string group,bool expanded)=>ProfileHost.Call<ProfileDocument>("group.expansion",new{Group=group,Expanded=expanded});
    public static Task LegacyRecoverAsync()=>Task.Run(()=>ProfileHost.Call<object>("legacy.recover"));
}
public static class DesktopEnvironment
{
    public static DesktopState State=>ProfileHost.Call<DesktopState>("hello");
    public static bool NeedsRecovery=>State.NeedsRecovery;
    public static bool IsAdministrator=>State.Administrator;
}
// DTO facades retained to keep presentation binding and tests simple. None of
// these methods parses a protocol, fetches a subscription or performs DPAPI.
public static class ProfileParser
{
    public static Profile Parse(string name,string source,Protocol? requested=null)=>ProfileHost.Call<Profile>("parse",new{Name=name,Source=source,Requested=requested});
}
public static class Subscriptions
{
    public const int MaxBytes=4*1024*1024;
    public static bool IsUrl(string source)=>ProfileHost.Call<bool>("url",new{Source=source});
}
public static class Storage
{
    public static string Root=>DesktopEnvironment.State.Root;
    public static HashSet<string> CollapsedGroups {get;}=[];
    public static Dictionary<string,string> SubscriptionSources {get;}=[];
    public static void Initialize()=>_ = Load();
    public static void Accept(ProfileDocument d){CollapsedGroups.Clear();CollapsedGroups.UnionWith(d.CollapsedGroups);SubscriptionSources.Clear();foreach(var pair in d.SubscriptionSources)SubscriptionSources.Add(pair.Key,pair.Value);}
    public static List<Profile> Load(){var d=ProfileHost.Call<ProfileDocument>("store.load");Accept(d);return d.Profiles;}
    public static void Save(IEnumerable<Profile> profiles)=>ProfileHost.Call<object>("store.save",new{Document=new ProfileDocument(1,profiles.ToList(),CollapsedGroups,SubscriptionSources)});
    public static byte[] Protect(byte[] data,bool encrypt)=>ProfileHost.Call<byte[]>("store.protect",new{Data=data,Encrypt=encrypt});
}
