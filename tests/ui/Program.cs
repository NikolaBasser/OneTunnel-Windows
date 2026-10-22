using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OneTunnel;
using OneTunnel.Core;
using OneTunnel.Controls;

internal static class UiTest
{
    [STAThread] public static int Main(string[] args)
    {
        if (args.Contains("--service-lifecycle")) return ServiceLifecycle();
        if (args.Contains("--service-ui-worker"))
        {
            var workerApp = CreateTestApplication();
            var workerWindow = new MainWindow { ShowInTaskbar = false, Left = -5000, Top = -5000 };
            workerWindow.Loaded += (_, _) => Console.WriteLine("UI ready");
            workerApp.Run(workerWindow); return 0;
        }
        string root = Path.Combine(Path.GetTempPath(), "OneTunnel-UiTests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR", root);
        var app = CreateTestApplication();
        var window = new MainWindow { ShowInTaskbar = false, Left = -5000, Top = -5000 };
        window.Show(); window.UpdateLayout();
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name) => Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS UI: " + label); }
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts")); Directory.CreateDirectory(artifacts);
        void Capture(string name)
        {
            window.UpdateLayout();
            var grid = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)grid.ActualWidth, (int)grid.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(grid);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(artifacts, name)); encoder.Save(file);
        }
        Assert(!Find<Button>("ConnectButton").IsEnabled, "empty state blocks connection");
        Capture("empty.png");
        // Invoke the public routed click path used by the actual sidebar button.
        var add = Descendants(window).OfType<Button>().First(b => b.Content as string == "Добавить"); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<ComboBox>("GroupBox").Text = "🇩🇪 Мои серверы";
        Find<TextBox>("NameBox").Text = "🇩🇪 Berlin 🚀";
        Find<ComboBox>("ProtocolBox").SelectedItem = Protocol.VLESS;
        Find<TextBox>("SourceBox").Text = "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:24443?encryption=none";
        Assert(Find<TextBlock>("DetectedProtocol").Text.Contains("VLESS"), "pasted VLESS recognized without protocol selection");
        Capture("editor.png");
        Assert(Find<EmojiText>("NamePreview").RenderedEmojiCount == 2, "editor previews flag and emoji as color images");
        var save = Find<Button>("SaveProfileButton");
        void Save()
        {
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var until=DateTime.UtcNow.AddSeconds(10);
            while(!save.IsEnabled && DateTime.UtcNow<until)
            {
                var frame=new System.Windows.Threading.DispatcherFrame();
                var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(10) };
                timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            if(!save.IsEnabled)throw new Exception("Go profile operation timed out");
        }
        Save();
        Assert(Storage.Load().Count == 1, "save profile through UI");
        window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Assert(Storage.Load()[0].Group == "🇩🇪 Мои серверы", "manual profile group saved");
        var groupExpander = Descendants(window).OfType<Expander>().First();
        groupExpander.IsExpanded = false;
        Assert(Storage.CollapsedGroups.Contains("🇩🇪 Мои серверы"), "collapse persists encrypted group state");
        groupExpander.IsExpanded = true;
        Assert(!Storage.CollapsedGroups.Contains("🇩🇪 Мои серверы"), "expand persists group state");
        Assert(Find<ComboBox>("ProtocolBox").Visibility == Visibility.Collapsed, "protocol selector replaced by automatic detection");
        Assert(Find<TextBox>("SourceBox").Text == "", "secret field cleared after saving");
        Assert(!Find<Button>("ConnectButton").IsEnabled, "saved profile waits for authenticated service status");
        Assert(Find<StackPanel>("ModePanel").Visibility == Visibility.Visible, "proxy mode visible for VLESS");
        Assert(Find<EmojiText>("ProfileTitle").RenderedEmojiCount == 2 && Storage.Load()[0].Name == "🇩🇪 Berlin 🚀", "heading renders color emojis and storage preserves Unicode");
        Capture("profile.png");
        Click("EditButton");
        Assert(Find<TextBox>("SourceBox").Text.StartsWith("vless://"), "edit restores configuration");
        Find<TextBox>("NameBox").Text = "🇺🇳 👩🏽‍💻 ❤️ 1️⃣"; window.UpdateLayout();
        Assert(Find<EmojiText>("NamePreview").RenderedEmojiCount == 4, "ZWJ skin-tone heart and keycap sequences stay intact");
        Assert(EmojiText.Countries().Count >= 250, "offline picker includes country and regional flags");
        Capture("emoji-preview.png");
        Assert(Find<CheckBox>("MuxCheck").IsChecked == false && Find<CheckBox>("FragmentCheck").IsChecked == false && Find<CheckBox>("NoiseCheck").IsChecked == false, "transport features default off");
        Find<Expander>("TransportExpander").IsExpanded = true;
        Find<CheckBox>("MuxCheck").IsChecked = true;
        Find<TextBox>("MuxConcurrencyBox").Text = "4";
        Find<TextBox>("NameBox").Text = "🇩🇪 Berlin 🚀";
        Find<Expander>("TransportExpander").BringIntoView(); window.UpdateLayout(); Capture("transport-options.png");
        Save();
        Assert(Storage.Load()[0].Transport is { Mux: true, MuxConcurrency: 4 }, "transport options saved encrypted through editor");
        Click("EditButton");
        Assert(Find<CheckBox>("MuxCheck").IsChecked == true && Find<TextBox>("MuxConcurrencyBox").Text == "4", "editing restores transport preferences");
        var cancel = Descendants(window).OfType<Button>().First(b => b.Content as string == "Отмена"); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Find<TextBox>("SourceBox").Text == "", "cancel clears secret field");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Find<CheckBox>("MuxCheck").IsChecked == false, "adding profile resets previous transport settings");
        string key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        Find<TextBox>("NameBox").Text = "AWG import regression";
        Find<ComboBox>("ProtocolBox").SelectedItem = Protocol.AmneziaWG;
        Find<TextBox>("SourceBox").Text = $"[Interface]\nPrivateKey = {key}\nAddress = 10.9.0.2/32\nJc = 3\n[Peer]\nPublicKey = {key}\nEndpoint = 127.0.0.1:51820\nAllowedIPs = 10.9.0.1/32";
        Save();
        Assert(Storage.Load().Count == 2 && Storage.Load()[1].Protocol == Protocol.AmneziaWG, "subscription detection preserves AWG configuration import");
        Assert(Find<StackPanel>("ModePanel").Visibility == Visibility.Visible, "native tunnels expose explicit full-device mode");
        Find<ComboBox>("ModeBox").SelectedIndex = 1;
        Assert(Find<TextBlock>("ModeHelp").Text.Contains("временно"), "native full-device mode explains temporary routes");
        Find<ComboBox>("ModeBox").SelectedIndex = 0;
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string link = "vless://6d7a9bdf-fdeb-4b9b-aa68-24abfa4bd12a@127.0.0.1:25555?type=xhttp&path=%2Ftest#Subscription";
        Find<TextBox>("SourceBox").Text = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(link));
        Save();
        Assert(Storage.Load().Count == 3, "Base64 subscription imports using default protocol selection");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Find<TextBox>("SourceBox").Text = link + "\n" + link;
        Save();
        Assert(Storage.Load().Count == 3, "repeat subscription import preserves existing profiles without duplicates");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<TextBox>("SourceBox").Text = "hy2://local-test@127.0.0.1:24445?sni=localhost#Hysteria2";
        Save();
        Assert(Storage.Load().Count == 4 && Storage.Load()[3].Protocol == Protocol.Hysteria2 && Find<StackPanel>("ModePanel").Visibility == Visibility.Visible, "Hysteria2 auto-detection and proxy mode in UI");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<TextBox>("SourceBox").Text = "ss://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("aes-128-gcm:local-test")) + "@127.0.0.1:24446#Shadowsocks";
        Save();
        Assert(Storage.Load().Count == 5 && Storage.Load()[4].Protocol == Protocol.Shadowsocks, "Shadowsocks auto-detection in UI");
        var beforeRefresh=Storage.Load();
        var original=beforeRefresh.Single(p=>p.Name=="🇩🇪 Berlin 🚀");
        Storage.CollapsedGroups.Add(original.Group);
        Storage.Save(beforeRefresh);
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<ComboBox>("GroupBox").Text=original.Group;
        Find<TextBox>("SourceBox").Text="https://subscription.example/test";
        var updated=original with{Id=Guid.NewGuid(),Source=original.Source.Replace(":24443",":24444")};
        typeof(MainWindow).GetMethod("SaveBatch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,[new ImportResult([updated],0,0)]);
        var refreshed=Storage.Load();
        Assert(refreshed.Single(p=>p.Id==original.Id).Source==updated.Source&&refreshed.Count==beforeRefresh.Count,"URL refresh updates stored configuration without duplicate row");
        Assert(beforeRefresh.Where(p=>p.Id!=original.Id).All(refreshed.Contains)&&Storage.CollapsedGroups.Contains(original.Group),"URL refresh preserves other profiles and collapsed group");
        Assert(Find<TextBlock>("Feedback").Text.Contains("Подписка обновлена"),"URL refresh reports updated subscription");
        Assert(Storage.SubscriptionSources.GetValueOrDefault(original.Group)=="https://subscription.example/test","subscription source survives encrypted storage round trip");
        var freshAgain=updated with{Source=updated.Source.Replace(":24444",":24445")};
        Find<TextBox>("SourceBox").Text="https://subscription.example/test";
        typeof(MainWindow).GetMethod("SaveBatch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,[new ImportResult([freshAgain],0,0)]);
        var final=Storage.Load().Single(p=>p.Id==original.Id);
        Assert(final.Source==freshAgain.Source&&final.Transport==original.Transport,"Go refresh preserves transport preferences and rotated source");
        Assert(typeof(Profile).Assembly.GetName().Name=="OneTunnel.Presentation","production UI references DTO and IPC assembly instead of legacy C# core");
        window.Width = 940; window.Height = 680; window.UpdateLayout(); Capture("minimum-size.png");
        var engines = Descendants(window).OfType<Button>().First(b => b.Content as string == "Движки и восстановление"); engines.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Find<StackPanel>("EnginesPanel").Visibility == Visibility.Visible, "engines page opens"); Capture("engines.png");
        window.Close(); app.Shutdown(); Console.WriteLine("ALL UI CHECKS PASSED"); return 0;
    }
    private static int ServiceLifecycle()
    {
        string root = Path.Combine(Path.GetTempPath(), "OneTunnel-ServiceUi-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ONETUNNEL_DATA_DIR", root); Storage.Initialize();
        var profile = ProfileParser.Parse("🇩🇪 Service UI", "socks5://127.0.0.1:9") with { Group = "🇩🇪 Existing group" };
        Storage.CollapsedGroups.Add(profile.Group); Storage.Save([profile]);
        byte[] before = File.ReadAllBytes(Path.Combine(root, "profiles.bin"));
        var client = new OneTunnel.Client.ServiceConnection(); client.RefreshAsync().GetAwaiter().GetResult();
        if (!client.Available || client.Active || client.NeedsRecovery) throw new Exception("Service must be idle for lifecycle test.");
        var app = CreateTestApplication();
        void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        try
        {
            client.StartAsync(profile, false).GetAwaiter().GetResult();
            for (int i = 0; i < 2; i++)
            {
                var window = new MainWindow { ShowInTaskbar = false, Left = -5000, Top = -5000 };
                window.Show(); Pump();
                if (((Button)window.FindName("ConnectButton")).Content as string != "Отключить") throw new Exception("UI did not attach to active service session.");
                window.Close(); Pump(); client.RefreshAsync().GetAwaiter().GetResult();
                if (!client.Active) throw new Exception("Closing UI disconnected service.");
            }
            Console.WriteLine("PASS SERVICE UI: closing and reopening window preserves connection");
            var info = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(typeof(UiTest).Assembly.Location); info.ArgumentList.Add("--service-ui-worker");
            using (var worker = System.Diagnostics.Process.Start(info)!)
            {
                if (worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() != "UI ready") throw new Exception("UI worker did not start.");
                worker.Kill(); worker.WaitForExit();
            }
            client.RefreshAsync().GetAwaiter().GetResult();
            if (!client.Active) throw new Exception("UI crash disconnected service.");
            Console.WriteLine("PASS SERVICE UI: forced UI process termination preserves connection");
            if (!Storage.Protect(before, false).SequenceEqual(Storage.Protect(File.ReadAllBytes(Path.Combine(root, "profiles.bin")), false)) || !Storage.Load().Any(p => p.Group == profile.Group) || !Storage.CollapsedGroups.Contains(profile.Group)) throw new Exception("Profile settings changed.");
            Console.WriteLine("PASS SERVICE UI: encrypted profiles, Unicode groups and collapse state preserved");
        }
        finally { client.StopAsync().GetAwaiter().GetResult(); app.Shutdown(); }
        return 0;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static Application CreateTestApplication()
    {
        // Use production resources without taking the user's singleton mutex or
        // automatically opening StartupUri while pumping the test dispatcher.
        using var source = typeof(UiTest).Assembly.GetManifestResourceStream("TestApp.xaml")!;
        var xml = System.Xml.Linq.XDocument.Load(source);
        System.Xml.Linq.XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(presentation + "ResourceDictionary",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            xml.Root!.Element(presentation + "Application.Resources")!.Elements());
        return new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()) };
    }
}
