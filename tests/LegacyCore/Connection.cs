using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace OneTunnel.Core;
public sealed record Session(string Kind, string Engine, string ConfigPath, string TunnelName, int Pid = 0, long Started = 0, int XrayPid = 0, long XrayStarted = 0, bool FullDevice = false);
public sealed class Connection
{
    private Process? process;
    private Process? xray;
    private ProcessJob? job;
    private Session? session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Action<string>? diagnostic;
    private readonly Func<Task<ClockPlan>> clockPlanner;
    public Connection(Action<string>? diagnostic = null, Func<Task<ClockPlan>>? clockPlanner = null) { this.diagnostic = diagnostic; this.clockPlanner = clockPlanner ?? ShadowsocksNative.PlanClockAsync; }
    public bool FullDeviceMode => session?.FullDevice == true;
    public bool TrafficLocked => session?.FullDevice == true && KillSwitch.NeedsRecovery;
    public bool WholeDevice { get; private set; }
    public bool ClockSynchronized { get; private set; }
    public bool Active => session != null;
    public int LocalPort { get; private set; }
    public string Status { get; private set; } = "Отключено";
    public event Action? Changed;
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static string Journal => Path.Combine(Storage.Root, "session.json");
    public static bool NeedsRecovery => File.Exists(Journal) || KillSwitch.NeedsRecovery;
    public static string XrayPath => Path.Combine(AppContext.BaseDirectory, "engines", "xray.exe");
    private static int FreePort()
    {
        for (int i = 0; i < 40; i++)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Server.ExclusiveAddressUse = true; listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try { using var udp = new UdpClient(AddressFamily.InterNetwork); udp.ExclusiveAddressUse = true; udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, port)); return port; }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("Не удалось выделить свободный локальный TCP/UDP-порт.");
    }
    public static string EnginePath(Protocol protocol)
    {
        var file = protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "amneziawg.exe" : "sing-box.exe";
        return Path.Combine(AppContext.BaseDirectory, "engines", file);
    }
    public static string ServiceName(Session s) => (s.Kind == "WireGuard" ? "WireGuardTunnel$" : "AmneziaWGTunnel$") + s.TunnelName;
    private void Notify(string status) { Status = status; Changed?.Invoke(); }
    private void SaveJournal() { File.WriteAllText(Journal + ".new", JsonSerializer.Serialize(session)); File.Move(Journal + ".new", Journal, true); }
    public Task StartAsync(Profile profile, bool tun) => StartInternalAsync(profile, tun, null);
    public Task StartPreparedAsync(Profile profile, bool tun, PreparedConnection plan) => StartInternalAsync(profile, tun, plan);
    private async Task StartInternalAsync(Profile profile, bool tun, PreparedConnection? plan)
    {
        if (plan == null && profile.Transport is { } options && (options.Fragment || options.Noise || options.Mux))
            throw new InvalidOperationException("Фрагментация, шумы и MUX требуют обновлённую Go-службу. Используй подключение через интерфейс OneTunnel.");
        await gate.WaitAsync();
        bool attempted = false;
        string? pendingPath = null;
        try
        {
            if (Active || NeedsRecovery) throw new InvalidOperationException("Сначала отключи текущее подключение или выполни восстановление.");
            if (plan == null)
            {
                profile = ProfileParser.Parse(profile.Name, profile.Source, profile.Protocol) with { Id = profile.Id };
                if (tun) profile = TunnelPolicy.ForFullDevice(profile);
            }
            else if (profile.Protocol is Protocol.WireGuard or Protocol.AmneziaWG) profile = profile with { Source = plan.NativeSource };
            bool useNativeShadowsocks = plan?.NativeShadowsocks ?? ShadowsocksNative.CanUse(profile);
            bool needsXray = plan?.NeedsXray ?? (ProxyCore.NeedsXray(profile.Protocol) && !useNativeShadowsocks);
            var engine = EnginePath(profile.Protocol);
            if (!File.Exists(engine)) throw new InvalidOperationException("Движок отсутствует. Открой раздел «Движки» и установи компоненты.");
            if (needsXray && !File.Exists(XrayPath)) throw new InvalidOperationException("Для этого протокола нужен xray.exe в папке engines. Используй полную сборку версии 0.8.");
            if ((tun || profile.Protocol is Protocol.WireGuard or Protocol.AmneziaWG) && !IsAdministrator) throw new InvalidOperationException("Для туннеля нужны права администратора. Отключи подключения и нажми «Перезапустить с правами администратора» в разделе «Движки».");
            WholeDevice = tun || profile.Protocol is Protocol.WireGuard or Protocol.AmneziaWG;
            attempted = true;
            ClockSynchronized = false;
            Notify("Запуск…");
            string tunnel = "OneTunnel" + profile.Id.ToString("N")[..12];
            string path = Path.Combine(Storage.Runtime, tunnel + (profile.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? ".conf" : ".json"));
            pendingPath = path;
            if (ProxyCore.IsProxy(profile.Protocol))
            {
                // Reserve a free port and fail if another process claims it before our engine.
                LocalPort = plan?.LocalPort ?? FreePort();
                int? bridgePort = plan == null ? needsXray ? FreePort() : null : plan.BridgePort;
                while (plan == null && bridgePort == LocalPort) bridgePort = FreePort();
                session = new("proxy", engine, path, tunnel, FullDevice: tun); SaveJournal();
                job = new ProcessJob();
                if (bridgePort is { } bridge)
                {
                    string xrayConfig = path + ".xray.json";
                    var json = System.Text.Json.Nodes.JsonNode.Parse(plan?.Xray ?? ProxyCore.Build(profile, bridge))!;
                    if (diagnostic != null) json["log"]!["loglevel"] = "debug";
                    if (tun)
                    {
                        await PrepareTunEndpoint(json);
                        BindTunUplink(json);
                    }
                    File.WriteAllText(xrayConfig, json.ToJsonString());
                    if (await RunAsync(XrayPath, ["run", "-test", "-config", xrayConfig]) != 0) throw new InvalidOperationException("Xray отклонил конфигурацию. Проверь протокол, транспорт, TLS/REALITY и дополнительные параметры.");
                    xray = new Process { StartInfo = Info(XrayPath, ["run", "-config", xrayConfig]) };
                    xray.OutputDataReceived += (_, e) => { if (e.Data != null) diagnostic?.Invoke(e.Data); };
                    xray.ErrorDataReceived += (_, e) => { if (e.Data != null) diagnostic?.Invoke(e.Data); };
                    xray.Start(); job.Add(xray); xray.BeginOutputReadLine(); xray.BeginErrorReadLine();
                    session = session with { XrayPid = xray.Id, XrayStarted = xray.StartTime.ToUniversalTime().Ticks }; SaveJournal();
                    await WaitReady(xray, bridge);
                }
                ClockPlan? clock = null;
                if (plan?.AccurateTime ?? (useNativeShadowsocks && ShadowsocksNative.RequiresAccurateTime(profile))) { Notify("Проверка времени для Shadowsocks 2022…"); clock = await clockPlanner(); }
                var clientConfig = System.Text.Json.Nodes.JsonNode.Parse(plan?.Proxy ?? ProfileParser.BuildProxyConfig(profile, LocalPort, tun, bridgePort, clock))!;
                if (plan != null && clock?.Synchronize == true)
                {
                    // Runtime clock discovery stays with Windows operations;
                    // protocol/cipher selection and static configuration are Go-owned.
                    clientConfig["ntp"] = System.Text.Json.Nodes.JsonNode.Parse(plan.Clock)!;
                    clientConfig["ntp"]!["server"] = clock.Server;
                    clientConfig["ntp"]!["server_port"] = clock.Port;
                    clientConfig["log"]!["level"] = "info";
                }
                if (tun) clientConfig["log"]!["level"] = "info";
                if (tun && bridgePort == null && clientConfig["outbounds"]![0]!["server"] is { } serverNode)
                {
                    string serverHost = serverNode.GetValue<string>();
                    if (!IPAddress.TryParse(serverHost, out _))
                    {
                        var ips = await Dns.GetHostAddressesAsync(serverHost).WaitAsync(TimeSpan.FromSeconds(10));
                        clientConfig["outbounds"]![0]!["server"] = (ips.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ?? ips.First()).ToString();
                    }
                }
                File.WriteAllText(path, clientConfig.ToJsonString());
                var check = await RunAsync(engine, ["check", "-c", path]);
                if (check != 0) { File.Delete(path); throw new InvalidOperationException("Движок отклонил конфигурацию. Проверь параметры ссылки и совместимость версии sing-box."); }
                process = new Process { StartInfo = Info(engine, ["run", "-c", path]), EnableRaisingEvents = true };
                var timeReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var engineReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Inspect(string? line)
                {
                    if (line == null) return;
                    if (line.Contains("sing-box started", StringComparison.OrdinalIgnoreCase)) engineReady.TrySetResult();
                    if (clock?.Synchronize == true && line.Contains("ntp", StringComparison.OrdinalIgnoreCase) && line.Contains("updated time", StringComparison.OrdinalIgnoreCase)) { ClockSynchronized = true; timeReady.TrySetResult(); }
                    diagnostic?.Invoke(line);
                }
                process.OutputDataReceived += (_, e) => Inspect(e.Data); process.ErrorDataReceived += (_, e) => Inspect(e.Data);
                if (!process.Start()) throw new InvalidOperationException("Не удалось запустить движок.");
                job.Add(process);
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                session = session with { Pid = process.Id, Started = process.StartTime.ToUniversalTime().Ticks }; SaveJournal();
                bool ready = false;
                for (int i = 0; i < 40; i++)
                {
                    if (process.HasExited) throw new InvalidOperationException("Сетевой движок завершился при запуске. Проверь конфигурацию, драйвер и доступность сети.");
                    try { using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, LocalPort).WaitAsync(TimeSpan.FromMilliseconds(150)); ready = true; break; } catch (SocketException) { } catch (TimeoutException) { }
                    await Task.Delay(100);
                }
                if (!ready) throw new InvalidOperationException("Локальный прокси не запустился за отведённое время.");
                if (tun)
                {
                    try { await engineReady.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
                    catch (TimeoutException) { throw new InvalidOperationException("TUN не завершил настройку маршрутов и защиты DNS за 30 секунд."); }
                    if (!SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")).Equals("OneTunnel", StringComparison.Ordinal) || !SystemNetworkProbe.RouteInterface(IPAddress.Parse("2606:4700:4700::1111")).Equals("OneTunnel", StringComparison.Ordinal))
                        throw new InvalidOperationException("Windows не направила IPv4 и IPv6 в TUN. Подключение остановлено.");
                }
                if (clock?.Synchronize == true) { try { await timeReady.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { throw new InvalidOperationException("Не удалось синхронизировать время внутри приложения. Для Shadowsocks 2022 нужны точные часы: разреши NTP (UDP 123) или синхронизируй время Windows."); } }
                Notify(tun ? "Туннель запущен · трафик ещё не проверен" : "Прокси запущен · трафик ещё не проверен");
            }
            else
            {
                File.WriteAllText(path, profile.Source);
                session = new("AmneziaWG", engine, path, tunnel, FullDevice: tun); SaveJournal();
                int exit = await RunAsync(engine, ["/installtunnelservice", path]);
                if (exit != 0) throw new InvalidOperationException($"Не удалось создать туннель (код {exit}). Проверь конфигурацию и наличие драйвера.");
                bool running = false;
                for (int i = 0; i < 30; i++) { if (ServiceRunning(ServiceName(session))) { running = true; break; } await Task.Delay(250); }
                if (!running) throw new InvalidOperationException("Служба туннеля не перешла в рабочее состояние.");
                if (tun)
                {
                    bool routesReady = false;
                    for (int i = 0; i < 120; i++)
                    {
                        if (!ServiceRunning(ServiceName(session))) break;
                        if (SystemNetworkProbe.RouteInterface(IPAddress.Parse("1.1.1.1")) == tunnel) { routesReady = true; break; }
                        await Task.Delay(250);
                    }
                    if (!routesReady) throw new InvalidOperationException("Служба запущена, но Windows не установила полный маршрут WG/AWG. Подключение остановлено.");
                }
                Notify("Туннель запущен · handshake ещё не проверен");
            }
            if (tun)
            {
                Notify("Включение блокировки прямого трафика…");
                KillSwitch.Enable(ProxyCore.IsProxy(profile.Protocol) ? "OneTunnel" : tunnel, needsXray ? new[] { engine, XrayPath } : new[] { engine });
                SystemNetworkProbe.FlushDnsCache();
                Notify("Всё устройство · kill switch включён");
            }
        }
        catch
        {
            if (attempted) { try { await StopInternal(); if (pendingPath != null && !NeedsRecovery) File.Delete(pendingPath); } catch { Notify("Ошибка · требуется восстановление"); } }
            throw;
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync(); try { await StopInternal(); } finally { gate.Release(); }
    }
    public async Task RecoverAsync()
    {
        await gate.WaitAsync();
        try
        {
            var saved = session;
            if (saved == null && File.Exists(Journal)) saved = JsonSerializer.Deserialize<Session>(File.ReadAllText(Journal));
            if (saved != null)
            {
                // Never execute a path supplied by the journal; only our bundled engine and runtime files.
                if (!System.Text.RegularExpressions.Regex.IsMatch(saved.TunnelName, "^OneTunnel[0-9a-f]{12}$") || saved.Kind is not ("proxy" or "WireGuard" or "AmneziaWG")) throw new InvalidOperationException("Журнал восстановления повреждён. Нужна ручная проверка session.json.");
                var protocol = saved.Kind == "WireGuard" ? Protocol.WireGuard : saved.Kind == "AmneziaWG" ? Protocol.AmneziaWG : Protocol.VLESS;
                session = saved with { Engine = EnginePath(protocol), ConfigPath = Path.Combine(Storage.Runtime, saved.TunnelName + (saved.Kind == "proxy" ? ".json" : ".conf")) };
            }
            await StopInternal();
            if (IsAdministrator) KillSwitch.Remove(forceOwnedRecovery: true);
            foreach (var f in Directory.EnumerateFiles(Storage.Runtime)) File.Delete(f);
        }
        finally { gate.Release(); }
    }
    private async Task StopInternal()
    {
        bool resetDns = session?.FullDevice == true || KillSwitch.NeedsRecovery;
        if (session != null)
        {
            Notify("Отключение…");
            if (session.Kind == "proxy")
            {
                Process? owned = process;
                if (owned == null && session.Pid != 0)
                {
                    try
                    {
                        var found = Process.GetProcessById(session.Pid);
                        if (found.StartTime.ToUniversalTime().Ticks == session.Started && string.Equals(found.MainModule?.FileName, session.Engine, StringComparison.OrdinalIgnoreCase)) owned = found;
                        else found.Dispose();
                    }
                    catch (ArgumentException) { }
                }
                if (owned != null) { using (owned) { if (!owned.HasExited) { owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } } }
                process = null;
                await StopOwned(xray, session.XrayPid, session.XrayStarted, XrayPath); xray = null;
                job?.Dispose(); job = null;
            }
            else
            {
                if (!IsAdministrator) throw new InvalidOperationException("Для удаления оставшегося WG/AWG-туннеля перезапусти приложение с правами администратора.");
                var svc = ServiceName(session);
                if (ServiceExists(svc))
                {
                    int code;
                    if (session.Kind == "WireGuard")
                    {
                        string sc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
                        code = await RunAsync(sc, ["stop", svc]);
                        if (code is not (0 or 1062)) throw new InvalidOperationException("Не удалось остановить прежнюю службу WireGuard.");
                        for (int i = 0; i < 50 && ServiceRunning(svc); i++) await Task.Delay(200);
                        if (ServiceRunning(svc)) throw new InvalidOperationException("Прежняя служба ещё останавливается. Повтори восстановление.");
                        code = await RunAsync(sc, ["delete", svc]);
                    }
                    else
                    {
                        if (!File.Exists(session.Engine)) throw new InvalidOperationException("Для восстановления нужен соответствующий движок.");
                        code = await RunAsync(session.Engine, ["/uninstalltunnelservice", session.TunnelName]);
                    }
                    if (code != 0) throw new InvalidOperationException("Не удалось удалить службу туннеля. Журнал сохранён для повторного восстановления.");
                    for (int i = 0; i < 30 && ServiceExists(svc); i++) await Task.Delay(200);
                    if (ServiceExists(svc)) throw new InvalidOperationException("Служба ещё удаляется. Повтори восстановление.");
                }
            }
            if (File.Exists(session.ConfigPath)) File.Delete(session.ConfigPath);
            File.Delete(session.ConfigPath + ".xray.json");
            KillSwitch.Remove();
            session = null;
        }
        KillSwitch.Remove();
        if (resetDns) SystemNetworkProbe.FlushDnsCache();
        File.Delete(Journal); LocalPort = 0; Notify("Отключено");
    }
    public void Poll()
    {
        if (session?.Kind == "proxy" && (process is { HasExited: true } || xray is { HasExited: true })) Notify(TrafficLocked ? "Соединение оборвано · прямой трафик заблокирован" : "Движок остановился · нажми «Отключить» для очистки");
        else if (session is { Kind: not "proxy" } s && !ServiceRunning(ServiceName(s))) Notify(TrafficLocked ? "Туннель остановился · прямой трафик заблокирован" : "Служба туннеля остановилась · требуется отключение");
    }
    public async Task<string> ProbeAsync()
    {
        if (session == null) throw new InvalidOperationException("Сначала запусти подключение.");
        if (WholeDevice) return await SystemNetworkProbe.RunAsync(session.FullDevice, session.Kind != "proxy");
        if (session.Kind != "proxy")
        {
            if (session.Kind == "WireGuard") throw new InvalidOperationException("Прежний WireGuard-туннель требует восстановления. Отключи его и подключись заново через общую службу.");
            string tool = Path.Combine(AppContext.BaseDirectory, "engines", "awg.exe");
            using var command = Process.Start(Info(tool, ["show", session.TunnelName, "latest-handshakes"]))!;
            var output = command.StandardOutput.ReadToEndAsync(); var error = command.StandardError.ReadToEndAsync();
            try { await command.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { if (!command.HasExited) command.Kill(true); throw new TimeoutException("Не удалось получить handshake туннеля."); }
            string text = await output; await error;
            if (command.ExitCode != 0) throw new InvalidOperationException("Не удалось прочитать handshake. Проверь права администратора и состояние службы.");
            long timestamp = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length >= 2).Select(parts => long.TryParse(parts[1], out var value) ? value : 0).DefaultIfEmpty().Max();
            if (timestamp == 0) return "Handshake ещё не получен. Проверь сервер, ключи и Endpoint. При отсутствии трафика обмен может не начаться; при необходимости задай PersistentKeepalive в профиле.";
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(timestamp);
            return $"Последний handshake: {Math.Max(0, (int)age.TotalSeconds)} сек. назад. Маршруты и DNS проверяются отдельно.";
        }
        using var handler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{LocalPort}"), AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await http.GetAsync("https://example.com/");
        if (!response.IsSuccessStatusCode || !(await response.Content.ReadAsStringAsync()).Contains("Example Domain", StringComparison.Ordinal)) throw new InvalidOperationException("Проверочный HTTPS-запрос не прошёл через прокси или сервер вернул неожиданный ответ.");
        return "HTTPS-запрос через локальный прокси выполнен. Это подтверждает работу выбранного сервера; маршруты TUN и DNS требуют отдельной проверки.";
    }
    private static async Task WaitReady(Process child, int port)
    {
        for (int i = 0; i < 40; i++)
        {
            if (child.HasExited) throw new InvalidOperationException("Движок VLESS завершился при запуске.");
            try { using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(150)); return; } catch (SocketException) { } catch (TimeoutException) { }
            await Task.Delay(100);
        }
        throw new TimeoutException("Локальный порт Xray не запустился.");
    }
    private static async Task StopOwned(Process? owned, int pid, long started, string engine)
    {
        if (owned == null && pid != 0)
        {
            try { var found = Process.GetProcessById(pid); if (found.StartTime.ToUniversalTime().Ticks == started && string.Equals(found.MainModule?.FileName, engine, StringComparison.OrdinalIgnoreCase)) owned = found; else found.Dispose(); }
            catch (ArgumentException) { }
        }
        if (owned != null) using (owned) { if (!owned.HasExited) { owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); } }
    }
    internal static async Task PrepareTunEndpoint(System.Text.Json.Nodes.JsonNode config, Func<string, Task<IPAddress[]>>? resolver = null)
    {
        // Resolve bootstrap endpoints before TUN starts. Keep original TLS/HTTP names.
        var outbound = config["outbounds"]![0]!;
        var stream = outbound["streamSettings"]!;
        var peer = outbound["protocol"]!.GetValue<string>() switch { "vless" => outbound["settings"]!["vnext"]![0]!, "shadowsocks" => outbound["settings"]!["servers"]![0]!, _ => outbound["settings"]! };
        string host = peer["address"]!.GetValue<string>();
        if (!IPAddress.TryParse(host, out _))
        {
            var addresses = await (resolver?.Invoke(host) ?? Dns.GetHostAddressesAsync(host)).WaitAsync(TimeSpan.FromSeconds(10));
            var chosen = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault() ?? throw new InvalidOperationException("Не удалось определить IP сервера VLESS.");
            foreach (var key in new[] { "tlsSettings", "realitySettings" }) if (stream[key] is System.Text.Json.Nodes.JsonObject settings && string.IsNullOrEmpty(settings["serverName"]?.GetValue<string>())) settings["serverName"] = host;
            foreach (var key in new[] { "xhttpSettings", "httpupgradeSettings" }) if (stream[key] is System.Text.Json.Nodes.JsonObject settings && string.IsNullOrEmpty(settings["host"]?.GetValue<string>())) settings["host"] = key == "xhttpSettings" ? stream["tlsSettings"]?["serverName"]?.GetValue<string>() ?? stream["realitySettings"]?["serverName"]?.GetValue<string>() ?? host : host;
            if (stream["wsSettings"] is System.Text.Json.Nodes.JsonObject ws) { ws["headers"] ??= new System.Text.Json.Nodes.JsonObject(); if (string.IsNullOrEmpty(ws["headers"]!["Host"]?.GetValue<string>())) ws["headers"]!["Host"] = host; }
            peer["address"] = chosen.ToString();
        }
        if (stream["xhttpSettings"]?["extra"]?["downloadSettings"] is System.Text.Json.Nodes.JsonObject download && download["address"] is { } address)
        {
            string hostname = address.GetValue<string>();
            if (!IPAddress.TryParse(hostname, out _))
            {
                var ips = await (resolver?.Invoke(hostname) ?? Dns.GetHostAddressesAsync(hostname)).WaitAsync(TimeSpan.FromSeconds(10));
                download["address"] = (ips.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? ips.First()).ToString();
                if (download["tlsSettings"] is System.Text.Json.Nodes.JsonObject tls && string.IsNullOrEmpty(tls["serverName"]?.GetValue<string>())) tls["serverName"] = hostname;
                if (download["xhttpSettings"] is System.Text.Json.Nodes.JsonObject xh && string.IsNullOrEmpty(xh["host"]?.GetValue<string>())) xh["host"] = download["tlsSettings"]?["serverName"]?.GetValue<string>() ?? download["realitySettings"]?["serverName"]?.GetValue<string>() ?? hostname;
            }
        }
    }
    internal static void BindTunUplink(System.Text.Json.Nodes.JsonNode config, Func<IPAddress, string>? routeInterface = null)
    {
        var outbound = config["outbounds"]![0]!;
        var stream = outbound["streamSettings"]!;
        var peer = outbound["protocol"]!.GetValue<string>() switch { "vless" => outbound["settings"]!["vnext"]![0]!, "shadowsocks" => outbound["settings"]!["servers"]![0]!, _ => outbound["settings"]! };
        void Bind(System.Text.Json.Nodes.JsonNode settings, string address)
        {
            string adapter = (routeInterface ?? SystemNetworkProbe.RouteInterface)(IPAddress.Parse(address));
            if (adapter is "No route" or "Unknown interface" || adapter.StartsWith("OneTunnel", StringComparison.Ordinal)) throw new InvalidOperationException("Не найден внешний сетевой интерфейс для сервера. Сначала отключи другое подключение OneTunnel.");
            settings["sockopt"] ??= new System.Text.Json.Nodes.JsonObject();
            settings["sockopt"]!["interface"] = adapter;
        }
        Bind(stream, peer["address"]!.GetValue<string>());
        if (stream["xhttpSettings"]?["extra"]?["downloadSettings"] is System.Text.Json.Nodes.JsonObject download && download["address"] is { } address)
            Bind(download, address.GetValue<string>());
    }
    public static ProcessStartInfo Info(string file, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task<int> RunAsync(string file, IEnumerable<string> args)
    {
        using var p = new Process { StartInfo = Info(file, args) }; p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25)); } catch { if (!p.HasExited) p.Kill(true); throw new TimeoutException("Сетевой компонент не ответил за 25 секунд."); }
        await Task.WhenAll(stdout, stderr); return p.ExitCode;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Controls, Win32Exit, SpecificExit, Checkpoint, Hint; }
    private static uint? ReadService(string name)
    {
        var manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, name, 4);
            if (service == IntPtr.Zero) { if (Marshal.GetLastWin32Error() == 1060) return null; throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            try { if (!QueryServiceStatus(service, out var status)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); return status.State; }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    private static bool ServiceExists(string name) => ReadService(name) != null;
    private static bool ServiceRunning(string name) => ReadService(name) == 4;
}
