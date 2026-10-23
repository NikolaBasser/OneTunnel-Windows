using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
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
        using var resource = typeof(Program).Assembly.GetManifestResourceStream("TestApp.xaml")!;
        var xml = System.Xml.Linq.XDocument.Load(resource);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(ns + "Application.Resources")!.Elements());
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()) };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
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
        foreach (var name in new[] { "AddProfileButton", "ConnectButton", "SearchBox", "ProfilesList" }) {
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
        typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close(); app.Shutdown();
        Console.WriteLine("PASS: isolated WPF profile search, empty results, navigation, existing settings, default/minimum layout and storage-preservation checks. No connect/probe/service mutation commands sent.");
        Console.WriteLine(Output);
        return 0;
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
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
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, name)); encoder.Save(stream);
    }
}
