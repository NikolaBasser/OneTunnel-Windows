using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneTunnel;
using OneTunnel.Core;

internal static class Program
{
    private static readonly string Output = Environment.GetEnvironmentVariable("ONETUNNEL_UI_TEST_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "onetunnel-workspace-ui-" + Guid.NewGuid().ToString("N"));
    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Output);
        var data = Path.Combine(Output, "synthetic-data-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR", data);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var transport = new TransportInput(false, "100-200", "10-20", false, "50-150", "10-20", false, "8");
        foreach (var (name, source, group) in new[] {
            ("🇳🇱 Amsterdam", "vless://11111111-1111-4111-8111-111111111111@192.0.2.1:443?security=tls&type=ws&sni=example.com", "Personal subscription"),
            ("🇩🇪 Frankfurt", "hysteria2://synthetic@example.com:443/?sni=example.com", "Personal subscription"),
            ("🇫🇮 Helsinki — a long synthetic profile name for layout verification", "socks5://192.0.2.3:1080", "Work profiles") })
            ProfileBackend.SaveEditorAsync(name, source, group, null, transport, new(true, false)).GetAwaiter().GetResult();
        // Only this synthetic test uses the raw store API to seed a saved source.
        var host = typeof(ProfileBackend).Assembly.GetType("OneTunnel.Core.ProfileHost")!;
        T Raw<T>(string command, object? args = null) => (T)host.GetMethod("Call", BindingFlags.Static | BindingFlags.Public)!.MakeGenericMethod(typeof(T)).Invoke(null, [command, args])!;
        var rawDocument = JsonNode.Parse(Raw<JsonElement>("store.load").GetRawText())!.AsObject();
        rawDocument["SubscriptionSources"] = new JsonObject { ["Personal subscription"] = "https://subscription.example/synthetic-test-only" };
        rawDocument["SubscriptionMetadata"] = new JsonObject { ["Personal subscription"] = new JsonObject { ["Upload"] = 200, ["Download"] = 300, ["Total"] = 1000, ["Expire"] = 1900000000, ["UpdatedAt"] = "2026-10-23T00:00:00Z" } };
        Raw<object?>("store.save", new { Document = rawDocument });
        using var resource = typeof(Program).Assembly.GetManifestResourceStream("TestApp.xaml")!;
        var xml = System.Xml.Linq.XDocument.Load(resource);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(ns + "Application.Resources")!.Elements());
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()) };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        TrayChecks.Run();
        var window = new MainWindow { ShowInTaskbar = false, Left = -5000, Top = -5000 };
        window.Show();
        T Find<T>(string name) where T : FrameworkElement => (T)(window.FindName(name) ?? throw new Exception("Missing control: " + name));
        void Pump() { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        void Wait(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(25); while (!condition() && DateTime.UtcNow < until) Pump(); if (!condition()) throw new Exception("UI condition timed out"); }
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Panel(string value) => typeof(MainWindow).GetMethod("ShowPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [value]);
        bool LibraryIdle() => !(bool)typeof(MainWindow).GetField("libraryPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("favoritePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Wait(() => Find<Button>("AddProfileButton").IsEnabled && Find<Button>("EditButton").IsEnabled && LibraryIdle());
        var list = Find<ListBox>("ProfilesList");
        Assert(list.Items.Count == 3, "Synthetic profiles loaded");
        var snapshot = Storage.Load();
        var profileFile = Path.Combine(data, "profiles.bin");
        var before = SHA256.HashData(File.ReadAllBytes(profileFile));
        var search = Find<TextBox>("SearchBox");
        search.Text = "AMSTERDAM"; Pump(); Assert(list.Items.Count == 1, "Case-insensitive name search");
        search.Text = "hysteria2"; Pump(); Assert(list.Items.Count == 1, "Protocol search");
        search.Text = "work profiles"; Pump(); Assert(list.Items.Count == 1, "Group search");
        search.Text = "no-synthetic-match"; Pump(); Assert(list.Items.Count == 0, "No-result filtering");
        Capture(window, "workspace-empty-results.png");
        search.Text = ""; Pump(); Assert(list.Items.Count == 3, "Clearing search restores profiles");
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Search does not write profile storage");
        list.SelectedIndex = 0; Wait(() => Find<Button>("EditButton").IsEnabled);
        Capture(window, "workspace-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Capture(window, "workspace-minimum.png");
        foreach (var name in new[] { "AddProfileButton", "ConnectButton", "SearchBox", "ProfilesList", "HideToTrayButton", "TrayNotificationsBox", "FavoriteButton", "LibrarySortBox", "FavoritesOnlyBox" }) {
            var element = Find<FrameworkElement>(name); Assert(element.IsVisible && element.ActualWidth > 20 && element.ActualHeight > 10, name + " remains visible at minimum size");
        }
        window.Width = 1240; window.Height = 850; Pump();
        Click("EditButton"); Wait(() => Find<StackPanel>("EditorPanel").IsVisible && Find<TextBox>("SourceBox").Text.Length > 0);
        Assert(Find<CheckBox>("AutoReconnectCheck").IsChecked == true, "Existing reconnection setting rendered");
        Find<Expander>("TransportExpander").IsExpanded = true;
        Pump();
        Assert(Find<CheckBox>("FragmentCheck").IsVisible, "Transport settings retained");
        Capture(window, "workspace-editor.png");
        Panel("connection"); Wait(() => list.IsVisible);
        Panel("engines"); Pump(); Assert(Find<StackPanel>("EnginesPanel").IsVisible && Find<Button>("RecoveryButton").IsVisible, "Recovery remains reachable");
        Capture(window, "workspace-recovery.png");
        Panel("connection"); Pump();
        Assert(Storage.Load().Count == snapshot.Count, "Navigation preserves synthetic profiles");
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Navigation does not write profile storage");
        Panel("activity");
        var report = Find<TextBox>("DiagnosticsReport");
        Assert(report.IsReadOnly && report.Text.Length == 0 && !Find<Button>("CopyDiagnosticsButton").IsEnabled && !Find<Button>("SaveDiagnosticsButton").IsEnabled, "Diagnostics requires explicit generation before exporting");
        Click("GenerateDiagnosticsButton");
        Assert(!Find<Button>("GenerateDiagnosticsButton").IsEnabled && !Find<Button>("CopyDiagnosticsButton").IsEnabled && !Find<Button>("SaveDiagnosticsButton").IsEnabled, "Pending diagnostics disables repeated generation and export");
        Wait(() => Find<Button>("GenerateDiagnosticsButton").IsEnabled);
        Assert(report.Text.Length > 0 && Find<Button>("CopyDiagnosticsButton").IsEnabled && Find<Button>("SaveDiagnosticsButton").IsEnabled, "Go report enables reviewed export");
        string reviewedReport = report.Text;
        using (var diagnostics = JsonDocument.Parse(reviewedReport)) {
            Assert(diagnostics.RootElement.GetProperty("schemaVersion").GetInt32() == 1 && diagnostics.RootElement.GetProperty("profiles").GetProperty("total").GetInt32() == 3, "Actual Go IPC returns the expected report schema and counts");
            using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "core-reference.json")));
            Assert(diagnostics.RootElement.GetProperty("profileHost").GetProperty("revision").GetString() == pin.RootElement.GetProperty("SourceRevision").GetString() && !diagnostics.RootElement.GetProperty("profileHost").GetProperty("dirty").GetBoolean(), "Report identifies the exact clean bundled host revision");
        }
        Assert(!reviewedReport.Contains("subscription.example") && !reviewedReport.Contains("synthetic-test-only") && !reviewedReport.Contains("Amsterdam") && !reviewedReport.Contains(data), "Go diagnostics excludes synthetic names, sources and local paths");
        var exportPath = Path.Combine(Output, "synthetic-diagnostics.json");
        var export = (Task)typeof(MainWindow).GetMethod("SaveDiagnosticsReportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [exportPath])!;
        Wait(() => export.IsCompleted); export.GetAwaiter().GetResult();
        Assert(File.ReadAllBytes(exportPath).SequenceEqual(new System.Text.UTF8Encoding(false).GetBytes(reviewedReport)), "File export preserves the exact reviewed report without a BOM");
        Capture(window, "diagnostics-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        foreach (var scroll in Descendants(window).OfType<ScrollViewer>().Where(s => s.Content is Grid)) scroll.ScrollToEnd();
        Pump(); Capture(window, "diagnostics-minimum.png");
        window.Width = 1240; window.Height = 850;
        var encryptedProfiles = File.ReadAllBytes(profileFile);
        try {
            File.WriteAllText(profileFile, "SYNTHETIC-CORRUPT-STORE-SECRET");
            Click("GenerateDiagnosticsButton");
            Assert(report.Text.Length == 0 && !Find<Button>("CopyDiagnosticsButton").IsEnabled && !Find<Button>("SaveDiagnosticsButton").IsEnabled, "Regeneration clears stale preview and export before reading Go");
            Wait(() => Find<Button>("GenerateDiagnosticsButton").IsEnabled);
            Assert(report.Text.Length == 0 && !Find<Button>("CopyDiagnosticsButton").IsEnabled && !Find<Button>("SaveDiagnosticsButton").IsEnabled, "Failed generation leaves no stale export");
            Assert(!Find<TextBlock>("Feedback").Text.Contains("SECRET") && !Find<TextBlock>("Feedback").Text.Contains(data), "Diagnostics failure feedback excludes underlying store data");
            Assert(File.ReadAllText(profileFile) == "SYNTHETIC-CORRUPT-STORE-SECRET", "Failed report does not modify even a corrupt synthetic store");
        }
        finally { File.WriteAllBytes(profileFile, encryptedProfiles); }
        Click("GenerateDiagnosticsButton"); Wait(() => Find<Button>("GenerateDiagnosticsButton").IsEnabled);
        Assert(report.Text == reviewedReport, "Regeneration recovers after restoring the synthetic store");
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Report generation and export preserve encrypted profiles and settings");
        // Do not replace the user's clipboard or open native Save dialogs during tests.
        Find<TextBox>("NameBox").Text = "Synthetic unfinished draft";
        Panel("subscriptions"); Pump();
        Assert(Find<StackPanel>("SubscriptionsPanel").IsVisible && Find<Button>("ImportSubscriptionButton").IsVisible && Find<ListBox>("SubscriptionGroupsList").IsVisible, "Subscription management page and import remain reachable");
        Wait(() => Find<ListBox>("SubscriptionGroupsList").Items.Count == 1);
        Assert(Find<TextBlock>("SubscriptionUsage").Text.Contains("500 B"), "Provider usage is rendered from the real Go summary");
        var summaryJSON = JsonSerializer.Serialize(ProfileBackend.SubscriptionsAsync().GetAwaiter().GetResult());
        Assert(!summaryJSON.Contains("subscription.example") && !summaryJSON.Contains("synthetic-test-only") && !summaryJSON.Contains("Source"), "Subscription summaries do not expose saved sources");
        var busyField = typeof(MainWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var downloadField = typeof(MainWindow).GetField("subscriptionDownload", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refreshHandler = typeof(MainWindow).GetMethod("RefreshSubscription_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
        busyField.SetValue(window, true);
        refreshHandler.Invoke(window, [window, new RoutedEventArgs()]);
        Assert(downloadField.GetValue(window) == null, "Busy refresh does not create an orphaned cancellation source");
        busyField.SetValue(window, false);
        using (var inFlight = new CancellationTokenSource()) {
            downloadField.SetValue(window, inFlight);
            refreshHandler.Invoke(window, [window, new RoutedEventArgs()]);
            Assert(ReferenceEquals(downloadField.GetValue(window), inFlight), "Repeated refresh preserves cancellation for the original operation");
            downloadField.SetValue(window, null);
        }
        var review = new SubscriptionPreview("synthetic-review-token", "Personal subscription", 1, 1, 1, 0, 0,
            [new("🇳🇱 New synthetic node", "VLESS", "added"), new("Changed synthetic node", "Hysteria2", "updated"), new("Retired synthetic node", "SOCKS5", "removed")],
            "500 B used · 500 B remaining of 1.0 KB · reported by provider", "18 Mar 2030 · reported by provider", "2026-10-23T12:10:00Z", true);
        var pendingField = typeof(MainWindow).GetField("pendingSubscriptionPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        pendingField.SetValue(window, review);
        typeof(MainWindow).GetMethod("PresentSubscriptionPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [review]);
        Wait(() => Find<Button>("ApplySubscriptionButton").IsEnabled);
        Assert(Find<ItemsControl>("SubscriptionChangesList").Items.Count == 3 && Find<TextBlock>("SubscriptionPreviewInfo").Text.Contains("1"), "Preview rows and Go-computed counts are rendered");
        foreach (var scroll in Descendants(window).OfType<ScrollViewer>().Where(s => s.Content is Grid)) scroll.ScrollToEnd();
        Pump(); Capture(window, "subscriptions-review.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump(); Capture(window, "subscriptions-review-minimum.png");
        window.Width = 1240; window.Height = 850; Pump();
        Click("CancelSubscriptionPreviewButton"); Wait(() => pendingField.GetValue(window) == null && Find<Button>("RefreshSubscriptionButton").IsEnabled);
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Preview rendering and Cancel do not change storage");
        pendingField.SetValue(window, review);
        typeof(MainWindow).GetMethod("PresentSubscriptionPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [review]);
        Wait(() => Find<Button>("ApplySubscriptionButton").IsEnabled);
        Click("ApplySubscriptionButton"); Wait(() => pendingField.GetValue(window) == null && Find<Button>("RefreshSubscriptionButton").IsEnabled);
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Invalid-token Apply through IPC preserves storage and permits a fresh preview");
        Assert(Find<TextBox>("NameBox").Text == "Synthetic unfinished draft", "Subscription review preserves the editor draft");
        var projectedDocument = new ProfileDocument(1, Storage.Load(), Storage.CollapsedGroups.ToHashSet(), new());
        typeof(MainWindow).GetMethod("PresentSubscriptionApplied", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window,
            [new AppliedProfiles(projectedDocument, null, 1, 1, 1, 0, 0, true, false, false)]);
        Assert(Find<TextBox>("NameBox").Text == "Synthetic unfinished draft", "Applied-subscription presentation preserves the editor draft");
        Panel("activity"); Panel("connection");
        Click("EditButton"); Pump();
        Assert(Find<TextBox>("NameBox").Text == "Synthetic unfinished draft", "Editing another profile preserves the draft");
        typeof(MainWindow).GetMethod("CancelEditor_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
        Assert(Find<TextBox>("SourceBox").Text.Length == 0 && Find<TextBox>("NameBox").Text.Length == 0, "Explicit cancel clears private editor input");
        var groupExpander = Descendants(window).OfType<Expander>().First(e => e.DataContext is System.Windows.Data.CollectionViewGroup);
        string groupName = ((System.Windows.Data.CollectionViewGroup)groupExpander.DataContext).Name.ToString()!;
        groupExpander.IsExpanded = false; Pump();
        Assert(Storage.CollapsedGroups.Contains(groupName), "Group collapse persists through Go");
        var collapsedHash = SHA256.HashData(File.ReadAllBytes(profileFile));
        search.Text = "no-synthetic-match"; Pump(); search.Clear(); Pump();
        Assert(collapsedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Rebuilding filtered groups preserves stored expansion state");
        // Successful import must select its result even when the previous query excluded it.
        search.Text = "no-synthetic-match"; Pump();
        var imported = ProfileBackend.SaveEditorAsync("Synthetic imported profile", "socks5://192.0.2.4:1080", "Imported", null, transport, new()).GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentApplied", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [imported]);
        Wait(() => Find<Button>("EditButton").IsEnabled && LibraryIdle());
        Assert(search.Text.Length == 0 && ((Profile)list.SelectedItem).Id == imported.Selected!.Id, "Import clears stale search and selects the imported profile");
        var tray = (TrayShell?)typeof(MainWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
        Assert(tray != null, "Tray initialization coexists with WPF on the STA dispatcher");
        window.ShowInTaskbar = true;
        var notifications = Find<CheckBox>("TrayNotificationsBox");
        Assert(notifications.IsEnabled && notifications.IsChecked == false && !tray!.NotificationsItem.Checked, "Visible notification setting starts off for this session");
        var trayStorageBefore = SHA256.HashData(File.ReadAllBytes(profileFile));
        notifications.IsChecked = true;
        Assert(tray!.NotificationsItem.Checked, "Window notification setting updates the tray without replaying status");
        tray.NotificationsItem.PerformClick();
        Assert(notifications.IsChecked == false, "Tray notification setting updates the window");
        tray.NotificationsItem.PerformClick();
        Assert(notifications.IsChecked == true, "Tray notification opt-in remains synchronized");
        notifications.IsChecked = false;
        Assert(!tray.NotificationsItem.Checked, "Window notification opt-out updates the tray");
        CheckTrayWindowControls(window, Pump, () => Click("HideToTrayButton"));
        Assert(trayStorageBefore.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Tray visibility and notification controls do not write profiles or settings");
        window.ShowInTaskbar = true;
        typeof(MainWindow).GetMethod("MinimizeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Assert(window.WindowState == WindowState.Minimized && window.ShowInTaskbar && window.IsVisible, "Tray minimization retains the taskbar recovery path");
        typeof(MainWindow).GetMethod("RestoreWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Assert(window.WindowState == WindowState.Normal && window.ShowInTaskbar && window.IsVisible, "Tray restore shows the existing window without connecting");
        typeof(MainWindow).GetMethod("MinimizeWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        tray!.Present(new(Status: "Synthetic navigation check"), new(CanRecover: true));
        tray.RecoverItem.PerformClick();
        Assert(window.WindowState == WindowState.Normal && Find<StackPanel>("EnginesPanel").Visibility == Visibility.Visible, "Tray recovery restores the window and only opens the existing view");
        Panel("connection");
        window.ShowInTaskbar = false;
        Assert(Storage.Load().Count == 4, "Tray window actions preserve profile storage");
        Wait(LibraryIdle);
        var storedIDs = Storage.Load().Select(p => p.Id).ToArray();
        var libraryBefore = Raw<JsonElement>("store.load").GetProperty("Profiles").GetRawText();
        var sortBox = Find<ComboBox>("LibrarySortBox");
        var favoritesBox = Find<CheckBox>("FavoritesOnlyBox");
        Assert(sortBox.SelectedIndex == 0 && favoritesBox.IsChecked == false && Storage.FavoriteIDs.Count == 0, "Legacy store opens with stored order and no favorites");
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id);
        Wait(() => Find<Button>("FavoriteButton").IsEnabled);
        Click("FavoriteButton"); Wait(LibraryIdle);
        Assert(Storage.FavoriteIDs.SetEquals([imported.Selected!.Id]), "Favorite toggle persists the selected ID through Go");
        Assert(Raw<JsonElement>("store.load").GetProperty("Profiles").GetRawText() == libraryBefore, "Favorite metadata preserves profiles, transport and reliability settings");
        favoritesBox.IsChecked = true; Wait(LibraryIdle);
        Assert(list.Items.Count == 1 && ((Profile)list.Items[0]).Id == imported.Selected!.Id, "Favorites filter renders the Go membership projection");
        Assert(((Profile?)list.SelectedItem)?.Id == imported.Selected!.Id, "Filtering retains the selected profile while it remains visible");
        string draftBeforeLibrary = Find<TextBox>("NameBox").Text;
        search.Text = "no-synthetic-match"; Pump(); Assert(list.Items.Count == 0, "Local search combines with the favorites projection");
        search.Clear(); Pump(); Assert(list.Items.Count == 1, "Clearing search restores the favorite without another library request");
        Capture(window, "library-favorites.png");
        favoritesBox.IsChecked = false; Wait(LibraryIdle);
        sortBox.SelectedIndex = 1; sortBox.SelectedIndex = 2; sortBox.SelectedIndex = 0; Wait(LibraryIdle);
        Assert(list.Items.OfType<Profile>().Select(p => p.Id).SequenceEqual(storedIDs), "Rapid sort changes cannot replace the final stored-order projection");
        sortBox.SelectedIndex = 1; Wait(LibraryIdle);
        var nameProjection = ProfileBackend.LibraryAsync("name", false).GetAwaiter().GetResult();
        bool GroupOrderMatches(List<Guid> ids) { var ranks = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index); return list.Items.OfType<Profile>().GroupBy(p => p.Group).All(group => group.Select(p => p.Id).SequenceEqual(group.OrderBy(p => ranks[p.Id]).Select(p => p.Id))); }
        Assert(GroupOrderMatches(nameProjection.IDs), "Name sort renders Go order within contiguous groups");
        sortBox.SelectedIndex = 2; Wait(LibraryIdle);
        var protocolProjection = ProfileBackend.LibraryAsync("protocol", false).GetAwaiter().GetResult();
        Assert(GroupOrderMatches(protocolProjection.IDs), "Protocol sort renders Go order within contiguous groups");
        Assert(Storage.Load().Select(p => p.Id).SequenceEqual(storedIDs), "Display sorting never reorders stored fallback candidates");
        Assert(Find<TextBox>("NameBox").Text == draftBeforeLibrary, "Library controls preserve the editor draft");
        var libraryBytes = File.ReadAllBytes(profileFile);
        var visibleBeforeFailure = list.Items.OfType<Profile>().Select(p => p.Id).ToArray();
        try {
            File.WriteAllText(profileFile, "SYNTHETIC-LIBRARY-READ-FAILURE");
            favoritesBox.IsChecked = true; Wait(LibraryIdle);
            Assert(favoritesBox.IsChecked == false && list.Items.OfType<Profile>().Select(p => p.Id).SequenceEqual(visibleBeforeFailure), "Projection failure retains the prior view and restores its controls");
            Assert(Find<TextBlock>("LibraryStatus").Text.Length > 0, "Projection failure is visible and permits retry");
            list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id);
            Wait(() => Find<Button>("FavoriteButton").IsEnabled);
            Click("FavoriteButton"); Wait(LibraryIdle);
            Assert(Storage.FavoriteIDs.Contains(imported.Selected!.Id), "Failed favorite mutation retains the last confirmed visible state");
            Assert(File.ReadAllText(profileFile) == "SYNTHETIC-LIBRARY-READ-FAILURE", "Failure paths do not rewrite corrupt synthetic storage");
        }
        finally { File.WriteAllBytes(profileFile, libraryBytes); }
        favoritesBox.IsChecked = true; Wait(LibraryIdle);
        Assert(list.Items.Count == 1, "Filter retries successfully after the storage error is resolved");
        Capture(window, "library-filter-retry.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump(); Capture(window, "library-minimum.png");
        window.Width = 1240; window.Height = 850;
        Assert(libraryBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Filter, sort, search and retry do not write profiles");
        Assert(ProfileBackend.DiagnosticsAsync().GetAwaiter().GetResult().IndexOf("FavoriteIDs", StringComparison.OrdinalIgnoreCase) < 0, "Diagnostics still excludes favorite metadata");
        list.SelectedItem = list.Items[0]; Wait(() => Find<Button>("FavoriteButton").IsEnabled);
        Click("FavoriteButton"); Wait(LibraryIdle);
        Assert(list.Items.Count == 0 && Storage.FavoriteIDs.Count == 0, "Removing the last favorite produces the favorites empty state");
        favoritesBox.IsChecked = false; Wait(LibraryIdle);
        sortBox.SelectedIndex = 0; Wait(LibraryIdle);
        var requestedID = storedIDs[0];
        var newerSelectionID = storedIDs[1];
        var currentDocument = ProfileBackend.LoadDocumentAsync().GetAwaiter().GetResult();
        var hostGate = (SemaphoreSlim)typeof(ProfileBackend).Assembly.GetType("OneTunnel.Core.ProfileHost")!.GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert(hostGate.Wait(2000), "Test can delay a local library projection without networking");
        try {
            typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [currentDocument, requestedID, true]);
            list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == newerSelectionID);
        }
        finally { hostGate.Release(); }
        Wait(LibraryIdle);
        Assert(((Profile?)list.SelectedItem)?.Id == newerSelectionID, "Delayed projection cannot override a newer user selection");
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id);
        Wait(() => Find<Button>("FavoriteButton").IsEnabled);
        Click("FavoriteButton"); Wait(LibraryIdle);
        bool GroupsIdle() => LibraryIdle() && !(bool)typeof(MainWindow).GetField("groupsPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("groupMutationPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var allGroupNames = Storage.Load().Select(p => p.Group).Distinct().ToHashSet();
        favoritesBox.IsChecked = true; Wait(LibraryIdle);
        Wait(() => Find<Button>("CollapseAllGroupsButton").IsEnabled);
        Click("CollapseAllGroupsButton"); Wait(GroupsIdle);
        Assert(Storage.CollapsedGroups.SetEquals(allGroupNames), "Collapse-all includes groups hidden by favorites");
        Assert(Descendants(window).OfType<Expander>().Where(e => e.DataContext is System.Windows.Data.CollectionViewGroup).All(e => !e.IsExpanded), "Bulk collapse updates realized group expanders");
        var collapsedGroupBytes = File.ReadAllBytes(profileFile);
        Wait(() => Find<Button>("CollapseAllGroupsButton").IsEnabled);
        Click("CollapseAllGroupsButton"); Wait(GroupsIdle); Pump();
        Assert(collapsedGroupBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Repeated bulk collapse causes no store rewrite or expander callback cascade");
        Wait(() => Find<Button>("ExpandAllGroupsButton").IsEnabled);
        Click("ExpandAllGroupsButton"); Wait(GroupsIdle);
        Assert(Storage.CollapsedGroups.Count == 0, "Expand-all clears persisted collapse state for hidden groups too");
        Assert(Descendants(window).OfType<Expander>().Where(e => e.DataContext is System.Windows.Data.CollectionViewGroup).All(e => e.IsExpanded), "Bulk expand updates realized group expanders");
        var expandedGroupBytes = File.ReadAllBytes(profileFile);
        Wait(() => Find<Button>("ExpandAllGroupsButton").IsEnabled);
        Click("ExpandAllGroupsButton"); Wait(GroupsIdle); Pump();
        Assert(expandedGroupBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Repeated expand-all is byte-preserving");
        Wait(() => Find<Button>("CollapseAllGroupsButton").IsEnabled);
        Click("CollapseAllGroupsButton"); Wait(GroupsIdle);
        favoritesBox.IsChecked = false; Wait(LibraryIdle);
        Click("GroupsNavigationButton");
        Wait(() => GroupsIdle() && Find<ListBox>("ConfigurationGroupsList").Items.Count == 3 && Find<Button>("RenameGroupButton").IsEnabled);
        var groupsList = Find<ListBox>("ConfigurationGroupsList");
        Assert(Find<StackPanel>("GroupsPanel").IsVisible && groupsList.Items.OfType<GroupSummary>().Count(g => g.IsSubscription) == 1, "Groups page includes manual groups and saved subscriptions");
        Assert(!JsonSerializer.Serialize(ProfileBackend.GroupsAsync().GetAwaiter().GetResult()).Contains("subscription.example"), "Group summaries omit subscription URLs");
        var oldSubscriptionGroup = groupsList.Items.OfType<GroupSummary>().First(g => g.IsSubscription);
        groupsList.SelectedItem = oldSubscriptionGroup;
        const string renamedGroup = "🇳🇱 Renamed synthetic subscription";
        Find<TextBox>("RenameGroupBox").Text = renamedGroup;
        string groupDraft = Find<TextBox>("NameBox").Text;
        var documentBeforeGroups = Raw<JsonElement>("store.load");
        Click("RenameGroupButton"); Wait(GroupsIdle);
        var renamedDocument = Raw<JsonElement>("store.load");
        Assert(groupsList.Items.OfType<GroupSummary>().Any(g => g.Name == renamedGroup && g.IsSubscription), "Go rename refreshes the all-groups summary");
        Assert(renamedDocument.GetProperty("SubscriptionSources").GetProperty(renamedGroup).GetString() == documentBeforeGroups.GetProperty("SubscriptionSources").GetProperty(oldSubscriptionGroup.Name).GetString(), "Rename preserves the private subscription source under the new key");
        Assert(renamedDocument.GetProperty("SubscriptionMetadata").GetProperty(renamedGroup).GetRawText() == documentBeforeGroups.GetProperty("SubscriptionMetadata").GetProperty(oldSubscriptionGroup.Name).GetRawText(), "Rename preserves provider metadata");
        Assert(Storage.CollapsedGroups.Contains(renamedGroup) && !Storage.CollapsedGroups.Contains(oldSubscriptionGroup.Name) && Storage.FavoriteIDs.SetEquals([imported.Selected!.Id]), "Rename preserves collapsed state and favorites");
        Assert(Storage.Load().Select(p => p.Id).SequenceEqual(storedIDs) && Find<TextBox>("NameBox").Text == groupDraft, "Rename retains IDs, order and unfinished editor draft");
        Capture(window, "groups-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        foreach (var scroll in Descendants(window).OfType<ScrollViewer>().Where(s => s.Content is Grid)) scroll.ScrollToEnd();
        Pump(); Capture(window, "groups-minimum.png");
        window.Width = 1240; window.Height = 850;
        var renameBytes = File.ReadAllBytes(profileFile);
        Find<TextBox>("RenameGroupBox").Text = "Work profiles";
        Wait(() => Find<Button>("RenameGroupButton").IsEnabled);
        Click("RenameGroupButton"); Wait(GroupsIdle);
        Assert(renameBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Rename collision is rejected without storage writes");
        var staleDeleteGroup = groupsList.Items.OfType<GroupSummary>().First(g => g.Name == renamedGroup);
        ProfileBackend.SetAllGroupsExpandedAsync(true).GetAwaiter().GetResult();
        var externallyChangedBytes = File.ReadAllBytes(profileFile);
        Task DeleteConfirmed(GroupSummary group) => (Task)typeof(MainWindow).GetMethod("DeleteSelectedGroupAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [group])!;
        Wait(() => Find<Button>("DeleteGroupButton").IsEnabled);
        var staleDeletion = DeleteConfirmed(staleDeleteGroup); Wait(() => staleDeletion.IsCompleted && GroupsIdle()); staleDeletion.GetAwaiter().GetResult();
        Assert(externallyChangedBytes.SequenceEqual(File.ReadAllBytes(profileFile)) && Storage.Load().Count == 4, "Stale confirmation cannot delete a changed group snapshot");
        Click("ReloadGroupsButton"); Wait(GroupsIdle);
        var confirmedGroup = groupsList.Items.OfType<GroupSummary>().First(g => g.Name == renamedGroup);
        Wait(() => Find<Button>("DeleteGroupButton").IsEnabled);
        var deletion = DeleteConfirmed(confirmedGroup); Wait(() => deletion.IsCompleted && GroupsIdle()); deletion.GetAwaiter().GetResult();
        var afterGroupDeletion = Raw<JsonElement>("store.load");
        Assert(Storage.Load().Count == 2 && groupsList.Items.Count == 2 && !afterGroupDeletion.GetProperty("SubscriptionSources").TryGetProperty(renamedGroup, out _), "Confirmed deletion removes only the chosen group and its local refresh source");
        Assert((!afterGroupDeletion.TryGetProperty("SubscriptionMetadata", out var remainingMetadata) || !remainingMetadata.TryGetProperty(renamedGroup, out _)) && Storage.FavoriteIDs.Contains(imported.Selected!.Id), "Group deletion removes provider metadata and preserves unrelated favorites");
        var validGroupBytes = File.ReadAllBytes(profileFile);
        try {
            File.WriteAllText(profileFile, "SYNTHETIC-GROUP-STORE-FAILURE");
            Find<TextBox>("RenameGroupBox").Text = "Rejected rename";
            Wait(() => Find<Button>("RenameGroupButton").IsEnabled);
            Click("RenameGroupButton"); Wait(GroupsIdle);
            Assert(File.ReadAllText(profileFile) == "SYNTHETIC-GROUP-STORE-FAILURE" && groupsList.Items.Count == 2, "Failed group mutation preserves the confirmed view and corrupt test file");
        }
        finally { File.WriteAllBytes(profileFile, validGroupBytes); }
        Click("ReloadGroupsButton"); Wait(GroupsIdle);
        busyField.SetValue(window, true);
        typeof(MainWindow).GetMethod("RefreshControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Wait(() => !Find<Button>("RenameGroupButton").IsEnabled && !Find<Button>("DeleteGroupButton").IsEnabled);
        Assert(validGroupBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Unavailable group actions leave storage untouched");
        busyField.SetValue(window, false);
        typeof(MainWindow).GetMethod("RefreshControls", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Wait(() => Find<Button>("RenameGroupButton").IsEnabled);
        var resultBytes = File.ReadAllBytes(profileFile);
        var emptyResults = ProfileBackend.GroupResultsAsync("stored", "all").GetAwaiter().GetResult();
        Assert(!emptyResults.HasResults && !emptyResults.CanSelectFastest && emptyResults.Rows.Count == 0, "Actual result IPC starts empty without probing servers");
        void RenderResults(GroupResultsView value) => typeof(MainWindow).GetMethod("PresentGroupResults", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [value]);
        bool ResultsIdle() => !(bool)typeof(MainWindow).GetField("groupResultsPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("fastestSelectionPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var targetResult = Storage.Load().First(p => p.Group == "Work profiles");
        var resultRows = new List<GroupResultDisplayRow> {
            new(targetResult.Id.ToString(), "Synthetic network result", "Available", true, true, 12, "12 ms check duration"),
            new(Guid.NewGuid().ToString(), "Synthetic configuration result", "Configuration valid", true, false, 0, "Configuration only · no network check")
        };
        var syntheticResults = new GroupResultsView("Synthetic results", "2026-10-23", resultRows, true, false, true, "synthetic-unretained-token", "");
        Panel("connection");
        RenderResults(syntheticResults); Pump();
        Assert(Find<ItemsControl>("GroupResults").Items.Cast<GroupResultDisplayRow>().SequenceEqual(resultRows), "Result rendering preserves Go-owned row order and timing labels");
        Assert(Find<Button>("SelectFastestResultButton").IsEnabled, "Eligible Go view enables explicit selection");
        Capture(window, "group-results-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Assert(Find<ComboBox>("GroupResultSortBox").ActualWidth > 100 && Find<Button>("ClearGroupResultsButton").ActualHeight > 10, "Result controls retain usable size at minimum window dimensions");
        Capture(window, "group-results-minimum.png");
        window.Width = 1240; window.Height = 850; Pump();
        RenderResults(syntheticResults with { Stale = true, CanSelectFastest = false, Message = "Synthetic stale snapshot" });
        Assert(!Find<Button>("SelectFastestResultButton").IsEnabled, "Stale Go view disables best selection");
        RenderResults(syntheticResults);
        Find<ComboBox>("GroupResultSortBox").SelectedIndex = 1; Wait(ResultsIdle);
        Assert(Find<ItemsControl>("GroupResults").Items.Count == 0 && !Find<Button>("SelectFastestResultButton").IsEnabled, "Sort refresh uses real empty-cache IPC and drops stale display rows");
        RenderResults(syntheticResults);
        Find<ComboBox>("GroupResultFilterBox").SelectedIndex = 2; Wait(ResultsIdle);
        Assert(Find<ItemsControl>("GroupResults").Items.Count == 0, "Filter refresh uses Go results rather than local classification");
        RenderResults(syntheticResults);
        Click("SelectFastestResultButton"); Wait(ResultsIdle);
        Assert(!Find<Button>("SelectFastestResultButton").IsEnabled && Find<ItemsControl>("GroupResults").Items.Count == 0, "Unretained result token clears misleading selection without connecting");
        Find<CheckBox>("FavoritesOnlyBox").IsChecked = true; Wait(LibraryIdle);
        search.Text = "Imported"; Pump();
        var targetSelection = (Task)typeof(MainWindow).GetMethod("PresentFastestSelectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [targetResult.Id])!;
        Wait(() => targetSelection.IsCompleted && LibraryIdle()); targetSelection.GetAwaiter().GetResult();
        Assert(list.SelectedItem is Profile resultSelected && resultSelected.Id == targetResult.Id && search.Text == "" && Find<CheckBox>("FavoritesOnlyBox").IsChecked == false, "Explicit result selection reveals and selects the exact hidden profile");
        Find<CheckBox>("FavoritesOnlyBox").IsChecked = true; Wait(LibraryIdle);
        var connectionObject = typeof(MainWindow).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var connectionSnapshot = connectionObject.GetType().GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var retainedSnapshot = (DesktopReply)connectionSnapshot.GetValue(connectionObject)!;
        var fastestFlag = typeof(MainWindow).GetField("fastestSelectionPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert(hostGate.Wait(2000), "Test can delay best-result visibility projection without networking");
        Task policySelection;
        try {
            fastestFlag.SetValue(window, true);
            policySelection = (Task)typeof(MainWindow).GetMethod("PresentFastestSelectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [targetResult.Id])!;
            connectionSnapshot.SetValue(connectionObject, retainedSnapshot with { Actions = retainedSnapshot.Actions! with { CanSelect = false } });
        }
        finally { hostGate.Release(); }
        Wait(() => policySelection.IsCompleted && LibraryIdle()); policySelection.GetAwaiter().GetResult();
        Assert(((Profile?)list.SelectedItem)?.Id != targetResult.Id, "A newly unavailable Go selection policy blocks pending best-result selection");
        connectionSnapshot.SetValue(connectionObject, retainedSnapshot);
        fastestFlag.SetValue(window, false);
        var selectedBeforeMissing = list.SelectedItem;
        var missingSelection = (Task)typeof(MainWindow).GetMethod("PresentFastestSelectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Guid.NewGuid()])!;
        Wait(() => missingSelection.IsCompleted);
        Assert(missingSelection.IsFaulted && ReferenceEquals(list.SelectedItem, selectedBeforeMissing), "Missing result ID cannot select an unrelated first row");
        _ = missingSelection.Exception;
        RenderResults(syntheticResults); Click("ClearGroupResultsButton"); Wait(ResultsIdle);
        Assert(Find<ItemsControl>("GroupResults").Items.Count == 0 && !Find<Button>("SelectFastestResultButton").IsEnabled, "Explicit clear resets result presentation through real IPC");
        Assert(resultBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "All result tools leave encrypted settings and profile order unchanged");
        bool OrganizationIdle() => !(bool)typeof(MainWindow).GetField("profileOrganizationPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("profileOrganizationMutationPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        search.Clear(); favoritesBox.IsChecked = false; Wait(LibraryIdle);
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id);
        Find<Expander>("ProfileOrganizationPanel").IsExpanded = true;
        Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        var organizationDraft = Find<TextBox>("NameBox").Text;
        var originalOrganizationProfile = Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == targetResult.Id.ToString());
        Find<TextBox>("ProfileOrganizationNameBox").Text = "Synthetic local copy";
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Local test copies";
        Click("DuplicateProfileButton");
        Wait(() => OrganizationIdle() && LibraryIdle() && list.Items.Count == 3 && Find<Button>("MoveProfileButton").IsEnabled);
        var copiedOrganizationProfile = list.Items.OfType<Profile>().Single(p => p.Name == "Synthetic local copy");
        var organizationRaw = Raw<JsonElement>("store.load");
        var copiedRaw = organizationRaw.GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == copiedOrganizationProfile.Id.ToString());
        Assert(copiedOrganizationProfile.Id != targetResult.Id && ((Profile?)list.SelectedItem)?.Id == copiedOrganizationProfile.Id && copiedOrganizationProfile.Group == "Local test copies", "Copy creates and selects a new profile in the requested manual group");
        Assert(copiedRaw.GetProperty("Source").GetString() == originalOrganizationProfile.GetProperty("Source").GetString() && copiedRaw.GetProperty("Protocol").GetInt32() == originalOrganizationProfile.GetProperty("Protocol").GetInt32() && !Storage.FavoriteIDs.Contains(copiedOrganizationProfile.Id), "Copy retains protocol/source and starts without a favorite");
        Assert(Find<TextBox>("NameBox").Text == organizationDraft, "Profile organization preserves the unfinished editor draft");
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Work profiles";
        Click("MoveProfileButton");
        Wait(() => OrganizationIdle() && LibraryIdle() && list.Items.OfType<Profile>().Any(p => p.Id == copiedOrganizationProfile.Id && p.Group == "Work profiles") && Find<Button>("MoveProfileButton").IsEnabled);
        Assert(Storage.Load().Select(p => p.Id).SequenceEqual(new[] { targetResult.Id, imported.Selected!.Id, copiedOrganizationProfile.Id }), "Move retains profile IDs and the stored fallback order");
        var organizationNoOpBytes = File.ReadAllBytes(profileFile);
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Work profiles"; Click("MoveProfileButton");
        Wait(() => OrganizationIdle() && LibraryIdle() && Find<Button>("MoveProfileButton").IsEnabled);
        Assert(organizationNoOpBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Moving to the current group avoids rewriting encrypted storage");
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Invalid\nGroup"; Click("MoveProfileButton"); Wait(OrganizationIdle);
        Assert(organizationNoOpBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Rejected organization input preserves encrypted settings");
        Click("ReloadProfileOrganizationButton"); Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        Find<TextBox>("ProfileOrganizationNameBox").Text = "Unfinished copy name";
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Unfinished manual group";
        Click("ReloadProfileOrganizationButton"); Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        Assert(Find<TextBox>("ProfileOrganizationNameBox").Text == "Unfinished copy name" && Find<ComboBox>("ProfileOrganizationGroupBox").Text == "Unfinished manual group", "Same-snapshot metadata refresh preserves typed organization fields");
        var organizationValidBytes = File.ReadAllBytes(profileFile);
        try {
            File.WriteAllText(profileFile, "SYNTHETIC-ORGANIZATION-READ-FAILURE");
            Click("ReloadProfileOrganizationButton"); Wait(OrganizationIdle);
            Assert(!Find<Button>("DuplicateProfileButton").IsEnabled && !Find<Button>("MoveProfileButton").IsEnabled && !Find<TextBlock>("ProfileOrganizationStatus").Text.Contains("SYNTHETIC"), "Metadata failure disables stale actions without exposing store errors");
            Assert(File.ReadAllText(profileFile) == "SYNTHETIC-ORGANIZATION-READ-FAILURE", "Failed metadata load does not overwrite corrupt synthetic storage");
        }
        finally { File.WriteAllBytes(profileFile, organizationValidBytes); }
        Click("ReloadProfileOrganizationButton"); Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        Assert(hostGate.Wait(2000), "Test can delay profile metadata without networking");
        try {
            list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id);
            list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id);
        }
        finally { hostGate.Release(); }
        Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        var renderedOrganization = (ProfileOrganizationView)typeof(MainWindow).GetField("profileOrganizationView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert(renderedOrganization.ID == targetResult.Id.ToString(), "Delayed metadata cannot overwrite the newer selected profile");
        var managedDocument = JsonNode.Parse(Raw<JsonElement>("store.load").GetRawText())!.AsObject();
        managedDocument["SubscriptionSources"]!["Imported"] = "https://subscription.example/synthetic-organization-only";
        Raw<object?>("store.save", new { Document = managedDocument });
        var managedPresentation = ProfileBackend.LoadDocumentAsync().GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [managedPresentation, imported.Selected!.Id, true]);
        Wait(() => OrganizationIdle() && LibraryIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        Assert(!Find<Button>("MoveProfileButton").IsEnabled, "Go-managed subscription member disables moving while retaining local copy action");
        Capture(window, "profile-organization-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Assert(Find<TextBox>("ProfileOrganizationNameBox").ActualWidth > 100 && Find<ComboBox>("ProfileOrganizationGroupBox").ActualWidth > 100, "Profile organization inputs retain usable minimum-size layout");
        Capture(window, "profile-organization-minimum.png");
        window.Width = 1240; window.Height = 850; Pump();
        var beforeStaleOrganization = File.ReadAllBytes(profileFile);
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = true });
        var afterExternalOrganization = File.ReadAllBytes(profileFile);
        Find<TextBox>("ProfileOrganizationNameBox").Text = "Stale copy must fail";
        Find<ComboBox>("ProfileOrganizationGroupBox").Text = "Local copies"; Click("DuplicateProfileButton"); Wait(OrganizationIdle);
        Assert(afterExternalOrganization.SequenceEqual(File.ReadAllBytes(profileFile)) && Storage.Load().Count == 3, "Changed-store organization token cannot create an unreviewed copy");
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = false });
        Click("ReloadProfileOrganizationButton"); Wait(() => OrganizationIdle() && Find<Button>("DuplicateProfileButton").IsEnabled);
        Assert(Find<TextBox>("NameBox").Text == organizationDraft && Raw<JsonElement>("store.load").GetProperty("FavoriteIDs").GetArrayLength() == 1, "Organization failures and reload preserve drafts and unrelated favorites");
        bool NetworkIdle() => !(bool)typeof(MainWindow).GetField("networkPreferencesPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("networkPreferencesSavePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        search.Clear(); favoritesBox.IsChecked = false; Wait(LibraryIdle);
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id);
        Find<Expander>("NetworkPreferencesPanel").IsExpanded = true;
        Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var networkDraft = Find<TextBox>("NameBox").Text;
        var networkOriginal = Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == targetResult.Id.ToString());
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 1;
        Find<TextBox>("NetworkServerIPBox").Text = "9.9.9.9";
        Find<TextBox>("NetworkServerNameBox").Text = "dns.quad9.net";
        Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Clear();
        Find<TextBox>("NetworkRulesBox").Text = "domain-suffix | proxy | example.com\ncidr | direct | 192.168.1.0/24\nprocess | reject | C:\\Synthetic\\blocked.exe";
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        JsonElement NetworkProfile() => Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == targetResult.Id.ToString());
        var networkSaved = NetworkProfile(); var networkPolicy = networkSaved.GetProperty("Network");
        Assert(networkPolicy.GetProperty("DNS").GetProperty("Transport").GetString() == "https" && networkPolicy.GetProperty("DNS").GetProperty("Port").GetInt32() == 443 && networkPolicy.GetProperty("Rules").GetArrayLength() == 3, "DNS and split rules save through Go with canonical defaults");
        Assert(networkSaved.GetProperty("Source").GetString() == networkOriginal.GetProperty("Source").GetString() && Find<TextBox>("NameBox").Text == networkDraft, "Network preference save preserves profile source and unfinished editor draft");
        var canonicalRules = Find<TextBox>("NetworkRulesBox").Text;
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(Find<TextBox>("NetworkRulesBox").Text == canonicalRules && Find<TextBox>("NetworkServerIPBox").Text == "9.9.9.9", "Saved network preferences round-trip through real metadata IPC");
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 2;
        Find<TextBox>("NetworkServerIPBox").Text = "1.1.1.1";
        Find<TextBox>("NetworkServerNameBox").Text = "one.one.one.one";
        Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Clear();
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var dot = NetworkProfile().GetProperty("Network").GetProperty("DNS");
        Assert(dot.GetProperty("Transport").GetString() == "tls" && dot.GetProperty("Port").GetInt32() == 853, "DoT uses the Go default TLS port and preserves split rules");
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 0;
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var rulesOnlyPolicy = NetworkProfile().GetProperty("Network");
        Assert(rulesOnlyPolicy.GetProperty("DNS").ValueKind == JsonValueKind.Null && rulesOnlyPolicy.GetProperty("Rules").GetArrayLength() == 3, "DNS default selector clears disabled custom fields while preserving split rules");
        var validNetworkBytes = File.ReadAllBytes(profileFile);
        Find<TextBox>("NetworkRulesBox").Text = "cidr | direct | invalid-subnet"; Click("SaveNetworkPreferencesButton"); Wait(NetworkIdle);
        Assert(validNetworkBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Invalid split routing cannot change encrypted storage");
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var beforeExternalNetwork = File.ReadAllBytes(profileFile);
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = true });
        var externallyChangedNetwork = File.ReadAllBytes(profileFile);
        Find<TextBox>("NetworkRulesBox").Text = "domain | reject | example.org"; Click("SaveNetworkPreferencesButton"); Wait(NetworkIdle);
        Assert(externallyChangedNetwork.SequenceEqual(File.ReadAllBytes(profileFile)), "Stale network preference token cannot overwrite a newer store");
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = false });
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 0; Find<TextBox>("NetworkRulesBox").Clear();
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(!NetworkProfile().TryGetProperty("Network", out var clearedNetwork) || clearedNetwork.ValueKind == JsonValueKind.Null, "Explicit default reset removes the custom network policy");
        var nativeFixture = "[Interface]\nPrivateKey = AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=\nAddress = 10.9.0.2/32\nDNS = 1.1.1.1\n[Peer]\nPublicKey = AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=\nEndpoint = 192.0.2.9:51820\nAllowedIPs = 0.0.0.0/0";
        var nativeAdded = ProfileBackend.SaveEditorAsync("Synthetic native", nativeFixture, "Native test", null, transport).GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [nativeAdded.Document, nativeAdded.Selected!.Id, true]);
        Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("ReloadNetworkPreferencesButton").IsEnabled);
        Assert(!Find<Button>("SaveNetworkPreferencesButton").IsEnabled && Find<TextBlock>("NetworkPreferencesStatus").Text.Length > 0, "Native profile explicitly disables unsupported shared-TUN network policy");
        var finalNetworkDocument = ProfileBackend.LoadDocumentAsync().GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [finalNetworkDocument, targetResult.Id, true]);
        Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(hostGate.Wait(2000), "Test can delay network metadata without networking");
        try { list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id); list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id); }
        finally { hostGate.Release(); }
        Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var renderedNetwork = (NetworkPreferencesView)typeof(MainWindow).GetField("networkPreferencesView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert(renderedNetwork.ID == targetResult.Id.ToString() && Find<TextBox>("NameBox").Text == networkDraft, "Delayed network metadata preserves newer selection and editor draft");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Assert(Find<TextBox>("NetworkRulesBox").ActualWidth > 200 && Find<TextBox>("NetworkServerIPBox").ActualWidth > 80, "Network editor retains usable controls at minimum window size");
        Capture(window, "dns-routing-minimum.png"); window.Width = 1240; window.Height = 850; Pump();
        typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
        Assert(tray!.StatusItem.IsDisposed && tray.ExitItem.IsDisposed, "Window Close disposes the tray even on the capture/test close path");
        CheckHiddenWindowClose();
        app.Shutdown();
        Console.WriteLine("PASS: isolated WPF profile search, empty results, navigation, existing settings, default/minimum layout and storage-preservation checks. No connect/probe/service mutation commands sent.");
        Console.WriteLine(Output);
        return 0;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static void CheckTrayWindowControls(MainWindow window, Action pump, Action hide)
    {
        const int probeId = 0x534F;
        const uint modifiers = 0x4006, key = 0x4F;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int hotkeyId = (int)typeof(MainWindow).GetField("RestoreHotkeyId", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        uint shellMessage = (uint)typeof(MainWindow).GetField("taskbarCreatedMessage", flags)!.GetValue(window)!;
        var hwnd = new WindowInteropHelper(window).Handle;
        var carrier = new Window { Width = 1, Height = 1, Left = -6000, Top = -6000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        carrier.Show();
        var probe = new WindowInteropHelper(carrier).Handle;
        bool probeRegistered = false;
        var selected = ((ListBox)window.FindName("ProfilesList")).SelectedItem;
        ((TextBox)window.FindName("NameBox")).Text = "Synthetic unsaved tray draft";
        var draft = ((TextBox)window.FindName("NameBox")).Text;
        void Restore() => typeof(MainWindow).GetMethod("RestoreWindow", flags)!.Invoke(window, null);
        try {
            Assert(shellMessage != 0 && ((Button)window.FindName("HideToTrayButton")).IsEnabled, "Native restore hook enables the explicit tray button");
            probeRegistered = RegisterHotKey(probe, probeId, modifiers, key);
            Assert(probeRegistered, "Restore shortcut remains free while the window is visible");
            hide();
            Assert(window.IsVisible && window.ShowInTaskbar, "Shortcut conflict refuses hiding and retains taskbar access");
            Assert(((TextBlock)window.FindName("Feedback")).Text.Contains("Ctrl+Shift+O"), "Shortcut conflict explains how to recover");
            UnregisterHotKey(probe, probeId); probeRegistered = false;
            hide();
            Assert(!window.IsVisible && !window.ShowInTaskbar, "Explicit tray button hides the window and taskbar entry");
            SendMessage(hwnd, 0x0312, hotkeyId + 1, 0);
            Assert(!window.IsVisible, "Unowned hotkey message does not restore the hidden window");
            SendMessage(hwnd, 0x0312, hotkeyId, 0); pump();
            Assert(window.IsVisible && window.ShowInTaskbar && window.WindowState == WindowState.Normal, "Owned hotkey restores the same window");
            Assert(ReferenceEquals(((ListBox)window.FindName("ProfilesList")).SelectedItem, selected) && ((TextBox)window.FindName("NameBox")).Text == draft, "Tray round trip preserves selection and draft");
            probeRegistered = RegisterHotKey(probe, probeId, modifiers, key);
            Assert(probeRegistered, "Restoring releases the global shortcut");
            UnregisterHotKey(probe, probeId); probeRegistered = false;
            hide();
            SendMessage(hwnd, shellMessage, 0, 0); pump();
            Assert(window.IsVisible && window.ShowInTaskbar, "Shell restart message restores an explicitly hidden window");
            typeof(MainWindow).GetMethod("MinimizeWindow", flags)!.Invoke(window, null);
            SendMessage(hwnd, shellMessage, 0, 0);
            Assert(window.IsVisible && window.ShowInTaskbar && window.WindowState == WindowState.Minimized, "Shell restart does not restore ordinary taskbar minimization");
            Restore();
            window.WindowState = WindowState.Maximized;
            hide();
            SendMessage(hwnd, 0x0312, hotkeyId, 0);
            Assert(window.WindowState == WindowState.Maximized && window.IsVisible, "Tray round trip preserves maximized state");
            window.WindowState = WindowState.Normal;
        }
        finally {
            if (probeRegistered) UnregisterHotKey(probe, probeId);
            Restore(); carrier.Close();
        }
    }

    private static void CheckHiddenWindowClose()
    {
        var window = new MainWindow { Left = -5000, Top = -5000 };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        window.Show();
        try {
            Assert(((CheckBox)window.FindName("TrayNotificationsBox")).IsChecked == false, "A new window starts with notifications disabled");
            Assert(Storage.FavoriteIDs.Count == 1 && ((ComboBox)window.FindName("LibrarySortBox")).SelectedIndex == 0 && ((CheckBox)window.FindName("FavoritesOnlyBox")).IsChecked == false, "Favorites survive a new window while view preferences reset");
            ((Button)window.FindName("HideToTrayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(!window.IsVisible, "A new session can hide using the released restore shortcut");
            typeof(MainWindow).GetField("allowClose", flags)!.SetValue(window, true);
            window.Close();
            var probe = new Window { Width = 1, Height = 1, Left = -6000, Top = -6000, ShowInTaskbar = false };
            probe.Show();
            var hwnd = new WindowInteropHelper(probe).Handle;
            bool registered = false;
            try {
                registered = RegisterHotKey(hwnd, 0x534F, 0x4006, 0x4F);
                Assert(registered, "Closing a hidden window releases the native shortcut");
            }
            finally { if (registered) UnregisterHotKey(hwnd, 0x534F); probe.Close(); }
        }
        finally {
            typeof(MainWindow).GetField("allowClose", flags)!.SetValue(window, true);
            window.Close();
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, name)); encoder.Save(stream);
    }
}
