using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Automation.Peers;
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
        var registryBefore = UserRegistryFingerprint();
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
		var policyWire = """{"DNS":null,"Rules":[{"Kind":"rule-set","Value":"sites","Action":"proxy","Outbound":"work","Network":"tcp","Port":"443","Process":"c:\\apps\\browser.exe"}],"Targets":[{"Name":"work","Protocol":3,"Source":"socks5://127.0.0.1:18080","Via":"@primary"}],"RuleSets":[{"Name":"sites","Members":[{"Kind":"domain-suffix","Value":"example.com","Action":""}]}]}""";
		var policy = JsonSerializer.Deserialize<NetworkOptions>(policyWire)!;
		var policyCopy = JsonSerializer.Deserialize<NetworkOptions>(JsonSerializer.Serialize(policy))!;
		Assert(policyCopy.Targets![0].Source == "socks5://127.0.0.1:18080" && policyCopy.Targets[0].Via == "@primary" && policyCopy.RuleSets![0].Members[0].Value == "example.com" && policyCopy.Rules[0].Outbound == "work" && policyCopy.Rules[0].Port == "443" && policyCopy.Rules[0].Network == "tcp" && policyCopy.Rules[0].Process == @"c:\apps\browser.exe", "Presentation DTO preserves advanced Go policy fields");
        using var resource = typeof(Program).Assembly.GetManifestResourceStream("TestApp.xaml")!;
        var xml = System.Xml.Linq.XDocument.Load(resource);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(ns + "Application.Resources")!.Elements());
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()) };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        TrayChecks.Run();
        var window = new MainWindow { ShowInTaskbar = false, Left = -5000, Top = -5000 };
        window.Show();
        // Session0 has a small virtual desktop; let captures exercise the requested window size.
        HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!.AddHook(CaptureWindowBounds);
        window.Width = 1379; window.Width = 1380;
        T Find<T>(string name) where T : FrameworkElement => (T)(window.FindName(name) ?? throw new Exception("Missing control: " + name));
        void Pump() { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        void Wait(Func<bool> condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { var until = DateTime.UtcNow.AddSeconds(25); while (!condition() && DateTime.UtcNow < until) Pump(); if (!condition()) throw new Exception("UI condition timed out at line " + line + "; feedback=" + Find<TextBlock>("Feedback").Text + "; org=" + Find<TextBlock>("ProfileOrganizationStatus").Text + "; policy=" + Find<TextBlock>("PolicyStatus").Text + "; mode=" + Find<ComboBox>("RoutingModeBox").SelectedIndex + "; draft=" + typeof(MainWindow).GetMethod("NetworkPolicyDraftChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)); }
        string NetworkDiff() {
            var v=typeof(MainWindow).GetField("networkPreferencesView",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window) as NetworkPreferencesView;
            if(v==null)return "view-null";
            var pairs=new[]{("mode",(string)typeof(MainWindow).GetField("routingDraftMode",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!,v.RoutingMode),("custom",(string)typeof(MainWindow).GetMethod("CustomRoutingText",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!,v.CustomRulesText),("preset",(string)typeof(MainWindow).GetMethod("PresetRoutingText",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!,v.PresetRulesText),("config",(string)typeof(MainWindow).GetField("presetConfigDraft",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!,v.PresetConfigText),("dns",Find<ComboBox>("NetworkDNSModeBox").SelectedIndex.ToString(),v.Mode),("ip",Find<TextBox>("NetworkServerIPBox").Text,v.ServerIP),("name",Find<TextBox>("NetworkServerNameBox").Text,v.ServerName),("port",Find<TextBox>("NetworkPortBox").Text,v.Port),("path",Find<TextBox>("NetworkPathBox").Text,v.Path),("resolvers",Find<TextBox>("NetworkResolversBox").Text,v.ResolversText),("dnsrules",Find<TextBox>("NetworkDNSRulesBox").Text,v.DNSRulesText),("lan",Find<CheckBox>("NetworkAllowLANBox").IsChecked?.ToString()??"null",v.AllowLAN.ToString()),("fake",Find<CheckBox>("NetworkFakeIPBox").IsChecked?.ToString()??"null",v.FakeIP.ToString()),("cache",Find<ComboBox>("NetworkCacheModeBox").SelectedIndex.ToString(),v.CacheMode),("capacity",Find<TextBox>("NetworkCacheCapacityBox").Text,v.CacheCapacity)};
            return string.Join("; ",pairs.Select(p=>$"{p.Item1}:eq={p.Item2==p.Item3},len={p.Item2.Length}/{p.Item3.Length}"))+"; status="+Find<TextBlock>("PolicyStatus").Text;
        }
        void WaitDownload(Func<bool> condition) { var until=DateTime.UtcNow.AddSeconds(130);while(!condition()&&DateTime.UtcNow<until)Pump();if(!condition())throw new Exception("Preset download timed out: "+Find<TextBlock>("LoyalStatus").Text+"; "+NetworkDiff());}
        void Click(string name) { if(name=="SaveNetworkPreferencesButton")Wait(()=>Find<Button>(name).IsEnabled&&((OneTunnel.Client.ServiceConnection)typeof(MainWindow).GetField("connection",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Actions.CanSave); Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        void Panel(string value) => typeof(MainWindow).GetMethod("ShowPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [value]);
        bool LibraryIdle() => !(bool)typeof(MainWindow).GetField("libraryPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("favoritePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Wait(() => Find<Button>("AddProfileButton").IsEnabled && Find<Button>("EditButton").IsEnabled && LibraryIdle());
        if(Environment.GetEnvironmentVariable("ONETUNNEL_PROXY_MODE_UI_ONLY")=="1") {
            var servers=Find<ListBox>("ProfilesList");
            foreach(var server in servers.Items.Cast<Profile>().ToArray()) {
                servers.SelectedItem=server;Wait(()=>LibraryIdle()&&Find<Button>("EditButton").IsEnabled);Pump();
                Assert(Find<StackPanel>("ModePanel").Visibility==Visibility.Collapsed,"Proxy inspector hides traffic destination: "+server.Protocol);
            }
            CaptureLogical(window,"proxy-inspector-normal.png",1280,760);CaptureLogical(window,"proxy-inspector-minimum.png",1060,641);
            var key=Convert.ToBase64String(Enumerable.Range(1,32).Select(i=>(byte)i).ToArray());
            var native=ProfileBackend.SaveEditorAsync("WireGuard fixture",$"[Interface]\nPrivateKey = {key}\nAddress = 10.66.0.2/32\n[Peer]\nPublicKey = {key}\nEndpoint = 192.0.2.5:51820\nAllowedIPs = 0.0.0.0/0","Native",null,transport,new()).GetAwaiter().GetResult();
            typeof(MainWindow).GetMethod("PresentApplied",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[native]);
            Wait(()=>LibraryIdle()&&servers.Items.Count==4&&Find<StackPanel>("ModePanel").Visibility==Visibility.Visible);
            Assert(servers.SelectedItem is Profile{Protocol:Protocol.WireGuard},"WG retains its traffic destination control");
            Wait(()=>((ComboBoxItem)Find<ComboBox>("ModeBox").Items[0]).Content?.ToString()=="Маршрутизация из AllowedIPs");
            Assert(((ComboBoxItem)Find<ComboBox>("ModeBox").Items[1]).Content?.ToString()=="Маршрутизация по настройкам","Native routing choices name the original and temporary-copy modes");
            CaptureLogical(window,"native-routing-copy-normal.png",1280,760);CaptureLogical(window,"native-routing-copy-minimum.png",1060,641);
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();return 0;
        }
        if(Environment.GetEnvironmentVariable("ONETUNNEL_BALANCE_UI_ONLY")=="1") {
            Panel("settings");typeof(MainWindow).GetField("settingsSection",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,"connection");typeof(MainWindow).GetMethod("PresentControlSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            var load=PrivateTask("LoadUserSettingsAsync",true);Wait(()=>load.IsCompleted);load.GetAwaiter().GetResult();
            Find<Expander>("BalanceExpander").IsExpanded=true;Pump();
            Find<CheckBox>("BalanceEnabledBox").IsChecked=true;Find<ComboBox>("BalanceModeBox").SelectedIndex=1;
            Assert(Find<ComboBox>("BalanceSelectionBox").SelectedIndex==0&&Find<ListBox>("BalanceProfilesList").Visibility==Visibility.Collapsed,"Automatic selection is the default and needs no server checklist");
            Wait(()=>Find<Button>("SaveUserSettingsButton").IsEnabled);Click("SaveUserSettingsButton");Wait(()=>!(bool)typeof(MainWindow).GetField("userSettingsSavePending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!);
            Assert(ProfileBackend.UserSettingsAsync(CancellationToken.None).GetAwaiter().GetResult().Preferences.Balance is {Enabled:true,Manual:false},"Automatic composition persists without selected server IDs");
            Find<ComboBox>("BalanceSelectionBox").SelectedIndex=1;Pump();
            var rows=Find<ListBox>("BalanceProfilesList").Items.Cast<object>().ToArray();Assert(rows.Length==3,"Pool server picker includes subscription and manual groups");
            foreach(var row in rows.Take(2)){Find<ListBox>("BalanceProfilesList").ScrollIntoView(row);Pump();var check=Descendants(Find<ListBox>("BalanceProfilesList")).OfType<CheckBox>().First(c=>ReferenceEquals(c.DataContext,row));typeof(System.Windows.Controls.Primitives.ToggleButton).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(check,null);Pump();}
            typeof(MainWindow).GetMethod("BalanceDraft_Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);
            Wait(()=>Find<Button>("SaveUserSettingsButton").IsEnabled);Click("SaveUserSettingsButton");Wait(()=>!(bool)typeof(MainWindow).GetField("userSettingsSavePending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!);
            var saved=ProfileBackend.UserSettingsAsync(CancellationToken.None).GetAwaiter().GetResult().Preferences.Balance;
            Assert(saved is {Enabled:true,Manual:true,Mode:"balanced",Best:2,Interval:30,MaxRTT:1500}&&saved.ProfileIDs.Split(',').Length==2,"Manual pool mode, members and quality limits persist through encrypted Go settings");
            Find<TextBox>("BalanceBestBox").Text="9";Pump();Assert(!Find<Button>("SaveUserSettingsButton").IsEnabled,"Invalid pool bounds cannot be saved");
            Click("ReloadUserSettingsButton");Wait(()=>Find<TextBox>("BalanceBestBox").Text=="2");
            Find<Expander>("BalanceExpander").IsExpanded=true;var scroll=Find<ScrollViewer>("WorkspaceScroll");var top=Find<Expander>("BalanceExpander").TranslatePoint(new Point(),scroll).Y;scroll.ScrollToVerticalOffset(scroll.VerticalOffset+top);Pump();
            CaptureLogical(window,"balance-manual-normal.png",960,640);
            Find<ComboBox>("BalanceSelectionBox").SelectedIndex=0;Pump();
            CaptureLogical(window,"balance-settings-normal.png",960,640);CaptureLogical(window,"balance-settings-minimum.png",800,560);
            Find<CheckBox>("BalanceEnabledBox").IsChecked=false;Wait(()=>Find<Button>("SaveUserSettingsButton").IsEnabled);Click("SaveUserSettingsButton");Wait(()=>!(bool)typeof(MainWindow).GetField("userSettingsSavePending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!);
            Assert(ProfileBackend.UserSettingsAsync(CancellationToken.None).GetAwaiter().GetResult().Preferences.Balance?.Enabled==false,"Pool can be disabled without deleting saved members");
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();return 0;
        }
        if(Environment.GetEnvironmentVariable("ONETUNNEL_PROVIDER_UI_ONLY")=="1") {
            typeof(MainWindow).GetMethod("BeginNewProfile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);
            Find<TextBox>("SourceBox").Text="""[{"remarks":"Imported policy","outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"192.0.2.88","port":443,"users":[{"id":"11111111-1111-4111-8111-111111111111","encryption":"none"}]}]},"streamSettings":{"network":"tcp","security":"tls","tlsSettings":{"serverName":"example.com"}}},{"tag":"direct","protocol":"freedom"}],"routing":{"rules":[{"type":"field","domain":["domain:imported.example"],"outboundTag":"direct"},{"type":"field","protocol":["bittorrent"],"outboundTag":"direct"}]}}]""";
            Find<TextBox>("NameBox").Text="Imported provider";Pump();Wait(()=>Find<Button>("SaveProfileButton").IsEnabled);Click("SaveProfileButton");Wait(()=>LibraryIdle()&&Find<ListBox>("ProfilesList").Items.Count==4);
            Assert(ProfileBackend.ImportedRoutingSourcesAsync().GetAwaiter().GetResult().Count==1,"Importing a subscription through the UI also imports routing");
            Panel("network");Wait(()=>NetworkIdle()&&Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
            Find<ComboBox>("RoutingModeBox").SelectedIndex=1;Find<ComboBox>("RoutingSourceBox").SelectedIndex=1;Pump();
            Wait(()=>Find<ComboBox>("ProviderRoutingGroupBox").Items.Count==1);Find<ComboBox>("ProviderRoutingGroupBox").SelectedIndex=0;
            Wait(()=>!(bool)typeof(MainWindow).GetField("policyToolsPending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!&&Find<TextBlock>("ProviderRoutingReview").Text.Contains("В черновике"));
            Assert(window.FindName("ProviderRoutingURLBox")==null&&window.FindName("LoadProviderRoutingButton")==null&&window.FindName("ApplyProviderRoutingButton")==null&&window.FindName("ProviderRoutingChoiceBox")==null,"Routing only selects already imported policies without a second URL/import flow");
            Assert(Find<TextBlock>("ProviderRoutingReview").Text.Contains("исключены"),"Excluded transport and balancing controls are explained after subscription selection");
            Click("SaveNetworkPreferencesButton");Wait(()=>NetworkIdle()&&LibraryIdle()&&Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
            Click("ReloadNetworkPreferencesButton");Wait(()=>NetworkIdle()&&Find<ComboBox>("ProviderRoutingGroupBox").SelectedItem is ImportedRoutingSource);
            Assert(Find<ComboBox>("RoutingSourceBox").SelectedIndex==1&&Find<ComboBox>("ProviderRoutingGroupBox").SelectedItem is ImportedRoutingSource&&Find<TextBlock>("ProviderRoutingReview").Text.Contains("исключены"),"Imported subscription, configuration and limitations survive encrypted save/reload");
            Find<Expander>("DNSSettingsExpander").IsExpanded=false;
            var sc=Find<ScrollViewer>("WorkspaceScroll");var top=Find<Expander>("LoyalRulesPanel").TranslatePoint(new Point(),sc).Y;sc.ScrollToVerticalOffset(sc.VerticalOffset+top);Pump();
            CaptureLogical(window,"imported-routing-normal.png",960,640);CaptureLogical(window,"imported-routing-minimum.png",800,560);
            Find<ComboBox>("RoutingSourceBox").SelectedIndex=0;Pump();Assert(Find<StackPanel>("LoyalsoldierSourcePanel").IsVisible,"Loyalsoldier remains available");
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();return 0;
        }
        if(Environment.GetEnvironmentVariable("ONETUNNEL_DNS_LAYOUT_ONLY")=="1") {
            Panel("network"); Pump();
            var dns=Find<Expander>("DNSSettingsExpander");
            dns.IsExpanded=false; Pump();
            Assert(!Find<ComboBox>("NetworkDNSModeBox").IsVisible && !Find<TextBox>("NetworkResolversBox").IsVisible,"Collapsing DNS hides all DNS controls");
            Assert(Find<CheckBox>("RoutingNoneToggle").IsVisible && Find<Button>("SaveNetworkPreferencesButton").IsVisible,"Routing and shared save remain visible when DNS collapses");
            CaptureLogical(window,"dns-collapsed-routing-normal.png",960,640);CaptureLogical(window,"dns-collapsed-routing-minimum.png",800,560);
            dns.IsExpanded=true;Pump();Assert(Find<ComboBox>("NetworkDNSModeBox").IsVisible,"DNS reopens independently");
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();return 0;
        }
        if(Environment.GetEnvironmentVariable("ONETUNNEL_GLOBAL_TRANSPORT_ONLY")=="1") {
            Panel("editor");Assert(!Find<Expander>("TransportExpander").IsVisible,"Profile/group creation has no transport options");
            Panel("settings");typeof(MainWindow).GetField("settingsSection",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,"general");typeof(MainWindow).GetMethod("PresentControlSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            var load=PrivateTask("LoadUserSettingsAsync",true);Wait(()=>load.IsCompleted);load.GetAwaiter().GetResult();
            Find<Expander>("TransportExpander").IsExpanded=true;Pump();Assert(Find<CheckBox>("FragmentCheck").IsVisible,"Common transport controls are in General settings");
            Find<CheckBox>("FragmentCheck").IsChecked=true;Find<CheckBox>("NoiseCheck").IsChecked=true;Find<CheckBox>("MuxCheck").IsChecked=true;
            Find<ComboBox>("MuxProtocolBox").SelectedIndex=3;Find<TextBox>("MuxMaxConnectionsBox").Text="2";Find<TextBox>("MuxMinStreamsBox").Text="1";Find<CheckBox>("MuxPaddingBox").IsChecked=true;Find<TextBox>("XUDPConcurrencyBox").Text="4";
            Wait(()=>Find<Button>("SaveUserSettingsButton").IsEnabled);Click("SaveUserSettingsButton");
            Wait(()=>!(bool)typeof(MainWindow).GetField("userSettingsSavePending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!);
            var savedPrefs=ProfileBackend.UserSettingsAsync(CancellationToken.None).GetAwaiter().GetResult().Preferences.Transport;
            Assert(savedPrefs is {Fragment:true,Noise:true,Mux:true,MuxProtocol:"yamux",MuxMaxConnections:"2",MuxMinStreams:"1",MuxPadding:true,XUDPConcurrency:"4"},"Global options persist through the encrypted Go settings store");
            var storePath=System.IO.Path.Combine(data,"profiles.bin");var valid=File.ReadAllBytes(storePath);Find<TextBox>("FragmentLengthBox").Text="0";Wait(()=>Find<Button>("SaveUserSettingsButton").IsEnabled);Click("SaveUserSettingsButton");Wait(()=>!(bool)typeof(MainWindow).GetField("userSettingsSavePending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!);
            Assert(valid.SequenceEqual(File.ReadAllBytes(storePath)),"Invalid common transport values cannot change saved settings");
            load=PrivateTask("LoadUserSettingsAsync",true);Wait(()=>load.IsCompleted);load.GetAwaiter().GetResult();
            var globalDraft=(TransportInput)typeof(MainWindow).GetMethod("ReadGlobalTransport",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
            typeof(MainWindow).GetMethod("BeginNewProfile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);Find<TextBox>("SourceBox").Text="socks5://192.0.2.81:1080\nsocks5://192.0.2.82:1080";Find<TextBox>("NameBox").Text="Global import";Pump();Wait(()=>Find<Button>("SaveProfileButton").IsEnabled);Click("SaveProfileButton");Wait(()=>LibraryIdle()&&Find<ListBox>("ProfilesList").Items.Count==5);
            Assert(ProfileBackend.UserSettingsAsync(CancellationToken.None).GetAwaiter().GetResult().Preferences.Transport==globalDraft,"Importing a multi-server group cannot erase global MUX/fragment/noise settings");
            Assert(!Find<Expander>("TransportExpander").IsVisible,"Transport settings remain outside the import editor");
            Panel("settings");typeof(MainWindow).GetField("settingsSection",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,"general");typeof(MainWindow).GetMethod("PresentControlSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);load=PrivateTask("LoadUserSettingsAsync",true);Wait(()=>load.IsCompleted);Find<Expander>("TransportExpander").IsExpanded=true;
            CaptureLogical(window,"global-transport-normal.png",960,640);CaptureLogical(window,"global-transport-minimum.png",800,560);
            var scroll=Find<ScrollViewer>("WorkspaceScroll");scroll.ScrollToBottom();Pump();Assert(Find<Button>("SaveUserSettingsButton").IsVisible,"Global settings save action remains reachable by scrolling");
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();Console.WriteLine("PASS: global transport UI save, validation, group import and layout checks");return 0;
        }
        if(Environment.GetEnvironmentVariable("ONETUNNEL_MENU_ONLY")=="1") {
            var menu=new ContextMenu{PlacementTarget=Find<Button>("InspectorMenuButton")};
            foreach(var title in new[]{"Редактировать профиль","Проверить доступ","Удалить профиль…","Копировать или переместить","DNS и маршрутизация"})menu.Items.Add(new MenuItem{Header=title,IsEnabled=title!="Проверить доступ"});
            menu.IsOpen=true;Pump();menu.UpdateLayout();
            foreach(MenuItem item in menu.Items){item.ApplyTemplate();Assert(((System.Windows.Shapes.Path)item.Template.FindName("CheckMark",item)).Visibility==Visibility.Collapsed,"Plain menu command has no empty icon/check square");}
            void SaveMenu(string name){var bmp=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(menu);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(System.IO.Path.Combine(Output,name));png.Save(file);}
            SaveMenu("inspector-menu.png");menu.IsOpen=false;
            var checkedItem=new MenuItem{Header="Группировать список",IsCheckable=true,IsChecked=true};var submenu=new MenuItem{Header="Порядок списка"};submenu.Items.Add(new MenuItem{Header="По имени"});menu.Items.Clear();menu.Items.Add(checkedItem);menu.Items.Add(submenu);menu.IsOpen=true;Pump();checkedItem.ApplyTemplate();submenu.ApplyTemplate();
            Assert(((System.Windows.Shapes.Path)checkedItem.Template.FindName("CheckMark",checkedItem)).Visibility==Visibility.Visible,"Checkable options retain their real checkmark");
            submenu.IsSubmenuOpen=true;Pump();Assert(((System.Windows.Controls.Primitives.Popup)submenu.Template.FindName("PART_Popup",submenu)).IsOpen,"Submenus remain available with the text-only menu template");submenu.IsSubmenuOpen=false;menu.IsOpen=false;
            CaptureLogical(window,"settings-gear.png",1280,760);
            typeof(MainWindow).GetField("allowClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();app.Shutdown();Console.WriteLine("PASS: menu chrome and settings gear checks");return 0;
        }
        var list = Find<ListBox>("ProfilesList");
        Assert(list.Items.Count == 3, "Synthetic profiles loaded");
        var snapshot = Storage.Load();
        var profileFile = Path.Combine(data, "profiles.bin");
        Assert(System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource).GroupDescriptions.Count==1,"Server library groups imported profiles by default");
        Assert(Raw<string>("group",new{Group="",Name="",Source="https://provider.example/sub/private-token?key=secret",Subscription=true})=="Подписка · provider.example","Subscription group name comes from URL host without exposing credentials or tokens");
        var batchImport=ProfileBackend.SaveEditorAsync("Batch test","socks5://192.0.2.81:1080\nsocks5://192.0.2.82:1080","",null,transport,new()).GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("SetGrouping",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false]);
        typeof(MainWindow).GetMethod("PresentApplied",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[batchImport]);
        Wait(()=>LibraryIdle()&&Find<Button>("EditButton").IsEnabled);Pump();
        Assert(batchImport.Batch&&batchImport.Added==2&&batchImport.Document.Profiles.Count(p=>p.Group=="Batch test")==2&&System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource).GroupDescriptions.Count==1,"Multiple pasted servers immediately appear as one named group");
        var batchExpander=Descendants(window).OfType<Expander>().First(e=>e.DataContext is System.Windows.Data.CollectionViewGroup g&&g.Name.ToString()=="Batch test");
        T GroupHeader<T>(Expander exp,string name) where T:FrameworkElement=>Descendants((DependencyObject)exp.Header).OfType<T>().First(c=>c.Name==name);
        Wait(()=>GroupHeader<Button>(batchExpander,"GroupDeleteAction").IsEnabled);
        Assert(!GroupHeader<Button>(batchExpander,"GroupRefreshAction").IsEnabled&&GroupHeader<Button>(batchExpander,"GroupDeleteAction").IsEnabled,"Group exposes explicit action chips and disables refresh for a pasted list without URL");
        var collapseGroup=GroupHeader<Button>(batchExpander,"GroupCollapseAction");collapseGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(!batchExpander.IsExpanded&&Storage.CollapsedGroups.Contains("Batch test"),"Collapse chip persists the imported group");
        Assert(GroupHeader<Button>(batchExpander,"GroupDeleteAction").IsVisible,"Collapsed group retains its action chips");
        collapseGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var subscriptionHeader=Descendants(window).OfType<Expander>().First(e=>e.DataContext is System.Windows.Data.CollectionViewGroup g&&g.Name.ToString()=="Personal subscription");
        Wait(()=>GroupHeader<Button>(subscriptionHeader,"GroupRefreshAction").IsEnabled);
        Assert(GroupHeader<Button>(subscriptionHeader,"GroupRefreshAction").IsEnabled,"Subscription header offers an explicit refresh chip");
        var groupTitle=GroupHeader<FrameworkElement>(batchExpander,"GroupTitle");var groupBadge=(FrameworkElement)GroupHeader<TextBlock>(batchExpander,"GroupCount").Parent;
        Assert(groupBadge.TranslatePoint(new Point(),window).X-groupTitle.TranslatePoint(new Point(groupTitle.ActualWidth,0),window).X<=10,"Profile count sits directly beside its group title instead of the latency column");
        CaptureLogical(window,"server-groups.png",1280,760);
        // Resizing for capture may recycle the virtualized group container.
        Pump(); window.UpdateLayout(); Pump();
        batchExpander=Descendants(window).OfType<Expander>().First(e=>e.DataContext is System.Windows.Data.CollectionViewGroup g&&g.Name.ToString()=="Batch test");
        Assert(batchExpander.IsLoaded && GroupHeader<Button>(batchExpander,"GroupDeleteAction").Tag is string deletionGroupName && deletionGroupName=="Batch test", "Group deletion uses the loaded container after capture");
        Wait(()=>GroupHeader<Button>(batchExpander,"GroupDeleteAction").IsEnabled);
        var beforeDeletePrompt=File.ReadAllBytes(profileFile);
        GroupHeader<Button>(batchExpander,"GroupDeleteAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(()=>GroupHeader<Border>(batchExpander,"GroupDeleteConfirmation").Visibility==Visibility.Visible);
        Assert(GroupHeader<TextBlock>(batchExpander,"GroupDeleteDescription").Text.Contains("2")&&beforeDeletePrompt.SequenceEqual(File.ReadAllBytes(profileFile)),"Inline deletion uses current group count and cannot delete on its first click");
        CaptureLogical(window,"group-delete-confirmation.png",1280,760);
        var cancelDelete=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};GroupHeader<Border>(batchExpander,"GroupDeleteConfirmation").RaiseEvent(cancelDelete);
        Assert(cancelDelete.Handled&&GroupHeader<Border>(batchExpander,"GroupDeleteConfirmation").Visibility==Visibility.Collapsed&&beforeDeletePrompt.SequenceEqual(File.ReadAllBytes(profileFile)),"Escape cancels group deletion without changing storage");
        GroupHeader<Button>(batchExpander,"GroupDeleteAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>GroupHeader<Border>(batchExpander,"GroupDeleteConfirmation").Visibility==Visibility.Visible);
        GroupHeader<Button>(batchExpander,"GroupDeleteConfirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(()=>LibraryIdle()&&list.Items.Count==3&&Find<Button>("EditButton").IsEnabled);
        Assert(!Storage.Load().Any(p=>p.Group=="Batch test"),"Confirmed header action deletes only its group through the existing Go mutation guard");
        var before = SHA256.HashData(File.ReadAllBytes(profileFile));
        var search = Find<TextBox>("SearchBox");
        string AccessibleName(UIElement element) => UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? "";
        Assert(AccessibleName(search) == "Поиск серверов" && AccessibleName(list) == "Серверы", "Search and server list have explicit screen-reader names");
        Assert(AccessibleName(Find<Button>("PrimaryConnectButton")) == Find<TextBlock>("PrimaryActionCaption").Text, "Primary action accessible name follows its current caption");
        Assert(ApplicationCommands.Find.CanExecute(null, window), "Find command is available in the server library");
        search.Text = "keyboard-query";
        ApplicationCommands.Find.Execute(null, window);
        Assert(search.SelectionLength == search.Text.Length, "Find command selects the existing search for replacement");
        var escapeSearch = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        search.RaiseEvent(escapeSearch);
        Assert(escapeSearch.Handled && search.Text.Length == 0 && list.Items.Count == 3, "Escape clears only the search and restores the server list");
        var emptyEscape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        search.RaiseEvent(emptyEscape);
        Assert(!emptyEscape.Handled, "Empty search does not swallow Escape");
        Panel("settings"); Pump();
        Assert(!ApplicationCommands.Find.CanExecute(null, window), "Find command does not steal keyboard input from settings");
        Panel("connection"); Pump();
        search.Text = "AMSTERDAM"; Pump(); Assert(list.Items.Count == 1, "Case-insensitive name search");
        search.Text = "hysteria2"; Pump(); Assert(list.Items.Count == 1, "Protocol search");
        search.Text = "work profiles"; Pump(); Assert(list.Items.Count == 1, "Group search");
        search.Text = "no-synthetic-match"; Pump(); Assert(list.Items.Count == 0, "No-result filtering");
        Capture(window, "workspace-empty-results.png");
        search.Text = ""; Pump(); Assert(list.Items.Count == 3, "Clearing search restores profiles");
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Search does not write profile storage");
        typeof(MainWindow).GetMethod("SidebarGroup_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new Button { Tag = "Work profiles" }, new RoutedEventArgs()]);
        Pump(); Assert(list.Items.Count == 1 && list.Items.OfType<Profile>().Single().Group == "Work profiles", "Control sidebar filters the selected group without changing stored order");
        Click("HomeNavigationButton"); Wait(() => LibraryIdle() && list.Items.Count == 3);
        Assert(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(profileFile))), "Sidebar navigation preserves encrypted profile storage");
        list.SelectedIndex = 0; Wait(() => LibraryIdle() && Find<Button>("FavoriteButton").IsEnabled);
        var selectedBeforeStar = ((Profile)list.SelectedItem).Id;
        var otherStarId = list.Items.OfType<Profile>().First(profile => profile.Id != selectedBeforeStar).Id;
        var starTask = PrivateTask("FavoriteProfileAsync", otherStarId); Wait(() => starTask.IsCompleted && LibraryIdle()); starTask.GetAwaiter().GetResult();
        Assert(((Profile)list.SelectedItem).Id == selectedBeforeStar && Storage.FavoriteIDs.Contains(otherStarId), "Row favorite updates the target profile without selecting it or connecting");
        var unstarTask = PrivateTask("FavoriteProfileAsync", otherStarId); Wait(() => unstarTask.IsCompleted && LibraryIdle()); unstarTask.GetAwaiter().GetResult();
        before = SHA256.HashData(File.ReadAllBytes(profileFile));
        var timingProfile = list.Items.OfType<Profile>().First();
        var timing = new OneTunnel.Controls.ProfileTimingConverter();
        var timingRow = new GroupResultDisplayRow(timingProfile.Id.ToString(), timingProfile.Name, "fixture", true, true, 12, "HTTPS · 12 мс", 12);
        Assert((string)timing.Convert([timingProfile.Id, new[] { timingRow }, false], typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture) == "12 мс", "Control rows format supplied HTTPS latency without inventing measurements");
        Assert((string)timing.Convert([timingProfile.Id, new[] { timingRow }, true], typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture) == "—", "Control rows hide stale check timings");
        list.SelectedIndex = 0; Wait(() => Find<Button>("EditButton").IsEnabled);
        Capture(window, "workspace-default.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Assert(list.TranslatePoint(new Point(0, 0), window).Y < window.ActualHeight - 160, "Server rows remain above the fold at minimum size");
        var connectBounds = Find<Button>("PrimaryConnectButton").TranslatePoint(new Point(0, Find<Button>("PrimaryConnectButton").ActualHeight), window);
        Assert(connectBounds.Y < window.ActualHeight - 65, "Connect action remains above the fold at minimum size");
        Capture(window, "workspace-minimum.png");
        foreach (var name in new[] { "ImportConnectionButton", "PrimaryConnectButton", "SearchBox", "ProfilesList", "LibraryMenuButton", "InspectorMenuButton", "InspectorFavorite" }) {
            var element = Find<FrameworkElement>(name); Assert(element.IsVisible && element.ActualWidth > 20 && element.ActualHeight > 10, name + " remains visible at minimum size");
        }
        window.Width = 1240; window.Height = 850; Pump();
        Panel("settings"); Wait(() => Find<Button>("ReloadUserSettingsButton").IsEnabled);
        var automaticDraft = Find<CheckBox>("UserAutoConnectBox").IsChecked;
        typeof(MainWindow).GetMethod("SettingsSection_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new Button { Tag = "UserSystemProxyBox" }, new RoutedEventArgs()]);
        Pump(); Assert(Find<Border>("ProxySettingsSection").IsVisible && !Find<Border>("AutoConnectSettingsSection").IsVisible && Find<Button>("SaveUserSettingsButton").IsVisible && Find<CheckBox>("UserAutoConnectBox").IsChecked == automaticDraft, "Settings navigation switches sections while retaining drafts and the common save action");
        Capture(window, "control-system-proxy.png");
        typeof(MainWindow).GetMethod("SettingsSection_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [new Button { Tag = "UserAutoConnectBox" }, new RoutedEventArgs()]); Pump();
        Capture(window, "control-settings.png");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump(); Capture(window, "control-settings-minimum.png");
        window.Width = 1240; window.Height = 850; Panel("connection"); Pump();
        Click("EditButton"); Wait(() => Find<StackPanel>("EditorPanel").IsVisible && Find<TextBox>("SourceBox").Text.Length > 0);
        Assert(Find<CheckBox>("AutoReconnectCheck").IsChecked == true, "Existing reconnection setting rendered");
        Assert(Find<CheckBox>("QualityCheck").IsChecked == false && !Find<CheckBox>("QualityCheck").IsEnabled, "Quality selection is explicitly opt-in");
        Find<CheckBox>("FallbackCheck").IsChecked = true; Pump();
        Assert(Find<CheckBox>("QualityCheck").IsEnabled, "Fallback permission enables quality selection");
        Find<CheckBox>("QualityCheck").IsChecked = true;
        Find<Expander>("ReliabilityExpander").IsExpanded = true; Pump();
        Find<CheckBox>("QualityCheck").BringIntoView(); Pump();
        Assert(Find<CheckBox>("QualityCheck").IsVisible && Find<CheckBox>("QualityCheck").ActualWidth > 200, "Quality selection is reachable in the editor");
        Capture(window, "workspace-quality-selection.png");
        Find<Expander>("ReliabilityExpander").IsExpanded = false;
        Find<CheckBox>("FallbackCheck").IsChecked = false; Pump();
        Assert(!Find<CheckBox>("QualityCheck").IsEnabled, "Quality selection requires fallback opt-in");
        Find<CheckBox>("FallbackCheck").IsChecked = true; Pump();
        Assert(!Find<Expander>("TransportExpander").IsVisible,"Transport options are not part of creating a profile or group");
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
        typeof(MainWindow).GetMethod("SetGrouping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]); Pump();
        var groupExpander = Descendants(window).OfType<Expander>().First(e => e.DataContext is System.Windows.Data.CollectionViewGroup);
        string groupName = ((System.Windows.Data.CollectionViewGroup)groupExpander.DataContext).Name.ToString()!;
        groupExpander.IsExpanded = false; Pump();
        Assert(Storage.CollapsedGroups.Contains(groupName), "Group collapse persists through Go");
        Raw<JsonElement>("store.load");Pump();
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
        if (Environment.GetEnvironmentVariable("ONETUNNEL_UI_HEADLESS") == "1") Console.WriteLine("SKIP: global restore hotkey requires an interactive Windows session");
        else CheckTrayWindowControls(window, Pump, () => Click("HideToTrayButton"));
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
        void HoldHostGate(string message) { bool acquired=false; Wait(() => acquired || (acquired=hostGate.Wait(0))); Assert(acquired,message); }
        HoldHostGate("Test can delay a local library projection without networking");
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
            new(targetResult.Id.ToString(), "Synthetic network result", "Available", true, true, 12, "12 ms check duration · 7 ms HTTPS latency (not ICMP RTT)", 7),
            new(Guid.NewGuid().ToString(), "Synthetic configuration result", "Configuration valid", true, false, 0, "Configuration only · no network check")
        };
        var syntheticResults = new GroupResultsView("Synthetic results", "2026-10-23", resultRows, true, false, true, "synthetic-unretained-token", "");
        Panel("connection");
        RenderResults(syntheticResults); Pump();
        Assert(Find<ItemsControl>("GroupResults").Items.Cast<GroupResultDisplayRow>().SequenceEqual(resultRows), "Result rendering preserves Go-owned row order and timing labels");
        Assert(Find<Button>("SelectFastestResultButton").IsEnabled, "Eligible Go view enables explicit selection");
        Panel("results"); Pump(); Capture(window, "group-results-default.png");
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
        HoldHostGate("Test can delay best-result visibility projection without networking");
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
        Panel("organization");
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
        HoldHostGate("Test can delay profile metadata without networking");
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
        Panel("network");
        Find<Expander>("DNSSettingsExpander").IsExpanded = true;
        Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var networkDraft = Find<TextBox>("NameBox").Text;
        var networkOriginal = Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == targetResult.Id.ToString());
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 1;
        Find<TextBox>("NetworkServerIPBox").Text = "9.9.9.9";
        Find<TextBox>("NetworkServerNameBox").Text = "dns.quad9.net";
        Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Clear();
        Find<ComboBox>("RoutingModeBox").SelectedIndex = 2;
        Find<TextBox>("NetworkRulesBox").Text = "domain-suffix | proxy | example.com\ncidr | direct | 192.168.1.0/24\nprocess | reject | C:\\Synthetic\\blocked.exe";
        Assert(Find<CheckBox>("NetworkAllowLANBox").IsChecked == false, "LAN bypass is off by default");
        Find<TextBox>("NetworkResolversBox").Text = "https | 1.1.1.1 | cloudflare-dns.com | | \ntls | 2001:4860:4860::8888 | dns.google | | ";
        Find<TextBox>("NetworkDNSRulesBox").Text = "domain | work.example | 1\ndomain-suffix | internal.example | 2";
        Find<CheckBox>("NetworkAllowLANBox").IsChecked = true;
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        JsonElement NetworkProfile() => Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == targetResult.Id.ToString());
        var networkSaved = NetworkProfile(); var networkPolicy = networkSaved.GetProperty("Network");
        Assert(networkPolicy.GetProperty("DNS").GetProperty("Transport").GetString() == "https" && networkPolicy.GetProperty("DNS").GetProperty("Port").GetInt32() == 443 && networkPolicy.GetProperty("Rules").GetArrayLength() == 3, "DNS and split rules save through Go with canonical defaults");
        Assert(networkSaved.GetProperty("Source").GetString() == networkOriginal.GetProperty("Source").GetString() && Find<TextBox>("NameBox").Text == networkDraft, "Network preference save preserves profile source and unfinished editor draft");
        Assert(networkPolicy.GetProperty("Resolvers").GetArrayLength() == 2 && networkPolicy.GetProperty("DNSRules")[1].GetProperty("ResolverIndex").GetInt32() == 1 && networkPolicy.GetProperty("AllowLAN").GetBoolean(), "Domain DNS and explicit LAN preferences save via the Go parser");
        var canonicalRules = Find<TextBox>("NetworkRulesBox").Text;
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(Find<TextBox>("NetworkRulesBox").Text == canonicalRules && Find<TextBox>("NetworkServerIPBox").Text == "9.9.9.9", "Saved network preferences round-trip through real metadata IPC");
        Assert(Find<TextBox>("NetworkResolversBox").Text.Contains("2001:4860:4860::8888") && Find<TextBox>("NetworkDNSRulesBox").Text.Contains("2") && Find<CheckBox>("NetworkAllowLANBox").IsChecked == true, "Domain resolver and LAN preferences round-trip through metadata IPC");
        bool PolicyIdle() => !(bool)typeof(MainWindow).GetField("policyToolsPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void CheckPolicy(bool explain) { var task = PrivateTask("RunPolicyToolsAsync", explain); Wait(() => task.IsCompleted && PolicyIdle()); task.GetAwaiter().GetResult(); }
        void PolicyQuery(string address, string port, string ip = "", string process = "") {
            Find<TextBox>("PolicyAddressBox").Text = address; Find<TextBox>("PolicyPortBox").Text = port; Find<TextBox>("PolicyIPBox").Text = ip; Find<TextBox>("PolicyProcessBox").Text = process; Find<ComboBox>("PolicyTransportBox").SelectedIndex = 1;
        }
        void AlignPolicy() { var scroll = Find<ScrollViewer>("WorkspaceScroll"); var top = Find<Expander>("PolicyToolsPanel").TranslatePoint(new Point(), scroll).Y; scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top); Pump(); }
        var policyBytes = File.ReadAllBytes(profileFile);
        Find<Expander>("PolicyToolsPanel").IsExpanded = true;
        CheckPolicy(false);
        Assert(Find<TextBlock>("PolicyAnalysisResult").Text.Contains("не найдены"), "Saved policy duplicate check uses the real Go analyzer");
        PolicyQuery("api.example.com", "443"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Через прокси" && Find<TextBlock>("PolicyExplanation").Text.Contains("№1"), "Saved domain rule predicts proxy with one-based rule number");
        PolicyQuery("outside.invalid", "443", "192.168.1.3"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Напрямую" && Find<TextBlock>("PolicyExplanation").Text.Contains("№2"), "Manual domain plus IP metadata predicts the CIDR rule without DNS lookup");
        PolicyQuery("outside.invalid", "443", "203.0.113.9", @"C:\Synthetic\blocked.exe"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Блокировка" && Find<TextBlock>("PolicyExplanation").Text.Contains("№3"), "Process metadata is passed to Go rather than matched by the UI");
        PolicyQuery("outside.invalid", "443", "203.0.113.9", @"C:\Synthetic\other.exe"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyExplanation").Text == "Действие по умолчанию", "Default policy does not invent rule number zero");
        PolicyQuery("outside.invalid", "53"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Через настроенный DNS-резолвер" && !Find<TextBlock>("PolicyExplanation").Text.Contains("№0"), "DNS interception is a prediction, without a fabricated resolver address");
        PolicyQuery("api.example.com", ""); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Недостаточно данных для прогноза", "Missing port remains unknown instead of silently using 443");
        PolicyQuery("203.0.113.9", "443"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Недостаточно данных для прогноза" && Find<TextBlock>("PolicyExplanation").Text.Contains("№1"), "Unknown earlier domain rule is not skipped to a later rule");
        var policyWidth = window.Width; var policyHeight = window.Height;
        CaptureLogical(window, "policy-unknown-normal.png", 1280, 760, 1, AlignPolicy);
        CaptureLogical(window, "policy-unknown-minimum.png", 800, 560, 1, () => { Find<StackPanel>("PolicyRouteResult").BringIntoView(); Pump(); });
        var policyScroll = Find<ScrollViewer>("WorkspaceScroll");
        var resultBottom = Find<StackPanel>("PolicyRouteResult").TranslatePoint(new Point(0, Find<StackPanel>("PolicyRouteResult").ActualHeight), policyScroll).Y;
        Assert(resultBottom <= policyScroll.ActualHeight + 1 && Find<Button>("ExplainPolicyButton").TranslatePoint(new Point(), policyScroll).Y >= 0 && Find<TextBlock>("PageTitle").ActualWidth > 300, "Minimum window keeps the action/result reachable by scrolling and does not reserve hidden toolbar columns");
        Assert(Find<StackPanel>("NetworkPreferencesPanel").ActualWidth <= 620 && Find<Border>("FeedbackNotice").Visibility == Visibility.Collapsed, "Policy form obeys the 620-DIP cap and stale feedback does not cover controls");
        window.Width = policyWidth; window.Height = policyHeight; Pump();
        PolicyQuery("api.example.com", "65536"); CheckPolicy(true);
        Assert(Find<StackPanel>("PolicyRouteResult").Visibility == Visibility.Collapsed && Find<TextBlock>("PolicyPortError").Text.Contains("65535") && Find<TextBlock>("PolicyPortError").Visibility == Visibility.Visible, "Invalid port clears old prediction and has an inline field error");
        Assert(policyBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Analysis and all route predictions leave encrypted profile storage unchanged");
        var duplicatePolicy = """{"Targets":[{"Name":"work","Protocol":3,"Source":"socks5://user:POLICY-TOOLS-SECRET@127.0.0.1:18080"}],"RuleSets":[{"Name":"sites","Members":[{"Kind":"domain-suffix","Value":"example.com"}]}],"Rules":[{"Kind":"rule-set","Value":"sites","Action":"proxy","Outbound":"work","Network":"tcp","Port":"443"},{"Kind":"rule-set","Value":"sites","Action":"reject","Network":"tcp","Port":"443"}]}""";
        Find<TextBox>("NetworkRulesBox").Text = duplicatePolicy;
        Assert(!Find<Button>("CheckPolicyDuplicatesButton").IsEnabled && !Find<Button>("ExplainPolicyButton").IsEnabled && Find<TextBlock>("PolicyStatus").Text.Contains("Сохраните"), "Unsaved policy drafts disable checks without silently saving");
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        policyBytes = File.ReadAllBytes(profileFile);
        CheckPolicy(false);
        Assert(Find<TextBlock>("PolicyAnalysisResult").Text.Contains("№2") && !Find<TextBlock>("PolicyAnalysisResult").Text.Contains("POLICY-TOOLS-SECRET"), "Identical conditions with different actions warn with correct index and no credentials");
        CaptureLogical(window, "policy-duplicates.png", 1280, 760, 1, AlignPolicy);
        PolicyQuery("api.example.com", "443"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Через прокси" && Find<TextBlock>("PolicyOutbound").Text == "Сервер: work" && !Find<TextBlock>("PolicyTechnicalDetails").Text.Contains("POLICY-TOOLS-SECRET"), "Named target prediction exposes its name but not target credentials");
        CaptureLogical(window, "policy-proxy-target.png", 1280, 760, 1, AlignPolicy);
        window.Width = policyWidth; window.Height = policyHeight; Pump();
        Assert(policyBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Advanced policy checks are also read-only");
        var external = ProfileBackend.NetworkPreferencesAsync(targetResult.Id).GetAwaiter().GetResult();
        var externalInput = new NetworkInput(external.Mode, external.ServerIP, external.ServerName, external.Port, external.Path, canonicalRules, external.ResolversText, external.DNSRulesText, external.AllowLAN, external.FakeIP, external.CacheMode, external.CacheCapacity);
        ProfileBackend.SaveNetworkPreferencesAsync(targetResult.Id, external.Token, externalInput).GetAwaiter().GetResult();
        CheckPolicy(false);
        Assert(Find<TextBlock>("PolicyAnalysisResult").Visibility == Visibility.Collapsed && !Find<Button>("CheckPolicyDuplicatesButton").IsEnabled && Find<TextBlock>("PolicyStatus").Text.Contains("Обновите"), "Changed saved token suppresses the result and requires explicit reload");
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        var policyTask = PrivateTask("RunPolicyToolsAsync", false);
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id != targetResult.Id);
        Wait(() => policyTask.IsCompleted && PolicyIdle() && NetworkIdle() && LibraryIdle()); policyTask.GetAwaiter().GetResult();
        Assert(Find<TextBlock>("PolicyAnalysisResult").Visibility == Visibility.Collapsed, "Profile switch discards an in-flight policy result");
        list.SelectedIndex = -1; Pump();
        Assert(!Find<Button>("CheckPolicyDuplicatesButton").IsEnabled && !Find<Button>("ExplainPolicyButton").IsEnabled, "No profile disables both policy operations");
        list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        var apiFailureStore = Raw<JsonElement>("store.load");
        Raw<object?>("delete", new { ID = targetResult.Id.ToString() });
        try { CheckPolicy(false); Assert(Find<TextBlock>("PolicyAnalysisResult").Visibility == Visibility.Collapsed && Find<TextBlock>("PolicyStatus").Text.Contains("Не удалось"), "API failure is not shown as a successful zero-warning analysis"); }
        finally { Raw<object?>("store.save", new { Document = JsonNode.Parse(apiFailureStore.GetRawText()) }); }
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        Find<Expander>("PolicyToolsPanel").IsExpanded = false;
        Assert(Find<TextBox>("NetworkRulesBox").Text == canonicalRules && Find<TextBox>("NameBox").Text == networkDraft, "Policy tools preserve restored settings and unrelated editor drafts");
        Find<ComboBox>("RuleKindBox").SelectedIndex=3;Find<ComboBox>("RuleActionBox").SelectedIndex=2;
        string chosenExecutable=Path.Combine(Environment.SystemDirectory,"cmd.exe");
        typeof(MainWindow).GetMethod("ApplyRuleExecutable",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[chosenExecutable,false]);
        Assert(Find<TextBox>("RuleValueBox").Text==chosenExecutable&&Find<Button>("RuleExecutableButton").Visibility==Visibility.Visible,"Executable picker selection binds to the process rule value without executing the program");
        var beforeProcessRule=File.ReadAllBytes(profileFile);var processRuleTask=PrivateTask("AppendCustomRuleAsync");Wait(()=>processRuleTask.IsCompleted&&PolicyIdle());processRuleTask.GetAwaiter().GetResult();
        Assert(Find<TextBox>("NetworkRulesBox").Text.Contains("cmd.exe")&&beforeProcessRule.SequenceEqual(File.ReadAllBytes(profileFile)),"Process builder appends a validated rule to the draft without auto-saving");
        Click("SaveNetworkPreferencesButton");Wait(()=>NetworkIdle()&&LibraryIdle()&&Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        PolicyQuery("outside.invalid","443","203.0.113.9",chosenExecutable);CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text=="Блокировка","A chosen executable reaches the real Go process matcher");
        CaptureLogical(window,"routing-process-builder.png",960,640,prepare:()=>{var scroll=Find<ScrollViewer>("WorkspaceScroll");scroll.ScrollToVerticalOffset(scroll.VerticalOffset+Find<StackPanel>("CustomRoutingPanel").TranslatePoint(new Point(),scroll).Y);Pump();});
        window.Width=policyWidth;window.Height=policyHeight;Pump();
        Find<TextBox>("NetworkRulesBox").Text=canonicalRules;Click("SaveNetworkPreferencesButton");Wait(()=>NetworkIdle()&&LibraryIdle()&&Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        var loyalBefore = File.ReadAllBytes(profileFile);
        var loyalFile = Path.Combine(Output, "geosite-fixture.dat");
        File.WriteAllBytes(loyalFile,Convert.FromBase64String("CjIKB3lvdXR1YmUSEQgCEg1sb3lhbC5leGFtcGxlEhQIAxIQZXhhY3QubG95YWwudGVzdA=="));
        Find<CheckBox>("RoutingPresetToggle").IsChecked=true;
        Assert(Find<ComboBox>("RoutingModeBox").SelectedIndex==1 && Find<CheckBox>("RoutingCustomToggle").IsChecked==false && Find<CheckBox>("RoutingNoneToggle").IsChecked==false,"Preset toggle turns off the other two routing modes");
        Find<CheckBox>("RoutingPresetToggle").IsChecked=false;
        Assert(Find<CheckBox>("RoutingPresetToggle").IsChecked==true,"Routing toggles keep exactly one selected mode");
        Assert(Find<TextBox>("NetworkRulesBox").Text == "" && Find<TextBox>("NetworkRulesBox").IsReadOnly && Find<Expander>("LoyalRulesPanel").Visibility == Visibility.Visible, "Preset mode has its own empty bank and disables manual policy editing");
        Find<Expander>("LoyalRulesPanel").IsExpanded = true;
        Find<TextBox>("LoyalCategoryBox").Clear();Click("ConfigurePresetButton");
        Assert(Find<TextBlock>("PresetCategoryError").Visibility==Visibility.Visible && loyalBefore.SequenceEqual(File.ReadAllBytes(profileFile)),"Missing category is reported inline before any import or save");
        Assert(Find<ComboBox>("LoyalFormatBox").Items.Count==2 && window.FindName("PresetClashListBox")==null,"Preset UI offers only geosite and geoip");
        Find<ComboBox>("LoyalFormatBox").SelectedIndex = 0;
        Find<TextBox>("LoyalCategoryBox").Text = "youtube";
        Find<TextBox>("LoyalNameBox").Text = "loyal-ui";
        Find<ComboBox>("LoyalActionBox").SelectedIndex = 2;
        var loyalTask = PrivateTask("ImportLoyalRulesAsync", loyalFile, false);
        Assert(!Find<Button>("SaveNetworkPreferencesButton").IsEnabled && !Find<ComboBox>("RoutingModeBox").IsEnabled, "Pending preset import prevents saving or switching a half-built policy");
        Wait(() => loyalTask.IsCompleted && PolicyIdle()); loyalTask.GetAwaiter().GetResult();
        var loyalDraft = Find<TextBox>("NetworkRulesBox").Text;
        Assert(loyalDraft.Contains("Snapshot") && loyalDraft.Contains("loyal-ui") && loyalBefore.SequenceEqual(File.ReadAllBytes(profileFile)), "Loyalsoldier import creates a compressed draft without changing stored profiles");
        Assert(!Find<Button>("CheckPolicyDuplicatesButton").IsEnabled && Find<TextBlock>("LoyalStatus").Text.Contains("2"), "Imported draft requires explicit save before analysis");
        var repeatedTask = PrivateTask("ImportLoyalRulesAsync", loyalFile, false); Wait(() => repeatedTask.IsCompleted && PolicyIdle()); repeatedTask.GetAwaiter().GetResult();
        Assert(Find<TextBox>("NetworkRulesBox").Text == loyalDraft && Find<TextBlock>("LoyalStatus").Text.Contains("уже есть"), "Import refuses silent replacement and preserves the draft");
        Find<CheckBox>("LoyalReplaceBox").IsChecked = true;
        Find<ComboBox>("LoyalActionBox").SelectedIndex = 1;
        var replaceTask = PrivateTask("ImportLoyalRulesAsync", loyalFile, false); Wait(() => replaceTask.IsCompleted && PolicyIdle()); replaceTask.GetAwaiter().GetResult();
        Assert(Find<TextBox>("NetworkRulesBox").Text == loyalDraft, "Explicit update preserves rule action and position");
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        var loyalCopy = JsonSerializer.Deserialize<NetworkOptions>(NetworkProfile().GetProperty("Network").GetRawText())!;
        Assert(loyalCopy.RuleSets!.First(s => s.Name == "loyal-ui").Data?.Snapshot?.Length > 0, "Presentation metadata retains imported compressed snapshots");
        Assert(Find<TextBlock>("RoutingPresetSummary").Text.Contains("loyal-ui") && Find<TextBlock>("RoutingPresetSummary").Text.Contains("Блокировать") && Find<TextBox>("NetworkRulesBox").Visibility==Visibility.Collapsed, "Preset mode shows an ordered name/action summary instead of JSON");
        PolicyQuery("a.loyal.example", "443", "203.0.113.9", @"C:\Synthetic\other.exe"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text.Contains("Блок"), "Imported geosite domain suffix reaches the saved Go route predictor");
        CaptureLogical(window, "routing-presets.png", 960, 640, prepare: () => { var scroll=Find<ScrollViewer>("WorkspaceScroll"); scroll.ScrollToVerticalOffset(scroll.VerticalOffset+Find<CheckBox>("RoutingPresetToggle").TranslatePoint(new Point(),scroll).Y-48); Pump(); });
        CaptureLogical(window, "loyal-import-normal.png", 960, 640, prepare: () => {
            var scroll = Find<ScrollViewer>("WorkspaceScroll");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + Find<Expander>("LoyalRulesPanel").TranslatePoint(new Point(), scroll).Y); Pump();
        });
        CaptureLogical(window, "loyal-import-minimum.png", 800, 560, prepare: () => {
            var scroll = Find<ScrollViewer>("WorkspaceScroll"); var status = Find<TextBlock>("LoyalStatus");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + status.TranslatePoint(new Point(0, status.ActualHeight), scroll).Y - scroll.ActualHeight + 12); Pump();
            Assert(status.TranslatePoint(new Point(0, status.ActualHeight), scroll).Y <= scroll.ActualHeight + 1 && Find<Button>("RefreshPresetsButton").TranslatePoint(new Point(), scroll).Y >= 0, "Loyalsoldier refresh action and complete result are reachable at the minimum window size");
        });
        window.Width = policyWidth; window.Height = policyHeight; Pump();
        Find<Expander>("LoyalRulesPanel").IsExpanded = false;
        if (Environment.GetEnvironmentVariable("ONETUNNEL_LOYAL_UI_LIVE") == "1")
        {
            Find<TextBox>("LoyalCategoryBox").Text = "category-ads-all";
            Find<TextBox>("LoyalNameBox").Text = "loyal-large";
            var largeBefore = File.ReadAllBytes(profileFile);
            var largeTask = PrivateTask("ImportLoyalRulesAsync", "", true);
            Wait(() => largeTask.IsCompleted && PolicyIdle()); largeTask.GetAwaiter().GetResult();
            Assert(Find<TextBox>("NetworkRulesBox").Text.Length > 1_000_000 && largeBefore.SequenceEqual(File.ReadAllBytes(profileFile)), "Real large Loyalsoldier download reaches the WPF draft without saving automatically");
            Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
            Assert(NetworkProfile().GetProperty("Network").GetProperty("RuleSets").EnumerateArray().Any(s => s.GetProperty("Name").GetString() == "loyal-large"), "Large imported snapshot survives the complete WPF save/reload path");
            var beforeRefresh=File.ReadAllBytes(profileFile);
            for(int repeat=0;repeat<2;repeat++){
                Click("RefreshPresetsButton");WaitDownload(()=>PolicyIdle());
                var rows=Find<ListBox>("PresetCatalogList").Items.OfType<PresetRow>().ToArray();
                Assert(rows.Length==3&&rows.Select(row=>row.Action).SequenceEqual(new[]{"reject","direct","direct"})&&rows.All(row=>!row.Pending),"The single paired source refreshes both geosite and geoip with fixed ads/RU priority");
                Assert(rows[1].Category=="category-ru"&&rows[2].Format=="geoip"&&rows[2].Category=="ru","Paired RU routing includes domain and IP data");
            }
            Assert(beforeRefresh.SequenceEqual(File.ReadAllBytes(profileFile)),"Paired refresh preserves encrypted storage until explicit save");
            Assert(!Find<ComboBox>("PresetGeoKindBox").IsVisible&&!Find<TextBox>("LoyalCategoryBox").IsVisible&&!Find<ComboBox>("LoyalActionBox").IsVisible,"Paired source hides category, format and action editors");
            Click("SaveNetworkPreferencesButton");Wait(()=>NetworkIdle()&&LibraryIdle()&&Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        }
        var savedPresetDraft = Find<TextBox>("NetworkRulesBox").Text;
        Find<CheckBox>("RoutingNoneToggle").IsChecked=true;
        Assert(Find<TextBox>("NetworkRulesBox").Visibility == Visibility.Collapsed && Find<Expander>("LoyalRulesPanel").Visibility == Visibility.Collapsed && !Find<Button>("LoyalDownloadButton").IsEnabled, "None mode hides both mutually exclusive route editors and disables preset import");
        Wait(() => Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle());
        Console.WriteLine("TRACE none-save "+NetworkDiff());
        WaitDownload(() => Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        var noRouting = NetworkProfile().GetProperty("Network");
        Assert(noRouting.GetProperty("Rules").ValueKind == JsonValueKind.Null && noRouting.GetProperty("DNS").GetProperty("ServerIP").GetString() == "9.9.9.9", "None mode sends no routing rules and preserves the DNS policy");
        Assert((!noRouting.TryGetProperty("AllowLAN",out var noneLAN)||!noneLAN.GetBoolean())&&Find<CheckBox>("NetworkAllowLANBox").Visibility==Visibility.Collapsed,"All-through-VPN mode disables direct LAN access while keeping its preference for the other modes");
        PolicyQuery("a.loyal.example", "443", "203.0.113.9", @"C:\Synthetic\other.exe"); CheckPolicy(true);
        Assert(Find<TextBlock>("PolicyDecision").Text == "Через прокси", "Disabled preset rules do not affect the saved runtime route predictor");
        CaptureLogical(window, "routing-none.png", 960, 640, prepare: () => { var scroll=Find<ScrollViewer>("WorkspaceScroll");scroll.ScrollToVerticalOffset(scroll.VerticalOffset+Find<CheckBox>("RoutingPresetToggle").TranslatePoint(new Point(),scroll).Y-48);Pump(); });
        Find<ComboBox>("RoutingModeBox").SelectedIndex = 2;
        Assert(Find<TextBox>("NetworkRulesBox").Text == canonicalRules && !Find<TextBox>("NetworkRulesBox").IsReadOnly && Find<Expander>("LoyalRulesPanel").Visibility == Visibility.Collapsed, "Custom rules survive preset and none saves and remain separately editable");
        Find<ComboBox>("RoutingModeBox").SelectedIndex = 1;
        Assert(Find<TextBox>("NetworkRulesBox").Text == savedPresetDraft, "Preset snapshots survive save/reload while the none mode is selected");
        Find<ComboBox>("RoutingModeBox").SelectedIndex = 2;
        CaptureLogical(window, "routing-custom-minimum.png", 800, 560, prepare: () => { var scroll=Find<ScrollViewer>("WorkspaceScroll");scroll.ScrollToVerticalOffset(scroll.VerticalOffset+Find<CheckBox>("RoutingPresetToggle").TranslatePoint(new Point(),scroll).Y-48);Pump(); });
        window.Width = policyWidth; window.Height = policyHeight; Pump();
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled);
        void TraceNetwork(string stage) => Console.WriteLine($"TRACE {stage}: busy={typeof(MainWindow).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)} idle={NetworkIdle()} policyIdle={PolicyIdle()} save={Find<Button>("SaveNetworkPreferencesButton").IsEnabled} dns={Find<ComboBox>("NetworkDNSModeBox").SelectedIndex} fake={Find<CheckBox>("NetworkFakeIPBox").IsChecked} cache={Find<ComboBox>("NetworkCacheModeBox").SelectedIndex}/{Find<TextBox>("NetworkCacheCapacityBox").Text}");
        TraceNetwork("after custom save");
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 3; Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Clear();
        Find<CheckBox>("NetworkFakeIPBox").IsChecked = true; Find<ComboBox>("NetworkCacheModeBox").SelectedIndex = 2; Find<TextBox>("NetworkCacheCapacityBox").Text = "512";
        TraceNetwork("before advanced DNS save");
        Click("SaveNetworkPreferencesButton"); TraceNetwork("advanced save dispatched"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        TraceNetwork("advanced save complete");
        var advancedDNS = NetworkProfile().GetProperty("Network");
        Assert(advancedDNS.GetProperty("DNS").GetProperty("Transport").GetString() == "quic" && advancedDNS.GetProperty("DNS").GetProperty("Port").GetInt32() == 853 && advancedDNS.GetProperty("FakeIP").GetBoolean() && advancedDNS.GetProperty("DNSCache").GetProperty("Capacity").GetInt32() == 512, "DoQ, FakeIP and bounded DNS cache persist through the real Go host");
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 4; Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Text = "/custom-dns";
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(NetworkProfile().GetProperty("Network").GetProperty("DNS").GetProperty("Path").GetString() == "/custom-dns" && Find<TextBox>("NetworkPathBox").IsEnabled, "HTTP3 keeps the editable HTTPS DNS path");
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(Find<CheckBox>("NetworkFakeIPBox").IsChecked == true && Find<ComboBox>("NetworkCacheModeBox").SelectedIndex == 2 && Find<TextBox>("NetworkCacheCapacityBox").Text == "512", "Advanced DNS controls reload without dropping preferences");
        Find<ComboBox>("NetworkDNSModeBox").BringIntoView(); Pump(); Capture(window, "workspace-advanced-dns.png");
        Find<CheckBox>("NetworkFakeIPBox").IsChecked = false; Find<ComboBox>("NetworkCacheModeBox").SelectedIndex = 0; Find<TextBox>("NetworkCacheCapacityBox").Clear();
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var validDomainDNSBytes = File.ReadAllBytes(profileFile);
        Find<TextBox>("NetworkDNSRulesBox").Text = "domain | work.example | 3";
        Click("SaveNetworkPreferencesButton"); Wait(NetworkIdle);
        Assert(validDomainDNSBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Missing resolver reference cannot alter encrypted storage");
        Click("ReloadNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
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
        Find<TextBox>("NetworkResolversBox").Clear(); Find<TextBox>("NetworkDNSRulesBox").Clear(); Find<CheckBox>("NetworkAllowLANBox").IsChecked = false;
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        Assert(!NetworkProfile().TryGetProperty("Network", out var clearedNetwork) || clearedNetwork.ValueKind == JsonValueKind.Null, "Explicit default reset removes the custom network policy");
        var nativeFixture = "[Interface]\nPrivateKey = AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=\nAddress = 10.9.0.2/32\nDNS = 1.1.1.1\n[Peer]\nPublicKey = AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=\nEndpoint = 192.0.2.9:51820\nAllowedIPs = 0.0.0.0/0";
        nativeFixture += "\n[Peer]\nPublicKey = ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=\nEndpoint = 192.0.2.10:51820\nAllowedIPs = 10.10.0.0/16";
        var nativeAdded = ProfileBackend.SaveEditorAsync("Synthetic native", nativeFixture, "Native test", null, transport).GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [nativeAdded.Document, nativeAdded.Selected!.Id, true]);
        Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var nativeOriginal = Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == nativeAdded.Selected!.Id.ToString());
        PolicyQuery("example.org", "443");
        Assert(!Find<Button>("ExplainPolicyButton").IsEnabled && !Find<TextBox>("PolicyAddressBox").IsEnabled && Find<Button>("CheckPolicyDuplicatesButton").IsEnabled && Find<TextBlock>("PolicyNativeHint").Visibility == Visibility.Visible, "Native AllowedIPs disable the prediction form but retain saved-policy analysis");
        var nativeOriginalSource = nativeOriginal.GetProperty("Source").GetString();
        Assert(nativeOriginalSource == nativeFixture && nativeOriginalSource.Split("[Peer]").Length == 3, "Editor import preserves both native peers and their route ownership");
        var nativeStatus = Find<TextBlock>("NetworkPreferencesStatus").Text;
        Assert(nativeStatus.Contains("shared TUN") && nativeStatus.Contains("Policy DNS replaces imported DNS") && nativeStatus.Contains("ICMP is unavailable"), "Go reports native full-device policy scope, imported DNS override, and unavailable ICMP");
        Assert(Find<TextBox>("NetworkResolversBox").IsEnabled && Find<TextBox>("NetworkDNSRulesBox").IsEnabled && Find<CheckBox>("NetworkAllowLANBox").IsEnabled, "Native profile enables shared-TUN policy controls");
        Find<ComboBox>("NetworkDNSModeBox").SelectedIndex = 2;
        Find<TextBox>("NetworkServerIPBox").Text = "9.9.9.9";
        Find<TextBox>("NetworkServerNameBox").Text = "dns.quad9.net";
        Find<TextBox>("NetworkPortBox").Clear(); Find<TextBox>("NetworkPathBox").Clear();
        Find<ComboBox>("RoutingModeBox").SelectedIndex=2;
        Find<TextBox>("NetworkRulesBox").Text = "domain-suffix | reject | blocked.example";
        string[] ServiceProcessIdentity() => System.Diagnostics.Process.GetProcessesByName("OneTunnel.Service").Select(process => { using (process) return $"{process.Id}/{process.StartTime.ToUniversalTime().Ticks}"; }).Order().ToArray();
        var serviceBeforeNativeSave = ServiceProcessIdentity();
        var nativeController = (IConnectionController)connectionObject;
        var connectionBeforeNativeSave = (nativeController.Active, nativeController.TrafficLocked, nativeController.LocalPort, nativeController.WholeDevice);
        Click("SaveNetworkPreferencesButton"); Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var nativeSaved = Raw<JsonElement>("store.load").GetProperty("Profiles").EnumerateArray().First(p => p.GetProperty("Id").GetString() == nativeAdded.Selected!.Id.ToString());
        Assert(nativeSaved.GetProperty("Protocol").GetInt32() == 1 && nativeSaved.GetProperty("Source").GetString() == nativeOriginalSource && nativeSaved.GetProperty("Network").GetProperty("DNS").GetProperty("Transport").GetString() == "tls", "Native policy save preserves the imported WireGuard source and selected policy through real Go IPC");
        Assert(connectionBeforeNativeSave == (nativeController.Active, nativeController.TrafficLocked, nativeController.LocalPort, nativeController.WholeDevice) && serviceBeforeNativeSave.SequenceEqual(ServiceProcessIdentity()), "Saving native preferences preserves observed connection state and service process identity");
        var finalNetworkDocument = ProfileBackend.LoadDocumentAsync().GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [finalNetworkDocument, targetResult.Id, true]);
        Wait(() => NetworkIdle() && LibraryIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        HoldHostGate("Test can delay network metadata without networking");
        try { list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == imported.Selected!.Id); list.SelectedItem = list.Items.OfType<Profile>().First(p => p.Id == targetResult.Id); }
        finally { hostGate.Release(); }
        Wait(() => NetworkIdle() && Find<Button>("SaveNetworkPreferencesButton").IsEnabled);
        var renderedNetwork = (NetworkPreferencesView)typeof(MainWindow).GetField("networkPreferencesView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert(renderedNetwork.ID == targetResult.Id.ToString() && Find<TextBox>("NameBox").Text == networkDraft, "Delayed network metadata preserves newer selection and editor draft");
        window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
        Find<Expander>("AdvancedRoutingEditor").IsExpanded=true;window.UpdateLayout();Pump();
        Assert(Find<TextBox>("NetworkRulesBox").ActualWidth > 200 && Find<TextBox>("NetworkResolversBox").ActualWidth > 200 && Find<TextBox>("NetworkDNSRulesBox").ActualWidth > 200 && Find<TextBox>("NetworkServerIPBox").ActualWidth > 80, "Expanded advanced network editor retains usable controls at minimum window size");
        Capture(window, "dns-routing-minimum.png"); window.Width = 1240; window.Height = 850; Pump();
        Task PrivateTask(string method, params object?[] args) => (Task)typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args)!;
        bool BackupIdle() => !(bool)typeof(MainWindow).GetField("backupPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("backupApplyPending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Panel("engines");
        Wait(() => Find<Button>("ExportBackupButton").IsEnabled);
        var backupPath = Path.Combine(data, "synthetic-password-backup.otbackup");
        const string backupPassword = "Synthetic-backup-password-2026";
        var backupOriginal = Raw<JsonElement>("store.load");
        var backupOriginalBytes = File.ReadAllBytes(profileFile);
        var exportBackup = PrivateTask("ExportBackupToPathAsync", backupPath, backupPassword);
        Wait(() => exportBackup.IsCompleted && BackupIdle()); exportBackup.GetAwaiter().GetResult();
        var backupCipher = File.ReadAllBytes(backupPath);
        Assert(backupCipher.Length > 50 && !System.Text.Encoding.UTF8.GetString(backupCipher).Contains("Synthetic native") && backupOriginalBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Password backup writes ciphertext without changing the profile store");
        var duplicateExport = PrivateTask("ExportBackupToPathAsync", backupPath, backupPassword); Wait(() => duplicateExport.IsCompleted && BackupIdle()); duplicateExport.GetAwaiter().GetResult();
        Assert(backupCipher.SequenceEqual(File.ReadAllBytes(backupPath)), "Backup export cannot overwrite an existing destination");
        var wrongBackup = PrivateTask("PreviewBackupFromPathAsync", backupPath, "Incorrect-synthetic-password"); Wait(() => wrongBackup.IsCompleted && BackupIdle()); wrongBackup.GetAwaiter().GetResult();
        Assert(!Find<Button>("ApplyBackupRestoreButton").IsEnabled && backupOriginalBytes.SequenceEqual(File.ReadAllBytes(profileFile)) && Find<PasswordBox>("RestorePasswordBox").Password.Length == 0, "Wrong backup password leaves storage unchanged and no stale Apply action");
        var previewBackup = PrivateTask("PreviewBackupFromPathAsync", backupPath, backupPassword); Wait(() => previewBackup.IsCompleted && BackupIdle()); previewBackup.GetAwaiter().GetResult();
        Wait(() => Find<Button>("ApplyBackupRestoreButton").IsEnabled);
        Assert(Find<CheckBox>("AddRestoreSchedulesBox").IsChecked == false, "Restored schedules require a separate opt-in");
        var cancelBackup = PrivateTask("CancelBackupRestoreAsync"); Wait(() => cancelBackup.IsCompleted && BackupIdle()); cancelBackup.GetAwaiter().GetResult();
        Assert(backupOriginalBytes.SequenceEqual(File.ReadAllBytes(profileFile)) && !Find<Button>("ApplyBackupRestoreButton").IsEnabled, "Cancelling restore preview preserves the encrypted library");
        Raw<object?>("delete", new { ID = copiedOrganizationProfile.Id.ToString() });
        var reducedBackupDocument = ProfileBackend.LoadDocumentAsync().GetAwaiter().GetResult();
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [reducedBackupDocument, targetResult.Id, true]);
        Wait(() => LibraryIdle() && Find<Button>("PreviewBackupButton").IsEnabled);
        previewBackup = PrivateTask("PreviewBackupFromPathAsync", backupPath, backupPassword); Wait(() => previewBackup.IsCompleted && BackupIdle()); previewBackup.GetAwaiter().GetResult();
        Wait(() => Find<Button>("ApplyBackupRestoreButton").IsEnabled);
        var confirmedRestore = PrivateTask("ApplyBackupRestoreAsync", false); Wait(() => confirmedRestore.IsCompleted && BackupIdle() && LibraryIdle()); confirmedRestore.GetAwaiter().GetResult();
        var backupRestored = Raw<JsonElement>("store.load");
        Assert(backupRestored.GetProperty("Profiles").GetRawText() == backupOriginal.GetProperty("Profiles").GetRawText() && backupRestored.GetProperty("FavoriteIDs").GetRawText() == backupOriginal.GetProperty("FavoriteIDs").GetRawText(), "Confirmed restore replaces the full synthetic profile library and preserves settings");
        Assert(Find<TextBox>("NameBox").Text == networkDraft && Find<PasswordBox>("BackupPasswordBox").Password.Length == 0 && Find<PasswordBox>("RestorePasswordBox").Password.Length == 0, "Backup operations preserve editor drafts and clear password controls");
        Panel("subscriptions"); Click("ReloadSubscriptionsButton");
        Wait(() => Find<ListBox>("SubscriptionGroupsList").Items.Count > 0);
        Find<ListBox>("SubscriptionGroupsList").SelectedIndex = 0;
        bool ScheduleIdle() => !(bool)typeof(MainWindow).GetField("subscriptionSchedulePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)! && !(bool)typeof(MainWindow).GetField("subscriptionScheduleSavePending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Wait(() => ScheduleIdle() && Find<Button>("SaveSubscriptionScheduleButton").IsEnabled);
        Find<CheckBox>("ScheduleEnabledBox").IsChecked = true; Find<TextBox>("ScheduleIntervalBox").Text = "15";
        Click("SaveSubscriptionScheduleButton"); Wait(() => ScheduleIdle() && Find<Button>("SaveSubscriptionScheduleButton").IsEnabled);
        var scheduledGroupName = ((SubscriptionSummary)Find<ListBox>("SubscriptionGroupsList").SelectedItem).Group;
        Assert(Raw<JsonElement>("store.load").GetProperty("SubscriptionSchedules").GetProperty(scheduledGroupName).GetProperty("Enabled").GetBoolean(), "Schedule opt-in persists through the Go profile host");
        var tickNotDue = ProfileBackend.TickSubscriptionScheduleAsync().GetAwaiter().GetResult();
        Assert(tickNotDue.Preview == null, "New schedule waits its interval and does not query a provider immediately");
        Find<CheckBox>("ScheduleEnabledBox").IsChecked = false; Click("SaveSubscriptionScheduleButton"); Wait(() => ScheduleIdle() && Find<Button>("SaveSubscriptionScheduleButton").IsEnabled);
        var disabledSchedule = Raw<JsonElement>("store.load").GetProperty("SubscriptionSchedules").GetProperty(scheduledGroupName);
        Assert(!disabledSchedule.GetProperty("Enabled").GetBoolean() && disabledSchedule.GetProperty("IntervalMinutes").GetInt32() == 15, "Turning a schedule off retains its interval without automatic apply");
        Panel("connection"); Wait(() => Find<Button>("EditButton").IsEnabled);
        var trayView = ProfileBackend.TrayProfilesAsync().GetAwaiter().GetResult();
        var trayBytes = File.ReadAllBytes(profileFile);
        Assert(trayView.Profiles.Count == 4 && trayView.CanSelect && trayView.Profiles.All(p => !p.Name.Contains("socks5://")), "Actual tray metadata lists profiles without sources");
        var trayChosen = ProfileBackend.SelectTrayProfileAsync(targetResult.Id.ToString(), trayView.Token).GetAwaiter().GetResult();
        Assert(trayChosen == targetResult.Id.ToString() && trayBytes.SequenceEqual(File.ReadAllBytes(profileFile)), "Tray selection validates the exact ID without saving or connecting");
        favoritesBox.IsChecked = true; Wait(() => LibraryIdle() && OrganizationIdle() && NetworkIdle());
        var trayPendingField = typeof(MainWindow).GetField("traySelectionPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pendingTargetField = typeof(MainWindow).GetField("pendingLibrarySelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trayRetainedSnapshot = (DesktopReply)connectionSnapshot.GetValue(connectionObject)!;
        HoldHostGate("Test can delay tray visibility projection without networking");
        try {
            trayPendingField.SetValue(window, true); pendingTargetField.SetValue(window, nativeAdded.Selected!.Id);
            favoritesBox.IsChecked = false;
            connectionSnapshot.SetValue(connectionObject, trayRetainedSnapshot with { Actions = trayRetainedSnapshot.Actions! with { CanSelect = false } });
        }
        finally { hostGate.Release(); }
        Wait(LibraryIdle);
        Assert(((Profile?)list.SelectedItem)?.Id != nativeAdded.Selected!.Id, "A revoked Go selection policy blocks the pending tray target before projection applies it");
        connectionSnapshot.SetValue(connectionObject, trayRetainedSnapshot); trayPendingField.SetValue(window, false);
        bool UserSettingsIdle() => new[] { "userSettingsPending", "userSettingsTickPending", "userSettingsSavePending", "userSettingsActionPending" }.All(name => !(bool)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
        Panel("settings"); Wait(() => UserSettingsIdle() && typeof(MainWindow).GetField("userSettingsView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) != null);
        ((DispatcherTimer?)typeof(MainWindow).GetField("userSettingsTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))?.Stop();
        Assert(Find<CheckBox>("UserAutoConnectBox").IsChecked == false && Find<CheckBox>("UserSystemProxyBox").IsChecked == false && Find<CheckBox>("UserAllowTrustedBox").IsChecked == false && Find<CheckBox>("UserAllowPublicBox").IsChecked == false, "All user automation and network permissions start disabled");
        Assert(!Find<Button>("EnableUserProxyButton").IsEnabled && Find<TextBox>("UserStartupPath").Text.Length > 0, "Inactive service cannot enable a proxy; startup path is visible without registry activation");
        var automationDraft = Find<TextBox>("NameBox").Text;
        Find<CheckBox>("UserSystemProxyBox").IsChecked = true; Find<CheckBox>("UserAllowTrustedBox").IsChecked = true;
        Wait(() => Find<Button>("SaveUserSettingsButton").IsEnabled); Click("SaveUserSettingsButton"); Wait(UserSettingsIdle);
        var userPreferences = Raw<JsonElement>("store.load").GetProperty("UserSettings");
        Assert(userPreferences.GetProperty("UseSystemProxy").GetBoolean() && !userPreferences.GetProperty("AutoConnect").GetBoolean() && userPreferences.GetProperty("AllowTrusted").GetBoolean(), "Go saves proxy permission separately from activation and auto-connect");
        Assert(!Find<Button>("EnableUserProxyButton").IsEnabled && Find<TextBlock>("UserProxyStatus").Text == "Системный прокси выключен" && Find<TextBox>("NameBox").Text == automationDraft, "Saving permissions never activates a proxy or changes the editor draft");
        Find<CheckBox>("UserFullDeviceBox").IsChecked = true;
        typeof(MainWindow).GetMethod("UserSettingsTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [null, EventArgs.Empty]); Wait(UserSettingsIdle);
        Assert(Find<CheckBox>("UserFullDeviceBox").IsChecked == true, "Go status ticks preserve unsaved automation drafts");
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = true });
        var refreshUserSettings = PrivateTask("LoadUserSettingsAsync", false); Wait(() => refreshUserSettings.IsCompleted && UserSettingsIdle()); refreshUserSettings.GetAwaiter().GetResult();
        Assert(Find<CheckBox>("UserFullDeviceBox").IsChecked == true && !Find<Button>("SaveUserSettingsButton").IsEnabled && Find<TextBlock>("UserSettingsStatus").Text.Contains("введённые"), "Changed Go revision keeps the draft but blocks a stale preference save");
        Raw<object?>("favorite.set", new { ID = targetResult.Id.ToString(), Favorite = false });
        Click("ReloadUserSettingsButton"); Wait(UserSettingsIdle);
        var userView = (UserSettingsView)typeof(MainWindow).GetField("userSettingsView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var selectedAutomationProfile = ((System.Collections.IEnumerable)Find<ComboBox>("UserAutoConnectProfileBox").ItemsSource).Cast<Profile>().First();
        var activeAutomationView = userView with { Preferences = userView.Preferences with { AutoConnect = true, ProfileID = selectedAutomationProfile.Id.ToString() }, CanEnableProxy = true };
        typeof(MainWindow).GetMethod("PresentUserSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [activeAutomationView, true]);
        var automationDocument = Raw<ProfileDocument>("store.load");
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [automationDocument, selectedAutomationProfile.Id, false]);
        Assert(Find<ComboBox>("UserAutoConnectProfileBox").SelectedItem is Profile reboundAutomationProfile && reboundAutomationProfile.Id == selectedAutomationProfile.Id && Find<Button>("EnableUserProxyButton").IsEnabled, "Refreshing profiles preserves the automatic profile and does not create a draft that blocks proxy activation");
        Find<CheckBox>("UserFullDeviceBox").IsChecked = true;
        typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [automationDocument, selectedAutomationProfile.Id, false]);
        Assert(Find<CheckBox>("UserFullDeviceBox").IsChecked == true && Find<ComboBox>("UserAutoConnectProfileBox").SelectedItem is Profile draftAutomationProfile && draftAutomationProfile.Id == selectedAutomationProfile.Id && !Find<Button>("EnableUserProxyButton").IsEnabled, "Refreshing profiles preserves a real unsaved automation draft");
        typeof(MainWindow).GetMethod("PresentUserSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [userView with { StartupRecorded = true, StartupEnabled = false }, false]);
        Assert(Find<TextBlock>("UserStartupStatus").Text.Contains("отсутствует или изменена"), "Recorded startup ownership is not presented as a confirmed active registration");
        typeof(MainWindow).GetMethod("PresentUserSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [userView, true]);
        Find<CheckBox>("UserSystemProxyBox").IsChecked = false; Find<CheckBox>("UserAllowTrustedBox").IsChecked = false;
        Find<CheckBox>("UserAllowPublicBox").IsChecked = false; Find<CheckBox>("UserFullDeviceBox").IsChecked = false; Find<ComboBox>("UserAutoConnectProfileBox").SelectedItem = null;
        Wait(() => Find<Button>("SaveUserSettingsButton").IsEnabled); Click("SaveUserSettingsButton"); Wait(UserSettingsIdle);
        var resetUserDocument = Raw<JsonElement>("store.load");
        Assert(resetUserDocument.TryGetProperty("UserSettings",out var resetUserPreferences)&&!resetUserPreferences.GetProperty("AutoConnect").GetBoolean()&&!resetUserPreferences.GetProperty("UseSystemProxy").GetBoolean()&&!resetUserPreferences.GetProperty("Transport").GetProperty("Mux").GetBoolean()&&!resetUserPreferences.GetProperty("Transport").GetProperty("Fragment").GetBoolean()&&!resetUserPreferences.GetProperty("Transport").GetProperty("Noise").GetBoolean(),"Clearing common preferences disables automation and transport flags without registry effects");
        Assert(Find<TextBox>("NameBox").Text == automationDraft && Raw<JsonElement>("store.load").GetProperty("FavoriteIDs").GetArrayLength() == 1, "Automation tools preserve editor drafts and unrelated favorites");
        typeof(MainWindow).GetMethod("SetGrouping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
        Panel("connection");
        var statsSnapshot = (DesktopReply)connectionSnapshot.GetValue(connectionObject)!;
        var statsDisplay = statsSnapshot.Display! with { ShowStatistics = true, Uploaded = "Uploaded: 1.00 MiB", Downloaded = "Downloaded: 2.00 MiB", UploadRate = "Upload: —", DownloadRate = "Download: 2 KiB/s", Elapsed = "Elapsed: 00:01:02", StatisticsScopeHint = "Current attempt; payload and packet bytes" };
        connectionSnapshot.SetValue(connectionObject, statsSnapshot with { Display = statsDisplay });
        typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Assert(Find<StackPanel>("StatisticsPanel").Visibility == Visibility.Visible && Find<TextBlock>("StatisticsUploaded").Text == statsDisplay.Uploaded && Find<TextBlock>("StatisticsUploadRate").Text == statsDisplay.UploadRate && Find<TextBlock>("StatisticsElapsed").Text == statsDisplay.Elapsed, "Statistics render Go display strings unchanged, including unavailable rates");
        connectionSnapshot.SetValue(connectionObject, statsSnapshot with { Display = statsDisplay with { ShowStatistics = false } });
        typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Assert(Find<StackPanel>("StatisticsPanel").Visibility == Visibility.Collapsed, "Older service projections hide unavailable statistics");
        var statsContract = new ConnectionStatistics(1, "synthetic-session", 62000, 1048576, 2097152, null, 2048, "proxy-payload");
        var statsRoundTrip = JsonSerializer.Deserialize<CoreStatus>(JsonSerializer.Serialize(statsSnapshot.Response.State with { Statistics = statsContract, ProxyListenerStable = true }));
        Assert(statsRoundTrip?.Statistics == statsContract && statsRoundTrip.ProxyListenerStable, "Service statistics survive the retained C# observation contract without recomputation");
        connectionSnapshot.SetValue(connectionObject, statsSnapshot);
        typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        var uiBusyField=typeof(MainWindow).GetField("busy",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var browseSelection=list.Items.OfType<Profile>().First();
        var browseOther=list.Items.OfType<Profile>().First(p=>p.Id!=browseSelection.Id);
        var activeView=statsSnapshot with {Response=statsSnapshot.Response with {State=statsSnapshot.Response.State with {Active=true,ProfileId=browseSelection.Id}},Actions=new(CanSelect:true,CanDisconnect:true,CanToggle:true,ConnectCaption:"Отключить")};
        typeof(MainWindow).GetMethod("SetGrouping",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);Panel("connection");Pump();
        HoldHostGate("Test can delay a pending connection command without changing the real service");
        try {
            uiBusyField.SetValue(window,true);
            // Drain the preceding context replies before installing the fake session.
            Pump();
            connectionSnapshot.SetValue(connectionObject,activeView);
            typeof(MainWindow).GetMethod("RefreshControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Click("UserSettingsNavigationButton");Pump();Assert(Find<FrameworkElement>("UserSettingsPanel").Visibility==Visibility.Visible,"Settings navigation remains responsive while a connection command is pending");
            typeof(MainWindow).GetMethod("Home_Click",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);Pump();
            list.SelectedItem=browseOther;Pump();
            Assert(list.IsEnabled&&((Profile)list.SelectedItem).Id==browseOther.Id&&((OneTunnel.Client.ServiceConnection)connectionObject).ProfileId==browseSelection.Id,$"Browsing another server cannot replace the active session or lock the server list; enabled={list.IsEnabled}, selected={((Profile?)list.SelectedItem)?.Id}, expected={browseOther.Id}, session={((OneTunnel.Client.ServiceConnection)connectionObject).ProfileId}, active={browseSelection.Id}");
            var busyGroup=Descendants(list).OfType<Expander>().First(e=>e.DataContext is System.Windows.Data.CollectionViewGroup);
            var wasExpanded=busyGroup.IsExpanded;busyGroup.IsExpanded=!wasExpanded;Pump();
            Assert(busyGroup.IsExpanded!=wasExpanded,"Collapsing a group never synchronously waits for a pending connection command");
            busyGroup.IsExpanded=wasExpanded;Pump();
            Assert(Find<Button>("SaveProfileButton").IsEnabled==false||!((OneTunnel.Client.ServiceConnection)connectionObject).Actions.CanSave,"Viewing during a connection does not grant profile mutation permission");
        } finally {uiBusyField.SetValue(window,false);connectionSnapshot.SetValue(connectionObject,statsSnapshot);hostGate.Release();}
        Wait(()=>LibraryIdle()&&NetworkIdle()&&OrganizationIdle());
        typeof(MainWindow).GetMethod("RefreshControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        int traySelections = 0;
        using (var menuFixture = new TrayShell(() => { }, () => { }, () => { }, () => { }, () => { })) {
            menuFixture.PresentProfiles(trayView, (_, _) => traySelections++);
            var previousMenuItem = (System.Windows.Forms.ToolStripMenuItem)menuFixture.TrayProfilesMenuItem.DropDownItems[0];
            previousMenuItem.PerformClick(); Assert(traySelections == 1, "Enabled tray item forwards one selection intent");
            menuFixture.ClearProfiles(); Assert(previousMenuItem.IsDisposed, "Tray refresh disposes removed profile items"); previousMenuItem.PerformClick();
            Assert(traySelections == 1, "Refreshing tray profiles disables stale selection callbacks");
            menuFixture.PresentProfiles(trayView with { CanSelect = false }, (_, _) => traySelections++);
            ((System.Windows.Forms.ToolStripMenuItem)menuFixture.TrayProfilesMenuItem.DropDownItems[0]).PerformClick();
            Assert(traySelections == 1, "Go-disabled tray profile cannot select or connect");
        }
        // Design-only data remains inside this suite's synthetic store; no connection command is sent.
        var referenceOriginal = Raw<JsonElement>("store.load").GetRawText();
        var changedEvent = connectionObject.GetType().GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalChangedEvent = changedEvent.GetValue(connectionObject);
        changedEvent.SetValue(connectionObject, null);
        try {
            ((DispatcherTimer?)typeof(MainWindow).GetField("userSettingsTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))?.Stop();
            var empty = JsonNode.Parse(referenceOriginal)!.AsObject(); empty["Profiles"] = new JsonArray(); empty["FavoriteIDs"] = new JsonArray();
            Raw<object?>("store.save", new { Document = empty });
            AppliedProfiles? lastReference = null;
            var referenceNames = new[] { "🇳🇱 Amsterdam", "🇫🇮 Helsinki", "🇺🇸 Office", "Proxy", "🇺🇸 New York", "🇯🇵 Tokyo", "🇩🇪 Frankfurt", "🇸🇬 Singapore" };
            var referenceProtocols = new[] { Protocol.VLESS, Protocol.AmneziaWG, Protocol.WireGuard, Protocol.SOCKS5, Protocol.VLESS, Protocol.Shadowsocks, Protocol.WireGuard, Protocol.Hysteria2 };
            var referenceTimings = new long[] { 42, 68, 21, 93, 112, 184, 57, 210 };
            for (int i = 0; i < 8; i++) lastReference = ProfileBackend.SaveEditorAsync(referenceNames[i], $"socks5://192.0.2.{i + 1}:1080", i < 3 ? "Личные" : i < 6 ? "Работа" : "Прокси", null, transport, new()).GetAwaiter().GetResult();
            var demoProfiles = lastReference!.Document.Profiles.Select((profile, i) => profile with { Protocol = referenceProtocols[i] }).ToList();
            var demoDocument = lastReference.Document with { Profiles = demoProfiles };
            typeof(MainWindow).GetMethod("PresentDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [demoDocument, demoProfiles[0].Id, true]);
            typeof(MainWindow).GetMethod("SetGrouping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
            Panel("connection"); search.Clear(); Find<CheckBox>("FavoritesOnlyBox").IsChecked = false;
            Wait(() => LibraryIdle() && NetworkIdle() && OrganizationIdle() && list.Items.Count == 8);
            for (int settling = 0; settling < 5; settling++) Pump();
            var demoDisplay = statsSnapshot.Display! with { Status = "Подключено", ShowSession = true, ModeIndex = 1, ShowStatistics = true, Uploaded = "28,7 МБ", Downloaded = "12,4 МБ", Elapsed = "00:18:32" };
            var demoState = statsSnapshot.Response.State with { Active = true, WholeDevice = true, FullDeviceMode = true, ProfileId = demoProfiles[0].Id };
            var demoSnapshot = statsSnapshot with { Response = statsSnapshot.Response with { State = demoState }, Display = demoDisplay, Actions = new(CanDisconnect: true, CanToggle: true, CanSelect: true, CanEdit: true, ConnectCaption: "Отключить") };
            connectionSnapshot.SetValue(connectionObject, demoSnapshot);
            RenderResults(new("Демонстрация", "2026-10-28", demoProfiles.Select((profile, i) => new GroupResultDisplayRow(profile.Id.ToString(), profile.Name, "Демо", true, true, referenceTimings[i], "Демонстрационная задержка", referenceTimings[i])).ToList(), true, false, false, "demo-only", ""));
            typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Find<Border>("FeedbackNotice").Visibility = Visibility.Collapsed;
            Find<TextBlock>("FooterStatus").Text += " · демонстрация без сети";
            CaptureLogical(window, "A-control-connected.png", 1280, 760);
            list.SelectedItem = demoProfiles[1];
            Wait(() => LibraryIdle() && NetworkIdle() && OrganizationIdle());
            for (int settling = 0; settling < 5; settling++) Pump();
            connectionSnapshot.SetValue(connectionObject, demoSnapshot);
            typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert(((Profile)list.SelectedItem).Id == demoProfiles[1].Id && Find<TextBlock>("StatusLabel").Text == "Не подключено" && Find<TextBlock>("FooterStatus").Text.Contains("Amsterdam"), "Selected and active profiles remain distinct in the design fixture");
            Find<TextBlock>("FooterStatus").Text += " · демонстрация без сети";
            CaptureLogical(window, "B-control-other-selected.png", 1280, 760);
            list.SelectedItem = demoProfiles[0];
            Wait(() => LibraryIdle() && NetworkIdle() && OrganizationIdle());
            for (int settling = 0; settling < 5; settling++) Pump();
            connectionSnapshot.SetValue(connectionObject, demoSnapshot);
            typeof(MainWindow).GetMethod("PresentConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Find<TextBlock>("FooterStatus").Text += " · демонстрация без сети";
            CaptureLogical(window, "D-control-minimum.png", 1060, 641);
            CaptureLogical(window, "D-control-scale125.png", 1280, 760, 1.25);
            CaptureLogical(window, "D-control-scale150.png", 1280, 760, 1.5);
            connectionSnapshot.SetValue(connectionObject, statsSnapshot); Panel("settings"); Pump();
            CaptureLogical(window, "C-settings.png", 960, 640);
            CaptureLogical(window, "D-settings-scale150.png", 960, 640, 1.5);
            var popupBox = Find<ComboBox>("UserAutoConnectProfileBox");
            popupBox.IsEnabled = true; popupBox.IsDropDownOpen = true; Pump();
            var popup = (System.Windows.Controls.Primitives.Popup)popupBox.Template.FindName("PART_Popup", popupBox);
            Assert(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 10 }, "Dark ComboBox popup opens with a measured visual surface on the VM");
            popupBox.IsDropDownOpen = false;
            var groupEmpty=JsonNode.Parse(referenceOriginal)!.AsObject();groupEmpty["Profiles"]=new JsonArray();groupEmpty["FavoriteIDs"]=new JsonArray();Raw<object?>("store.save",new{Document=groupEmpty});
            var longGroup="Подписка команды — европейские серверы с очень длинным названием для проверки заголовка";
            AppliedProfiles? many=null;
            for(int g=0;g<2;g++)many=ProfileBackend.SaveEditorAsync(g==0?longGroup:"Локальный набор",string.Join("\n",Enumerable.Range(10,50).Select(i=>$"socks5://{(g==0?"192.0.2":"198.51.100")}.{i}:1080")),"",null,transport,new()).GetAwaiter().GetResult();
            var manyRaw=JsonNode.Parse(Raw<JsonElement>("store.load").GetRawText())!.AsObject();manyRaw["SubscriptionSources"]![longGroup]="https://subscription.example/fixture-only";Raw<object?>("store.save",new{Document=manyRaw});
            typeof(MainWindow).GetMethod("PresentDocument",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[many!.Document,many.Selected!.Id,true]);
            typeof(MainWindow).GetMethod("SetGrouping",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);Panel("connection");search.Clear();
            Wait(()=>LibraryIdle()&&NetworkIdle()&&OrganizationIdle()&&list.Items.Count==100);for(int settle=0;settle<4;settle++)Pump();list.IsEnabled=true;
            CaptureLogical(window,"group-actions-50.png",1280,760);
            var libraryScroll=(ScrollViewer)list.Template.FindName("PART_ScrollViewer",list);
            Console.WriteLine($"TRACE pixel list={list.Items.Count} unit={VirtualizingPanel.GetScrollUnit(list)} extent={libraryScroll.ExtentHeight} viewport={libraryScroll.ViewportHeight} scrollable={libraryScroll.ScrollableHeight}");
            Assert(VirtualizingPanel.GetScrollUnit(list)==ScrollUnit.Pixel&&VirtualizingPanel.GetIsVirtualizingWhenGrouping(list)&&libraryScroll.ScrollableHeight>2000,"Large groups use virtualized pixel scrolling");
            libraryScroll.ScrollToVerticalOffset(17);Pump();Assert(Math.Abs(libraryScroll.VerticalOffset-17)<1,"Pixel scrolling stops within a server row instead of skipping a whole group");
            var wheelBefore=libraryScroll.VerticalOffset;libraryScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.MouseWheelEvent});Pump();
            Assert(libraryScroll.VerticalOffset>wheelBefore&&libraryScroll.VerticalOffset-wheelBefore<libraryScroll.ViewportHeight,"One wheel step scrolls a small pixel distance within a 50-server group");
            libraryScroll.ScrollToHome();Pump();list.IsEnabled=false;Pump();
            var librarySurface=(Border)list.Template.FindName("ServerLibrarySurface",list);
            Assert(((SolidColorBrush)librarySurface.Background).Color==((SolidColorBrush)app.Resources["Canvas"]).Color,"Disabled server library keeps its dark surface instead of Windows white disabled chrome");
            CaptureLogical(window,"group-actions-disabled.png",1280,760);
            list.IsEnabled=true;CaptureLogical(window,"group-actions-minimum.png",1060,641);
            Console.WriteLine("SKIP: native mouse hover, global keys and real monitor DPI need an interactive user session; scale captures are offscreen rasterization checks.");
        }
        finally {
            Raw<object?>("store.save", new { Document = JsonNode.Parse(referenceOriginal) });
            connectionSnapshot.SetValue(connectionObject, statsSnapshot);
            changedEvent.SetValue(connectionObject, originalChangedEvent);
        }

        typeof(MainWindow).GetField("allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
        Assert(tray!.StatusItem.IsDisposed && tray.ExitItem.IsDisposed, "Window Close disposes the tray even on the capture/test close path");
        if (Environment.GetEnvironmentVariable("ONETUNNEL_UI_HEADLESS") != "1") CheckHiddenWindowClose();
        Assert(registryBefore.SequenceEqual(UserRegistryFingerprint()), "Isolated UI checks leave current-user proxy and startup registry settings unchanged");
        app.Shutdown();
        Console.WriteLine("PASS: isolated WPF profile search, empty results, navigation, existing settings, default/minimum layout and storage-preservation checks. No connect/probe/service mutation commands sent.");
        Console.WriteLine(Output);
        return 0;
    }
    private static byte[] UserRegistryFingerprint()
    {
        var values = new List<object>();
        foreach (var item in new[] {
            (Key: @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", Names: new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect" }),
            (Key: @"Software\Microsoft\Windows\CurrentVersion\Run", Names: new[] { "OneTunnel" }) })
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(item.Key, writable: false);
            var existing = key?.GetValueNames() ?? [];
            foreach (string name in item.Names)
            {
                bool exists = existing.Contains(name, StringComparer.OrdinalIgnoreCase);
                values.Add(new { item.Key, Name = name, Exists = exists, Kind = exists ? (int)key!.GetValueKind(name) : -1,
                    Value = exists ? key!.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) : null });
            }
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(values);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    private static nint CaptureWindowBounds(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0024) {
            Marshal.WriteInt32(lParam, 32, 1920); Marshal.WriteInt32(lParam, 36, 1280);
            handled = true;
        }
        return 0;
    }
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
    private static void CaptureLogical(Window window, string name, double width, double height, double scale = 1, Action? prepare = null)
    {
        var content = (FrameworkElement)window.Content;
        double nativeNonClientHeight = window.ActualHeight - content.ActualHeight;
        // Test-only: WPF caches the small session0 desktop's tracking limits.
        foreach (var namePart in new[] { "_trackMaxWidthDeviceUnits", "_windowMaxWidthDeviceUnits" }) typeof(Window).GetField(namePart, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2048d);
        foreach (var namePart in new[] { "_trackMaxHeightDeviceUnits", "_windowMaxHeightDeviceUnits" }) typeof(Window).GetField(namePart, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 1600d);
        window.Width = width + 16; window.Height = height + 39;
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(90) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        window.UpdateLayout();
        Assert(Math.Abs(content.ActualWidth - width) < .1 && Math.Abs(content.ActualHeight - height) < .1, "Native test window has the requested client dimensions for " + name);
        prepare?.Invoke();
        if (name == "A-control-connected.png") {
            var inspector = (FrameworkElement)window.FindName("InspectorPanel");
            var profileList = (ListBox)window.FindName("ProfilesList");
            Assert(Math.Abs(inspector.TranslatePoint(new Point(0, 0), content).X - 940) < .1 && Math.Abs(inspector.ActualWidth - 340) < .1 && Math.Abs(((Grid)content).ColumnDefinitions[0].ActualWidth - 208) < .1, "Control reference geometry is exactly 208 / 1 / 730 / 1 / 340 DIP");
            Assert(profileList.ItemContainerGenerator.ContainerFromIndex(7) is FrameworkElement last && Math.Abs(last.ActualHeight - 50) < .1 && last.TranslatePoint(new Point(0, last.ActualHeight), content).Y < height - 30, "Eight 50-DIP server rows fit above the single footer");
        }
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height), Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Output, name))) encoder.Save(file);
        var metrics = new { LogicalWidth = width, LogicalHeight = height, RenderScale = scale,
            NativeDpi = VisualTreeHelper.GetDpi(content).PixelsPerInchX,
            NativeNonClientHeight = nativeNonClientHeight,
            Sidebar = ((Grid)content).ColumnDefinitions[0].ActualWidth,
            Inspector = ((FrameworkElement)window.FindName("InspectorPanel")).ActualWidth,
            BodyWidth = content.ActualWidth, BodyHeight = content.ActualHeight,
            Font = window.FontFamily.Source,
            FontAvailable = Fonts.SystemFontFamilies.Any(font => font.Source == "Segoe UI Variable") };
        File.WriteAllText(Path.Combine(Output, name + ".json"), JsonSerializer.Serialize(metrics));
        content.InvalidateMeasure(); window.UpdateLayout();
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill }, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        if (!Enumerable.Range(0, bitmap.PixelWidth * bitmap.PixelHeight).Any(i => pixels[i * 4 + 3] != 0))
            File.WriteAllText(Path.Combine(Output, name + ".unrendered.txt"), "No rendered pixels in this VM session; layout/control checks are separate from visual acceptance.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Output, name)); encoder.Save(stream);
    }
}
