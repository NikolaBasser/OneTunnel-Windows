using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using OneTunnel.Core;

namespace OneTunnel;
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Profile> profiles = [];
    private readonly IConnectionController connection = new OneTunnel.Client.ServiceConnection();
    private bool refreshing;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Profile? selected => ProfilesList.SelectedItem as Profile;
    private Guid? editing;
    private bool busy, allowClose, storageReady;
    public MainWindow()
    {
        InitializeComponent();
        ModeHelp.Text = "После подключения укажи локальный адрес прокси в браузере или другом приложении.";
        ProtocolBox.ItemsSource = Enum.GetValues<Protocol>(); ProtocolBox.SelectedIndex = 0;
        ProfilesList.ItemsSource = profiles;
        System.Windows.Data.CollectionViewSource.GetDefaultView(profiles).GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Profile.Group)));
        try { Storage.Initialize(); foreach (var p in Storage.Load()) profiles.Add(p); storageReady = true; }
        catch { Feedback.Text = "Не удалось прочитать хранилище профилей. Файл сохранён; новые записи заблокированы. Проверь права доступа и учётную запись Windows."; }
        RefreshGroups();
        connection.Changed += () => Dispatcher.Invoke(() => { StatusLabel.Text = connection.Status; RefreshControls(); });
        timer.Tick += async (_, _) => await RefreshService();
        timer.Start();
        if (profiles.Count > 0) ProfilesList.SelectedIndex = 0;
        if (storageReady && DesktopEnvironment.NeedsRecovery) { ShowPanel("engines"); Feedback.Text = "Найдено подключение прежней версии. Перед установкой службы отключи его в прежней версии приложения либо используй восстановление от администратора."; }
        Log("Приложение готово. Подключения не запускаются автоматически."); RefreshControls();
        Loaded += async (_, _) =>
        {
            await RefreshService();
            var args = Environment.GetCommandLineArgs();
            int capture = Array.IndexOf(args, "--capture");
            if (capture >= 0 && args.Length > capture + 1)
            {
                await Task.Delay(300);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(this); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var stream = File.Create(args[capture + 1])) png.Save(stream);
                allowClose = true; Close();
            }
        };
    }
    private async Task RefreshService()
    {
        if (refreshing || busy) return;
        refreshing = true;
        try
        {
            await connection.RefreshAsync();
            if (connection is OneTunnel.Client.ServiceConnection service && service.ProfileId is { } id && profiles.FirstOrDefault(p => p.Id == id) is { } active) ProfilesList.SelectedItem = active;
            if (connection.Active && connection.Available)
            {
                ModeBox.SelectedIndex = connection.FullDeviceMode ? 1 : 0;
                ConnectionInfo.Text = connection.WholeDevice ? connection.FullDeviceMode ? "Всё устройство · системный туннель" : "Системный туннель · по конфигурации" : $"127.0.0.1:{connection.LocalPort}";
                ConnectionHint.Text = connection.TrafficLocked ? "Kill switch включён. Соединением управляет служба; закрытие окна его не прерывает. Для возврата обычной сети нажми «Отключить»." : "Соединением управляет служба. Закрытие окна его не прерывает; «Отключить» завершает подключение.";
            }
            if (connection.NeedsRecovery) Feedback.Text = "Служба обнаружила незавершённое подключение. Для снятия блокировки используй «Очистить оставшееся подключение».";
        }
        finally { refreshing = false; RefreshControls(); }
    }
    private void Log(string message)
    {
        var lines = (EventLog.Text + $"{DateTime.Now:HH:mm:ss}  {message}\n").Split('\n');
        EventLog.Text = string.Join('\n', lines.TakeLast(40)); EventLog.ScrollToEnd();
    }
    private void ShowPanel(string panel)
    {
        ConnectionPanel.Visibility = panel == "connection" ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = panel == "editor" ? Visibility.Visible : Visibility.Collapsed;
        EditorActions.Visibility = panel == "editor" ? Visibility.Visible : Visibility.Collapsed;
        EnginesPanel.Visibility = panel == "engines" ? Visibility.Visible : Visibility.Collapsed;
        if (panel != "editor") { SourceBox.Clear(); NameBox.Clear(); editing = null; }
        if (panel == "engines")
        {
            var state=DesktopEnvironment.State;
            EngineDetails.Text = $"AmneziaWG / WireGuard — {(state.Awg ? "компонент найден" : "не установлен")}\nSOCKS5 / Shadowsocks / TUN (sing-box) — {(state.SingBox ? "компонент найден" : "не установлен")}\nVLESS / Hysteria2 (Xray) — {(state.Xray ? "компонент найден" : "не установлен")}\nПрофили, импорт и подписки: Go-ядро\nУправляющая служба: {(connection.Available ? "доступна" : "нет связи — установи или запусти службу")}\nДля обычной работы права администратора интерфейсу не требуются.";
        }
    }
    private void RefreshControls()
    {
        ConnectButton.Content = connection.Active ? "Отключить" : "Подключить";
        ConnectButton.IsEnabled = !busy && storageReady && connection.Available && (connection.Active || selected != null && !connection.NeedsRecovery && !DesktopEnvironment.NeedsRecovery);
        ProbeButton.IsEnabled = !busy && connection.Available && connection.Active;
        EditButton.IsEnabled = DeleteButton.IsEnabled = !busy && !connection.Active && selected != null;
        ProfilesList.IsEnabled = !busy && !connection.Active;
        ModeBox.IsEnabled = !busy && !connection.Active;
        RecoveryButton.IsEnabled = !busy && (connection.Available || storageReady && DesktopEnvironment.IsAdministrator && DesktopEnvironment.NeedsRecovery);
        EditorPanel.IsEnabled = !busy;
        EditorActions.IsEnabled = !busy;
    }
    private void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || ProfileTitle == null) return;
        var p = selected;
        ProfileTitle.Value = p?.Name ?? "Добавь первое подключение";
        ProfileDescription.Text = p == null ? "Импортируй WG/AWG-конфигурацию, ссылку подключения или HTTPS-подписку. Профили хранятся на этом компьютере в зашифрованном виде." : $"{p.Protocol} · профиль сохранён на этом компьютере";
        ModePanel.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        ((ComboBoxItem)ModeBox.Items[0]).Content = p?.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "По конфигурации · маршруты из AllowedIPs" : "Локальный прокси · только выбранные приложения";
        ConnectionInfo.Text = p == null ? "AmneziaWG · WireGuard · VLESS · SOCKS5 · Hysteria2 · Shadowsocks" : p.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "Маршрутизация из конфигурации" : "Режим подключения";
        ConnectionHint.Text = p == null ? "Серверы не входят в приложение. Используй собственную конфигурацию или данные своего провайдера." : p.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "В режиме «По конфигурации» используются исходные AllowedIPs. В режиме «Всё устройство» полные маршруты создаются во временном конфиге; сохранённый профиль не меняется." : "Локальный прокси не меняет настройки Windows. В режиме всего устройства kill switch блокирует прямой выход при обрыве и после аварии службы. Для возврата обычной сети нажми «Отключить».";
        UpdateModeHelp();
        RefreshControls();
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e) => UpdateModeHelp();
    private void UpdateModeHelp()
    {
        if (ModeHelp == null) return;
        ModeHelp.Text = ModeBox.SelectedIndex == 0 ? selected?.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "Используются маршруты AllowedIPs из профиля. Частичные маршруты не защищают весь трафик устройства." : "После подключения укажи локальный адрес прокси в браузере или другом приложении." : selected?.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "Полные маршруты применяются временно. Kill switch сохраняет блокировку при обрыве и аварии приложения. «Отключить» возвращает обычную сеть." : "Трафик IPv4/IPv6 и DNS направляется через туннель. Kill switch включается автоматически и сохраняет блокировку при обрыве. «Отключить» возвращает обычную сеть.";
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (busy || connection.Active) { Feedback.Text = "Сначала отключи активное подключение."; return; }
        editing = null; LoadTransport(null); NameBox.Clear(); SourceBox.Clear(); GroupBox.Text = ""; ProtocolBox.SelectedIndex = 0; EditorTitle.Text = "Новое подключение"; ShowPanel("editor"); NameBox.Focus();
    }
    private void EmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        MenuItem Item(string symbol, string label)
        {
            var item = new MenuItem { Header = new OneTunnel.Controls.EmojiText { Value = symbol + "  " + label, FontSize = 15, Padding = new Thickness(4, 2, 4, 2) } };
            item.Click += (_, _) => { int start = NameBox.SelectionStart; NameBox.SelectedText = symbol; NameBox.CaretIndex = start + symbol.Length; NameBox.Focus(); };
            return item;
        }
        foreach (var (symbol, label) in new[] { ("🚀", "Быстрое подключение"), ("🔒", "Защита"), ("⚡", "Молния"), ("⭐", "Избранное"), ("❤️", "Сердце"), ("🌍", "Мир") }) menu.Items.Add(Item(symbol, label));
        var countries = new MenuItem { Header = "Флаги стран и регионов" };
        foreach (var (flag, name) in OneTunnel.Controls.EmojiText.Countries()) countries.Items.Add(Item(flag, name));
        menu.Items.Add(new Separator()); menu.Items.Add(countries);
        EmojiPickerButton.ContextMenu = menu; menu.PlacementTarget = EmojiPickerButton; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true;
    }
    private void LoadTransport(TransportOptions? options)
    {
        var o = options ?? new TransportOptions();
        FragmentCheck.IsChecked = o.Fragment; NoiseCheck.IsChecked = o.Noise; MuxCheck.IsChecked = o.Mux;
        FragmentLengthBox.Text = o.FragmentLength; FragmentIntervalBox.Text = o.FragmentInterval;
        NoiseLengthBox.Text = o.NoiseLength; NoiseDelayBox.Text = o.NoiseDelay;
        MuxConcurrencyBox.Text = o.MuxConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TransportExpander.IsExpanded = o.Fragment || o.Noise || o.Mux;
    }
    private TransportOptions? ReadTransport()
    {
        if (!int.TryParse(MuxConcurrencyBox.Text, out int concurrency)) throw new FormatException("Укажи число потоков MUX от 1 до 128.");
        var o = new TransportOptions { Fragment = FragmentCheck.IsChecked == true, Noise = NoiseCheck.IsChecked == true,
            Mux = MuxCheck.IsChecked == true, FragmentLength = FragmentLengthBox.Text.Trim(), FragmentInterval = FragmentIntervalBox.Text.Trim(),
            NoiseLength = NoiseLengthBox.Text.Trim(), NoiseDelay = NoiseDelayBox.Text.Trim(), MuxConcurrency = concurrency };
        // Preserve an explicit all-off preference and custom values across edits.
        return o;
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (selected is not { } p || busy || connection.Active) return;
        LoadTransport(p.Transport);
        GroupBox.Text = p.Group; editing = p.Id; NameBox.Text = p.Name; SourceBox.Text = p.Source; ProtocolBox.SelectedItem = p.Protocol; EditorTitle.Text = "Изменить подключение"; ShowPanel("editor");
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || connection.Active || !storageReady) { Feedback.Text = "Сохранение сейчас недоступно. Проверь состояние подключения и хранилища."; return; }
        await Execute(async () =>
        {
            var result=await ProfileBackend.SaveEditorAsync(NameBox.Text,SourceBox.Text,GroupBox.Text,editing,ReadTransport());
            PresentApplied(result);
        });
    }
    private void SaveBatch(ImportResult batch)
    {
        PresentApplied(ProfileBackend.Apply(batch,SourceBox.Text.Trim(),GroupName(true)));
    }
    private void PresentDocument(ProfileDocument document,Guid? selectedId=null)
    {
        Storage.Accept(document);RefreshGroups(document.Profiles);
        profiles.Clear();foreach(var profile in document.Profiles)profiles.Add(profile);
        ProfilesList.SelectedItem=profiles.FirstOrDefault(p=>p.Id==selectedId)??profiles.FirstOrDefault();
    }
    private void PresentApplied(AppliedProfiles result)
    {
        PresentDocument(result.Document,result.Selected?.Id??selected?.Id);ShowPanel("connection");
        Feedback.Text=result.Batch?$"Подписка обновлена / импортирована. Обновлено: {result.Updated}. Добавлено: {result.Added}. Устаревших удалено: {result.Removed}. Повторов: {result.Duplicates}. Неподдерживаемых: {result.Skipped}.":"Профиль сохранён. Ключи защищены учётной записью Windows.";
        Log(result.Batch?"Импорт завершён Go-ядром.":"Профиль сохранён.");
    }
    private string GroupName(bool subscription=false)=>ProfileBackend.Group(GroupBox.Text,NameBox.Text,SourceBox.Text,subscription);
    private void RefreshGroups(IEnumerable<Profile>? items = null) => GroupBox.ItemsSource = (items ?? profiles).Select(p => p.Group).Distinct().ToList();
    private void Group_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander exp && exp.DataContext is System.Windows.Data.CollectionViewGroup group) exp.IsExpanded = !Storage.CollapsedGroups.Contains(group.Name.ToString()!);
    }
    private void Group_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander exp || !exp.IsLoaded || !storageReady || exp.DataContext is not System.Windows.Data.CollectionViewGroup group) return;
        string name = group.Name.ToString()!;
        var before = Storage.CollapsedGroups.ToHashSet();
        if (exp.IsExpanded) Storage.CollapsedGroups.Remove(name); else Storage.CollapsedGroups.Add(name);
        try { Storage.Accept(ProfileBackend.SetGroupExpanded(name,exp.IsExpanded)); } catch { Storage.CollapsedGroups.Clear(); Storage.CollapsedGroups.UnionWith(before); Feedback.Text = "Не удалось сохранить состояние группы."; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Конфигурация / подписка (*.conf;*.json;*.txt;*.vpn)|*.conf;*.json;*.txt;*.vpn", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string contents = ProfileBackend.ReadFile(dialog.FileName);
            if (!Path.GetExtension(dialog.FileName).Equals(".conf", StringComparison.OrdinalIgnoreCase)) { SourceBox.Text = contents; ProtocolBox.SelectedItem = Protocol.VLESS; return; }
            var p = ProfileParser.Parse(Path.GetFileNameWithoutExtension(dialog.FileName), contents);
            SourceBox.Text = p.Source; ProtocolBox.SelectedItem = p.Protocol; if (NameBox.Text == "") NameBox.Text = p.Name;
        }
        catch (FormatException ex) { Feedback.Text = ex.Message; }
        catch { Feedback.Text = "Не удалось прочитать файл."; }
    }
    private void Source_Changed(object sender, TextChangedEventArgs e)
    {
        if (DetectedProtocol == null || SourceBox == null) return;
        string text = SourceBox.Text.Trim();
        if (text.Length == 0) { DetectedProtocol.Text = "Протокол определится по конфигурации"; return; }
        if (Subscriptions.IsUrl(text)) { DetectedProtocol.Text = "HTTPS-подписка · будет импортирована отдельной группой"; return; }
        try { var profile = ProfileParser.Parse("Предпросмотр", text); ProtocolBox.SelectedItem = profile.Protocol; DetectedProtocol.Text = "Распознано: " + profile.Protocol; }
        catch { DetectedProtocol.Text = "Подписка или конфигурация · проверка при импорте"; }
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText(); if (text.Length > Subscriptions.MaxBytes) throw new FormatException(); SourceBox.Text = text;
            if (Subscriptions.IsUrl(text) || text.TrimStart().StartsWith('[')) ProtocolBox.SelectedItem = Protocol.VLESS;
            else { try { ProtocolBox.SelectedItem = ProfileParser.Parse("", text).Protocol; } catch (FormatException) { } }
        }
        catch { Feedback.Text = "Не удалось вставить текст. Проверь буфер обмена и размер конфигурации."; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || connection.Active || !storageReady || selected is not { } p) return;
        if (MessageBox.Show(this, "Удалить выбранный профиль с этого компьютера?", "Удаление профиля", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { PresentDocument(ProfileBackend.Delete(p.Id)); Feedback.Text = "Профиль удалён."; }
        catch { Feedback.Text = "Не удалось удалить профиль из хранилища."; }
    }
    private async Task Execute(Func<Task> action)
    {
        if (busy) return; busy = true; RefreshControls();
        try { await action(); }
        catch (Exception ex) { Feedback.Text = ex is InvalidOperationException or TimeoutException or FormatException ? ex.Message : "Операция не выполнена. Проверь доступность сервера, настройки и права доступа."; Log("Операция не выполнена. Подробности в строке состояния."); }
        finally { busy = false; RefreshControls(); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        if (connection.Active) { await connection.StopAsync(); Feedback.Text = "Отключено. Временная конфигурация удалена."; Log("Подключение остановлено."); }
        else if (selected is { } p)
        {
            bool fullDevice=ModeBox.SelectedIndex==1;
            p=await RefreshSubscriptionForConnection(p);
            ModeBox.SelectedIndex=fullDevice?1:0;
            await connection.StartAsync(p, fullDevice);
            Feedback.Text = p.Protocol is Protocol.WireGuard or Protocol.AmneziaWG ? "Служба туннеля запущена. Нажми «Проверить доступ», чтобы проверить системный HTTPS и DNS." : "Движок запущен. Нажми «Проверить доступ», чтобы выполнить запрос через прокси.";
            if(connection.ClockSynchronized) Feedback.Text = "Время для Shadowsocks 2022 синхронизировано внутри приложения. Часы Windows не изменены. Нажми «Проверить доступ».";
            if (connection.WholeDevice) { Feedback.Text = "Системный туннель готов. Нажми «Проверить доступ», чтобы проверить HTTPS и DNS через сеть Windows."; ConnectionInfo.Text = connection.FullDeviceMode ? "Всё устройство · системный туннель" : "Системный туннель · по конфигурации"; ConnectionHint.Text = connection.TrafficLocked ? "Kill switch включён. При обрыве прямой выход останется заблокированным. Для возврата обычной сети нажми «Отключить»; после аварии службы — «Очистить оставшееся подключение»." : "Проверка доступа использует сеть Windows без локального прокси. Отсутствие утечек подтверждается отдельным сетевым тестом."; }
            else if (connection.LocalPort > 0) { ConnectionInfo.Text = $"127.0.0.1:{connection.LocalPort}"; ConnectionHint.Text = "Локальный HTTP / SOCKS5-прокси. Системные настройки прокси Windows не изменены."; }
            Log("Движок выбранного профиля запущен.");
        }
    });
    private async Task<Profile> RefreshSubscriptionForConnection(Profile profile)
    {
        Feedback.Text="Обновление параметров подписки перед подключением…";
        var result=await ProfileBackend.PrepareAsync(profile.Id);
        PresentDocument(result.Document,result.Selected?.Id);
        if(result.Offline)Log("Не удалось обновить подписку; используется сохранённый профиль.");
        if(result.Retired||result.Selected==null)throw new InvalidOperationException("Сервер удалён из подписки. Выбери актуальный узел из обновлённой группы.");
        return result.Selected;
    }
    private async void Probe_Click(object sender, RoutedEventArgs e) => await Execute(async () => { Feedback.Text = "Проверка…"; Feedback.Text = await connection.ProbeAsync(); Log("Проверка завершена."); });
    private async void Recovery_Click(object sender, RoutedEventArgs e) => await Execute(async () => { if (DesktopEnvironment.NeedsRecovery && DesktopEnvironment.IsAdministrator) await ProfileBackend.LegacyRecoverAsync(); else await connection.RecoverAsync(); Feedback.Text = "Подключение очищено, блокировка kill switch снята. Обычная сеть восстановлена."; Log("Восстановление завершено."); });
    private void Cancel_Click(object sender, RoutedEventArgs e) { ShowPanel("connection"); }
    private void Engines_Click(object sender, RoutedEventArgs e) { if (!busy) ShowPanel("engines"); }
    private async void StartService_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        await OneTunnel.Client.ServicePeer.StartAsync(); await connection.RefreshAsync();
        Feedback.Text = connection.NeedsRecovery ? "Служба запущена. Для возврата обычной сети нажми «Очистить оставшееся подключение»." : "Служба запущена. Автоматическое подключение не выполняется.";
    });
    private async void InstallService_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        if (connection.Active || DesktopEnvironment.NeedsRecovery || connection.NeedsRecovery) throw new InvalidOperationException("Сначала отключи подключение или выполни восстановление. Затем можно обновить службу.");
        string script = Path.Combine(AppContext.BaseDirectory, "scripts", "Install-Service.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException("Для установки службы нужна полная сборка OneTunnel.");
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        info.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -OwnerSid {sid}";
        using var install = Process.Start(info) ?? throw new InvalidOperationException("Установка не запущена.");
        await install.WaitForExitAsync();
        if (install.ExitCode != 0) throw new InvalidOperationException("Служба не установлена. Проверь разрешение UAC и отсутствие активного подключения.");
        await connection.RefreshAsync();
        Feedback.Text = "Служба установлена. Интерфейс можно запускать без прав администратора. Закрытие окна сохраняет подключение.";
        ShowPanel("engines");
    });
    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        if (connection.Active || busy) { Feedback.Text = "Сначала отключи активное подключение."; return; }
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = $"--wait-for {Environment.ProcessId}" });
            allowClose = true; timer.Stop(); Close();
        }
        catch { allowClose = false; Feedback.Text = "Не удалось перезапустить приложение. Запусти OneTunnel.exe от имени администратора."; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (busy) { e.Cancel = true; Feedback.Text = "Дождись завершения текущей операции."; return; }
        timer.Stop();
        // The service owns the connection. Closing the UI never tears it down.
    }
}
