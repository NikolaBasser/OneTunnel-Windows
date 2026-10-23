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
        Wait(() => Find<Button>("AddProfileButton").IsEnabled && Find<Button>("EditButton").IsEnabled);
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
        foreach (var name in new[] { "AddProfileButton", "ConnectButton", "SearchBox", "ProfilesList", "HideToTrayButton", "TrayNotificationsBox" }) {
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
        Wait(() => Find<Button>("EditButton").IsEnabled);
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
