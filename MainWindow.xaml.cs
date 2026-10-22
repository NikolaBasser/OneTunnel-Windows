using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OneTunnel.Core;

namespace OneTunnel;
public partial class MainWindow : Window
{
    private readonly ObservableCollection<Profile> profiles = [];
    private readonly OneTunnel.Client.ServiceConnection connection = new();
    private bool refreshing;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? preview;
    private ProfileView? profileView;
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
        try { foreach (var p in Storage.Load()) profiles.Add(p); storageReady = true; }
        catch { Feedback.Text = "Не удалось прочитать хранилище профилей. Файл сохранён; новые записи заблокированы. Проверь права доступа и учётную запись Windows."; }
        RefreshGroups();
        connection.Changed += () => { if (!lifetime.IsCancellationRequested) Dispatcher.BeginInvoke(new Action(() => { StatusLabel.Text = connection.Status; PresentConnectionState(); RefreshControls(); })); };
        if (profiles.Count > 0) ProfilesList.SelectedIndex = 0;
        if (storageReady && DesktopEnvironment.NeedsRecovery) { ShowPanel("engines"); Feedback.Text = "Найдено подключение прежней версии. Перед установкой службы отключи его в прежней версии приложения либо используй восстановление от администратора."; }
        Log("Приложение готово. Подключения не запускаются автоматически."); RefreshControls();
        Loaded += async (_, _) =>
        {
            await RefreshService();
            _ = connection.ObserveAsync(lifetime.Token);
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
        try { await connection.RefreshAsync(); PresentConnectionState(); }
        finally { refreshing = false; RefreshControls(); }
    }
    private void PresentConnectionState()
    {
        var display=connection.Display;
        StatusLabel.Text=display.Status;
        if (connection.ProfileId is { } id && profiles.FirstOrDefault(p => p.Id == id) is { } active) ProfilesList.SelectedItem = active;
        if(display.ShowSession) {
            ModeBox.SelectedIndex=display.ModeIndex;
            ConnectionInfo.Text=display.ConnectionInfo;
            ConnectionHint.Text=display.ConnectionHint;
        }
        if(display.RecoveryHint!="") Feedback.Text=display.RecoveryHint;
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
    private async void RefreshControls()
    {
        try { await connection.SetContextAsync(new(busy,storageReady,selected?.Id.ToString()??"")); }
        catch { Feedback.Text="Нет ответа от пользовательского Go-ядра."; }
        var actions=connection.Actions;
        ConnectButton.Content=actions.ConnectCaption;
        ConnectButton.IsEnabled=actions.CanToggle;
        ProbeButton.IsEnabled=actions.CanProbe;
        EditButton.IsEnabled=actions.CanEdit; DeleteButton.IsEnabled=actions.CanDelete;
        AddProfileButton.IsEnabled=actions.CanAdd;
        ProfilesList.IsEnabled=actions.CanSelect;
        ModeBox.IsEnabled=actions.CanChangeMode;
        RecoveryButton.IsEnabled=LegacyRecoveryButton.IsEnabled=actions.CanRecover;
        InstallServiceButton.IsEnabled=actions.CanInstallService;
        StartServiceButton.IsEnabled=actions.CanStartService;
        EditorPanel.IsEnabled=EditorActions.IsEnabled=actions.CanSave;
        SaveProfileButton.IsEnabled=actions.CanSave;
    }
    private async void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || ProfileTitle == null) return;
        var p = selected;
        ProfileTitle.Value = p?.Name ?? "Добавь первое подключение";
        ProfileDescription.Text = p == null ? "Импорт конфигурации или подписки" : $"{p.Protocol} · профиль сохранён локально";
        ModePanel.Visibility = p != null ? Visibility.Visible : Visibility.Collapsed;
        profileView = null;
        if (p == null) { ConnectionInfo.Text = "AmneziaWG · WireGuard · VLESS · SOCKS5 · Hysteria2 · Shadowsocks"; ConnectionHint.Text = "Серверы не входят в приложение."; }
        else
        {
            try
            {
                var view = await ProfileBackend.ViewAsync(p.Protocol);
                if (selected?.Id != p.Id) return;
                profileView = view;
                ((ComboBoxItem)ModeBox.Items[0]).Content = view.ModeLabel;
                ConnectionInfo.Text = view.ConnectionInfo; ConnectionHint.Text = view.ConnectionHint;
            }
            catch { Feedback.Text = "Нет ответа от Go-ядра."; }
        }
        UpdateModeHelp(); RefreshControls();
        PresentConnectionState();
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e) => UpdateModeHelp();
    private void UpdateModeHelp()
    {
        if (ModeHelp == null) return;
        ModeHelp.Text = profileView == null ? "" : ModeBox.SelectedIndex == 0 ? profileView.ConfigHelp : profileView.FullHelp;
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanAdd) return;
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
        var o = options ?? ProfileBackend.DefaultTransport();
        FragmentCheck.IsChecked = o.Fragment; NoiseCheck.IsChecked = o.Noise; MuxCheck.IsChecked = o.Mux;
        FragmentLengthBox.Text = o.FragmentLength; FragmentIntervalBox.Text = o.FragmentInterval;
        NoiseLengthBox.Text = o.NoiseLength; NoiseDelayBox.Text = o.NoiseDelay;
        MuxConcurrencyBox.Text = o.MuxConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TransportExpander.IsExpanded = o.Fragment || o.Noise || o.Mux;
    }
    private TransportInput ReadTransport() => new(FragmentCheck.IsChecked == true, FragmentLengthBox.Text, FragmentIntervalBox.Text, NoiseCheck.IsChecked == true, NoiseLengthBox.Text, NoiseDelayBox.Text, MuxCheck.IsChecked == true, MuxConcurrencyBox.Text);

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanEdit || selected is not { } p) return;
        await Execute(async () => {
            var edit = await ProfileBackend.LoadEditorAsync(p.Id);
            LoadTransport(edit.Transport);
            GroupBox.Text = edit.Group; editing = edit.Id; NameBox.Text = edit.Name; SourceBox.Text = edit.Source; ProtocolBox.SelectedItem = edit.Protocol; EditorTitle.Text = "Изменить подключение"; ShowPanel("editor");
        });
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanSave) return;
        await Execute(async () =>
        {
            var result=await ProfileBackend.SaveEditorAsync(NameBox.Text,SourceBox.Text,GroupBox.Text,editing,ReadTransport());
            PresentApplied(result);
        });
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
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Конфигурация / подписка (*.conf;*.json;*.txt;*.vpn)|*.conf;*.json;*.txt;*.vpn", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        await Execute(async () =>
        {
            var result = await ProfileBackend.ImportFileAsync(dialog.FileName);
            SourceBox.Text = result.Source;
            if (result.Protocol is { } protocol) ProtocolBox.SelectedItem = (Protocol)protocol;
            if (NameBox.Text == "") NameBox.Text = result.Name;
        });
    }
    private async void Source_Changed(object sender, TextChangedEventArgs e)
    {
        preview?.Cancel(); preview?.Dispose(); preview = new CancellationTokenSource();
        var cancellation = preview.Token;
        if (DetectedProtocol == null || SourceBox == null) return;
        string text = SourceBox.Text;
        if (text.Length == 0) { DetectedProtocol.Text = "Протокол определится по конфигурации"; return; }
        try
        {
            await Task.Delay(250, cancellation);
            var result = await ProfileBackend.PreviewAsync(text, cancellation);
            if (cancellation.IsCancellationRequested) return;
            if (result.Protocol is { } protocol) ProtocolBox.SelectedItem = (Protocol)protocol;
            DetectedProtocol.Text = result.Description;
        }
        catch (OperationCanceledException) { }
        catch { if (!cancellation.IsCancellationRequested) DetectedProtocol.Text = "Проверка при импорте"; }
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try { var text = Clipboard.GetText(); if (text.Length > DesktopEnvironment.State.MaxImportBytes) throw new FormatException(); SourceBox.Text = text; }
        catch { Feedback.Text = "Не удалось вставить текст. Проверь размер конфигурации."; }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanDelete || selected is not { } p) return;
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
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var id=selected?.Id;
        bool fullDevice=ModeBox.SelectedIndex==1;
        await Execute(async () => {
            Feedback.Text="Выполнение команды…";
            try { Feedback.Text=await connection.ToggleAsync(id,fullDevice); }
            finally { if(connection.Prepared is { } prepared) PresentDocument(prepared.Document,prepared.Selected?.Id); }
            PresentConnectionState(); Log("Команда подключения завершена Go-ядром.");
        });
    }
    private async void Probe_Click(object sender, RoutedEventArgs e) => await Execute(async () => { Feedback.Text = "Проверка…"; Feedback.Text = await connection.ProbeAsync(); Log("Проверка завершена."); });
    private async void Recovery_Click(object sender, RoutedEventArgs e) => await Execute(async () => { await connection.RecoverAsync(); Feedback.Text = "Подключение очищено, блокировка kill switch снята. Обычная сеть восстановлена."; Log("Восстановление завершено."); });
    private void Cancel_Click(object sender, RoutedEventArgs e) { ShowPanel("connection"); }
    private void Engines_Click(object sender, RoutedEventArgs e) { if (!busy) ShowPanel("engines"); }
    private async void StartService_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        await connection.StartServiceAsync(); Feedback.Text = "Служба запущена. Автоматическое подключение не выполняется.";
    });
    private async void InstallService_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        await connection.InstallServiceAsync(); Feedback.Text = "Служба установлена Go-ядром."; ShowPanel("engines");
    });
    private async void Elevate_Click(object sender, RoutedEventArgs e) => await Execute(async () =>
    {
        await connection.RecoverAsync(); Feedback.Text = "Восстановление завершено.";
    });
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (!connection.Actions.CanClose) { e.Cancel = true; Feedback.Text = "Дождись завершения текущей операции."; return; }
        lifetime.Cancel(); preview?.Cancel();
        // The service owns the connection. Closing the UI never tears it down.
    }
}

