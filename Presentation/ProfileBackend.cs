namespace OneTunnel.Core;

public static class ProfileBackend
{
    public static Task<string> DiagnosticsAsync()=>ProfileHost.CallAsync<string>("diagnostics.report");
    public static Task<ProfileDocument> FavoriteAsync(Guid id,bool favorite)=>ProfileHost.CallAsync<ProfileDocument>("ui.favorite.set",new{ID=id.ToString(),Favorite=favorite});
    public static Task<ProfileLibraryView> LibraryAsync(string sort,bool favoritesOnly)=>ProfileHost.CallAsync<ProfileLibraryView>("library.view",new{Sort=sort,FavoritesOnly=favoritesOnly});
    public static Task<List<GroupSummary>> GroupsAsync()=>ProfileHost.CallAsync<List<GroupSummary>>("group.list");
    public static Task<ProfileDocument> RenameGroupAsync(string group,string name,string token)=>ProfileHost.CallAsync<ProfileDocument>("ui.group.rename",new{Group=group,Name=name,Token=token});
    public static Task<ProfileDocument> DeleteGroupAsync(string group,string token)=>ProfileHost.CallAsync<ProfileDocument>("ui.group.delete",new{Group=group,Token=token});
    public static Task<ProfileDocument> SetAllGroupsExpandedAsync(bool expanded)=>ProfileHost.CallAsync<ProfileDocument>("ui.group.expansion.all",new{Expanded=expanded});
    public static Task<AppliedProfiles> SaveEditorAsync(string name,string source,string group,Guid? id,TransportInput transport,ReliabilityOptions? reliability=null)=>ProfileHost.CallAsync<AppliedProfiles>("ui.editor.save",new{Name=name,Source=source,Group=group,ID=id?.ToString()??"",TransportInput=transport,Reliability=reliability});
    public static Task<GroupCheckResult> CheckGroupAsync(string group,CancellationToken cancellation=default)=>ProfileHost.CallAsync<GroupCheckResult>("group.check",new{Group=group},1000,cancellation:cancellation);
    public static Task<GroupResultsView> GroupResultsAsync(string sort,string filter)=>ProfileHost.CallAsync<GroupResultsView>("group.results.view",new{Sort=sort,Filter=filter});
    public static Task<Guid> FastestGroupResultAsync(string token)=>ProfileHost.CallAsync<Guid>("group.results.fastest",new{Token=token});
    public static Task ClearGroupResultsAsync()=>ProfileHost.CallAsync<object?>("group.results.clear");
    public static Task<ProfileOrganizationView> ProfileOrganizationAsync(Guid id)=>ProfileHost.CallAsync<ProfileOrganizationView>("profile.organization",new{ID=id.ToString()});
    public static Task<NetworkPreferencesView> NetworkPreferencesAsync(Guid id)=>ProfileHost.CallAsync<NetworkPreferencesView>("network.preferences.view",new{ID=id.ToString()});
    public static Task<ProfileDocument> SaveNetworkPreferencesAsync(Guid id,string token,NetworkInput input)=>ProfileHost.CallAsync<ProfileDocument>("ui.network.preferences.save",new{ID=id.ToString(),Token=token,NetworkInput=input});
    public static Task<AppliedProfiles> DuplicateProfileAsync(Guid id,string name,string group,string token)=>ProfileHost.CallAsync<AppliedProfiles>("ui.profile.duplicate",new{ID=id.ToString(),Name=name,Group=group,Token=token});
    public static Task<AppliedProfiles> MoveProfileAsync(Guid id,string group,string token)=>ProfileHost.CallAsync<AppliedProfiles>("ui.profile.move",new{ID=id.ToString(),Group=group,Token=token});
    public static Task<Profile> LoadEditorAsync(Guid id)=>ProfileHost.CallAsync<Profile>("editor.load",new{ID=id.ToString()});
    public static Task<ImportPreview> PreviewAsync(string source,CancellationToken cancellation)=>ProfileHost.CallAsync<ImportPreview>("editor.preview",new{Source=source},cancellation:cancellation);
    public static Task<ImportPreview> ImportFileAsync(string path)=>ProfileHost.CallAsync<ImportPreview>("editor.import",new{Path=path});
    public static Task<List<SubscriptionSummary>> SubscriptionsAsync()=>ProfileHost.CallAsync<List<SubscriptionSummary>>("ui.subscription.list");
    public static Task<SubscriptionPreview> PreviewSubscriptionAsync(string group,CancellationToken cancellation=default)=>ProfileHost.CallAsync<SubscriptionPreview>("ui.subscription.refresh.preview",new{Group=group},cancellation:cancellation);
    public static Task<ProfileDocument> LoadDocumentAsync()=>ProfileHost.CallAsync<ProfileDocument>("ui.store.load");
    public static Task<AppliedProfiles> ApplySubscriptionAsync(string token)=>ProfileHost.CallAsync<AppliedProfiles>("ui.subscription.refresh.apply",new{Token=token});
    public static Task CancelSubscriptionPreviewAsync(string token)=>ProfileHost.CallAsync<object?>("ui.subscription.refresh.cancel",new{Token=token});
    public static TransportOptions DefaultTransport()=>ProfileHost.Call<TransportOptions>("transport.defaults");
    public static Task<ProfileView> ViewAsync(Protocol protocol)=>ProfileHost.CallAsync<ProfileView>("profile.view",new{Requested=(int)protocol});
    public static ProfileDocument Delete(Guid id)=>ProfileHost.Call<ProfileDocument>("ui.delete",new{ID=id.ToString()});
    public static ProfileDocument SetGroupExpanded(string group,bool expanded)=>ProfileHost.Call<ProfileDocument>("ui.group.expansion",new{Group=group,Expanded=expanded});
}
public static class DesktopEnvironment
{
    private static DesktopState? state;
    public static DesktopState State=>state??=ProfileHost.Call<DesktopState>("hello");
    public static void Accept(DesktopState value)=>state=value;
    public static bool NeedsRecovery=>State.NeedsRecovery;
    public static bool IsAdministrator=>State.Administrator;
}
public static class Storage
{
    public static string Root=>DesktopEnvironment.State.Root;
    public static HashSet<string> CollapsedGroups {get;}=[];
    public static HashSet<Guid> FavoriteIDs {get;}=[];
    public static void Initialize()=>_ = Load();
    public static void Accept(ProfileDocument d){CollapsedGroups.Clear();CollapsedGroups.UnionWith(d.CollapsedGroups);FavoriteIDs.Clear();if(d.FavoriteIDs!=null)FavoriteIDs.UnionWith(d.FavoriteIDs);}
    public static List<Profile> Load(){var d=ProfileHost.Call<ProfileDocument>("ui.store.load");Accept(d);return d.Profiles;}
}
