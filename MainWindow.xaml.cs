using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OneTunnel.Core;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;

namespace OneTunnel;
public partial class MainWindow : Window
{
    private const int RestoreHotkeyId = 0x4F31;
    private const uint ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private readonly ObservableCollection<Profile> profiles = [];
    private readonly ObservableCollection<SubscriptionSummary> subscriptions = [];
    private readonly ObservableCollection<GroupSummary> groupSummaries = [];
    private readonly OneTunnel.Client.ServiceConnection connection = new();
    private bool refreshing;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? preview;
    private CancellationTokenSource? groupCheck;
    private ProfileView? profileView;
    private bool restoringGroupExpansion;
    private string? appliedSearch;
    private Guid? appliedSession;
    private string? sidebarGroup;
    private string visiblePanel = "connection";
    private string settingsSection = "connection";
    private sealed record SidebarGroup(string Name, int Count, bool Selected);
    private Profile? selected => ProfilesList.SelectedItem as Profile;
    private SubscriptionSummary? selectedSubscription => SubscriptionGroupsList.SelectedItem as SubscriptionSummary;
    private GroupSummary? selectedGroup => ConfigurationGroupsList.SelectedItem as GroupSummary;
    private SubscriptionPreview? pendingSubscriptionPreview;
    private CancellationTokenSource? subscriptionDownload;
    private int subscriptionLoadVersion;
    private SubscriptionScheduleView? subscriptionScheduleView;
    private bool subscriptionSchedulePending, subscriptionScheduleSavePending, scheduleTickPending;
    private int subscriptionScheduleVersion;
    private string? subscriptionScheduleDraftGroup;
    private System.Windows.Threading.DispatcherTimer? subscriptionScheduleTimer;
    private TrayProfilesView? trayProfilesView;
    private bool trayProfilesPending, traySelectionPending, applyingTrayProfileSelection;
    private int trayProfilesVersion, traySelectionVersion;
    private BackupRestorePreview? backupRestorePreview;
    private bool backupPending, backupApplyPending;
    private Guid? editing;
    private bool busy, allowClose, storageReady;
    private bool libraryPending, favoritePending, libraryProjectionLoaded, updatingLibraryControls, applyingLibrarySelection;
    private bool favoritesOnly, appliedFavoritesOnly;
    private string librarySort = "stored", appliedLibrarySort = "stored";
    private int libraryRequestVersion;
    private HashSet<Guid> libraryIDs = [];
    private Dictionary<Guid, int> libraryRanks = [];
    private Guid? pendingLibrarySelection;
    private System.Windows.Data.ListCollectionView? profileCollectionView;
    private bool diagnosticsPending;
    private bool groupsPending, groupMutationPending, applyingGroupSelection, applyingBulkGroupExpansion, groupsLoaded;
    private int groupRequestVersion;
    private GroupResultsView? groupResultsView;
    private bool groupResultsPending, fastestSelectionPending;
    private int groupResultsRequestVersion;
    private string groupResultsSort = "stored", groupResultsFilter = "all";
    private ProfileOrganizationView? profileOrganizationView;
    private bool profileOrganizationPending, profileOrganizationMutationPending;
    private bool applyingProfileDocument;
    private int profileOrganizationRequestVersion;
    private Guid? profileOrganizationRequestedId;
    private Guid? profileOrganizationFieldsId;
    private string? profileOrganizationFieldsToken;
    private NetworkPreferencesView? networkPreferencesView;
    private bool networkPreferencesPending, networkPreferencesSavePending;
    private int networkPreferencesRequestVersion;
    private Guid? networkPreferencesRequestedId, networkPreferencesFieldsId;
    private string? networkPreferencesFieldsToken;
    private UserSettingsView? userSettingsView;
    private bool userSettingsPending, userSettingsTickPending, userSettingsSavePending, userSettingsActionPending, updatingUserSettingsControls, userSettingsDraftDirty;
    private string? userSettingsDraftToken;
    private int userSettingsRequestVersion;
    private System.Windows.Threading.DispatcherTimer? userSettingsTimer;
    private TrayShell? tray;
    private WindowState previousWindowState = WindowState.Normal;
    private IntPtr windowHandle;
    private HwndSource? windowSource;
    private uint taskbarCreatedMessage;
    private bool hotkeyRegistered, hiddenToTray, syncingTrayNotifications;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterWindowMessage(string message);
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitializeNativeWindow();
        ModeHelp.Text = "После подключения укажи локальный адрес прокси в браузере или другом приложении.";
        ProtocolBox.ItemsSource = Enum.GetValues<Protocol>(); ProtocolBox.SelectedIndex = 0;
        ProfilesList.ItemsSource = profiles;
        SubscriptionGroupsList.ItemsSource = subscriptions;
        ConfigurationGroupsList.ItemsSource = groupSummaries;
        var profileItems = System.Windows.Data.CollectionViewSource.GetDefaultView(profiles);
        profileCollectionView = profileItems as System.Windows.Data.ListCollectionView;
        profileItems.Filter = MatchesProfile;
        InitializeControlV2();
        try { foreach (var p in Storage.Load()) profiles.Add(p); storageReady = true; }
        catch { Feedback.Text = "Не удалось прочитать хранилище профилей. Файл сохранён; новые записи заблокированы. Проверь права доступа и учётную запись Windows."; }
        RefreshGroups();
        RefreshProfileFilter(true);
        connection.Changed += () => { if (!lifetime.IsCancellationRequested) Dispatcher.BeginInvoke(new Action(() => { StatusLabel.Text = connection.Status; PresentConnectionState(); RefreshControls(); })); };
        if (profiles.Count > 0) ProfilesList.SelectedIndex = 0;
        if (storageReady && DesktopEnvironment.NeedsRecovery) { ShowPanel("engines"); Feedback.Text = "Найдено подключение прежней версии. Перед установкой службы отключи его в прежней версии приложения либо используй восстановление от администратора."; }
        Log("Приложение готово. Подключения не запускаются автоматически."); RefreshControls();
        Loaded += async (_, _) =>
        {
            _ = RefreshLibraryProjectionAsync();
            try {
                tray = new TrayShell(RestoreWindow, MinimizeWindow, () => Connect_Click(this, new RoutedEventArgs()), () => { RestoreWindow(); Engines_Click(this, new RoutedEventArgs()); }, Close);
                tray.NotificationsItem.CheckedChanged += TrayNotificationsItem_Changed;
                TrayNotificationsBox.IsEnabled = true;
                UpdateTray(); tray.Show();
                _ = RefreshTrayProfilesAsync();
                HideToTrayButton.IsEnabled = windowSource != null && windowHandle != IntPtr.Zero && taskbarCreatedMessage != 0;
            }
            catch { tray?.Dispose(); tray = null; TrayNotificationsBox.IsEnabled = HideToTrayButton.IsEnabled = false; Feedback.Text = "Значок трея недоступен. Окно остаётся в панели задач; уведомления недоступны."; }
            subscriptionScheduleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            subscriptionScheduleTimer.Tick += SubscriptionScheduleTimer_Tick;
            subscriptionScheduleTimer.Start();
            UserAutoConnectProfileBox.ItemsSource = profiles;
            userSettingsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            userSettingsTimer.Tick += UserSettingsTimer_Tick;
            userSettingsTimer.Start();
            _ = LoadUserSettingsAsync();
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
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) previousWindowState = WindowState; };
        Closed += (_, _) => { subscriptionScheduleTimer?.Stop(); userSettingsTimer?.Stop(); lifetime.Cancel(); preview?.Cancel(); groupCheck?.Cancel(); subscriptionDownload?.Cancel(); ReleaseRestoreHotkey(); try { windowSource?.RemoveHook(WindowNativeHook); } catch { } windowSource = null; tray?.Dispose(); tray = null; };
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
        RefreshProfileFilter();
        if (selected == null && connection.ProfileId is { } id && profiles.FirstOrDefault(p => p.Id == id) is { } active) ProfilesList.SelectedItem = active;
        if(display.ShowSession) {
            ModeBox.SelectedIndex=display.ModeIndex;
            ConnectionInfo.Text=display.ConnectionInfo;
            ConnectionHint.Text=display.ConnectionHint;
        }
        StatisticsPanel.Visibility = display.ShowStatistics ? Visibility.Visible : Visibility.Collapsed;
        StatisticsUploaded.Text = display.Uploaded;
        StatisticsDownloaded.Text = display.Downloaded;
        StatisticsUploadRate.Text = display.UploadRate;
        StatisticsDownloadRate.Text = display.DownloadRate;
        StatisticsElapsed.Text = display.Elapsed;
        StatisticsScopeHint.Text = display.StatisticsScopeHint;
        if(display.ErrorDiagnostic!="") Feedback.Text=display.ErrorDiagnostic;
        if(display.RecoveryHint!="") Feedback.Text=display.RecoveryHint;
        UpdateTray();
        UpdateControlInspector();
    }
    private void Log(string message)
    {
        var lines = (EventLog.Text + $"{DateTime.Now:HH:mm:ss}  {message}\n").Split('\n');
        EventLog.Text = string.Join('\n', lines.TakeLast(40)); EventLog.ScrollToEnd();
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void UpdateControlNavigation() => PresentControlNavigation();
    private void SidebarGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanSelect || sender is not Button { Tag: string group }) return;
        sidebarGroup = group;
        SearchBox.Clear(); FavoritesOnlyBox.IsChecked = false;
        ShowPanel("connection"); RefreshProfileFilter(true);
    }
    private void SidebarFavorites_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanSelect) return;
        sidebarGroup = null; SearchBox.Clear(); FavoritesOnlyBox.IsChecked = true;
        ShowPanel("connection"); RefreshProfileFilter(true);
    }
    private void SettingsSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        if (visiblePanel != "settings") ShowPanel("settings");
        settingsSection = name switch { "UserSystemProxyBox" => "proxy", "UserStartupPath" => "startup", _ => "connection" };
        PresentSettingsSection(); WorkspaceScroll.ScrollToTop();
    }
    private void PresentSettingsSection() => PresentControlSettings();
    private void NetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel("network");
    }
    private void ShowPanel(string panel)
    {
        visiblePanel = panel;
        UpdateControlNavigation();
        if (panel == "settings") PresentSettingsSection();
        ConnectionPanel.Visibility = panel == "connection" ? Visibility.Visible : Visibility.Collapsed;
        EditorPanel.Visibility = panel == "editor" ? Visibility.Visible : Visibility.Collapsed;
        EditorActions.Visibility = panel == "editor" ? Visibility.Visible : Visibility.Collapsed;
        EnginesPanel.Visibility = panel == "engines" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = panel == "activity" ? Visibility.Visible : Visibility.Collapsed;
        GroupsPanel.Visibility = panel == "groups" ? Visibility.Visible : Visibility.Collapsed;
        SubscriptionsPanel.Visibility = panel == "subscriptions" ? Visibility.Visible : Visibility.Collapsed;
        UserSettingsPanel.Visibility = panel == "settings" ? Visibility.Visible : Visibility.Collapsed;
        ImportConnectionButton.Visibility = panel == "connection" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = panel switch { "editor" => "Импорт / подписка", "subscriptions" => "Подписки", "groups" => "Группы", "engines" => "Движки и восстановление", "activity" => "События", "settings" => "Настройки", _ => sidebarGroup ?? (FavoritesOnlyBox.IsChecked == true ? "Избранное" : "Все серверы") };
        PageDescription.Text = panel switch { "editor" => "Импортируй профиль или обнови его из источника", "subscriptions" => "Сохранённые группы и ручное обновление профилей", "groups" => "Управление группами профилей и подписок", "engines" => "Состояние компонентов и восстановление соединения", "activity" => "Журнал событий приложения", "settings" => "Подключение, системный прокси и запуск приложения", _ => "Профили, сохранённые на этом компьютере" };
        if (panel == "subscriptions") _ = LoadSubscriptionsAsync();
        if (panel == "settings") _ = LoadUserSettingsAsync();
        if (panel == "engines")
        {
            var state=DesktopEnvironment.State;
            EngineDetails.Text = $"AmneziaWG / WireGuard — {(state.Awg ? "компонент найден" : "не установлен")}\nSOCKS5 / Shadowsocks / HTTP(S) / Trojan / TUIC / AnyTLS / NaiveProxy / TUN (sing-box) — {(state.SingBox ? "компонент найден" : "не установлен")}\nVLESS / Hysteria2 (Xray) — {(state.Xray ? "компонент найден" : "не установлен")}\nПрофили, импорт и подписки: Go-ядро\nУправляющая служба: {(connection.Available ? "доступна" : "нет связи — установи или запусти службу")}\nДля обычной работы права администратора интерфейсу не требуются.";
        }
        ApplyControlLayout(panel);
    }
    private async void RefreshControls()
    {
        try { await connection.SetContextAsync(new(busy,storageReady,selected?.Id.ToString()??"",groupCheck!=null)); }
        catch { Feedback.Text="Нет ответа от пользовательского Go-ядра."; }
        var actions=connection.Actions;
        ConnectButton.Content=actions.ConnectCaption;
        ConnectButton.IsEnabled=actions.CanToggle;
        ProbeButton.IsEnabled=actions.CanProbe;
 CheckGroupButton.IsEnabled=actions.CanCheckGroup;
 CancelGroupButton.IsEnabled=actions.CanCancelGroup;CancelGroupButton.Visibility=actions.CanCancelGroup?Visibility.Visible:Visibility.Collapsed;
        EditButton.IsEnabled=actions.CanEdit; DeleteButton.IsEnabled=actions.CanDelete;
        AddProfileButton.IsEnabled=actions.CanAdd;
        ImportConnectionButton.IsEnabled=ImportSubscriptionButton.IsEnabled=actions.CanAdd;
        SubscriptionButton.IsEnabled=true;ReloadSubscriptionsButton.IsEnabled=!busy;
        UserSettingsNavigationButton.IsEnabled=true;
        HomeNavigationButton.IsEnabled=GroupsNavigationButton.IsEnabled=EventsNavigationButton.IsEnabled=true;
        CancelSubscriptionDownloadButton.Visibility=subscriptionDownload != null ? Visibility.Visible : Visibility.Collapsed;
        CancelSubscriptionDownloadButton.IsEnabled=subscriptionDownload != null && !subscriptionDownload.IsCancellationRequested;
        SubscriptionGroupsList.IsEnabled=pendingSubscriptionPreview == null && !busy;
        RefreshSubscriptionButton.IsEnabled=actions.CanAdd && selectedSubscription?.CanRefresh == true && pendingSubscriptionPreview == null && !busy;
        SubscriptionRefreshUnavailable.Visibility=selectedSubscription != null && (!selectedSubscription.CanRefresh || !actions.CanAdd) ? Visibility.Visible : Visibility.Collapsed;
        ApplySubscriptionButton.IsEnabled=actions.CanSave && pendingSubscriptionPreview?.CanApply == true && !busy;
        CancelSubscriptionPreviewButton.IsEnabled=pendingSubscriptionPreview != null && !busy;
        SubscriptionApplyUnavailable.Visibility=pendingSubscriptionPreview != null && (!pendingSubscriptionPreview.CanApply || !actions.CanSave) ? Visibility.Visible : Visibility.Collapsed;
        ProfilesList.IsEnabled=actions.CanSelect && !fastestSelectionPending;
        SidebarGroups.IsEnabled=SidebarFavoritesButton.IsEnabled=ProfilesList.IsEnabled;
        ModeBox.IsEnabled=actions.CanChangeMode;
        RecoveryButton.IsEnabled=LegacyRecoveryButton.IsEnabled=actions.CanRecover;
        InstallServiceButton.IsEnabled=actions.CanInstallService;
        StartServiceButton.IsEnabled=actions.CanStartService;
        EditorPanel.IsEnabled=EditorActions.IsEnabled=actions.CanSave;
        SaveProfileButton.IsEnabled=actions.CanSave;
        UpdateTray();
        UpdateDiagnosticsControls();
        UpdateLibraryControls();
        UpdateGroupControls();
        UpdateGroupResultControls();
        UpdateProfileOrganizationControls();
        UpdateNetworkPreferencesControls();
        UpdateUserSettingsControls();
        UpdateSubscriptionScheduleControls();
        UpdateBackupControls();
        UpdateControlInspector();
    }
    private async Task RefreshTrayProfilesAsync()
    {
        if (tray == null || trayProfilesPending || lifetime.IsCancellationRequested) return;
        int request = ++trayProfilesVersion;
        trayProfilesPending = true;
        trayProfilesView = null;
        tray.ClearProfiles();
        try
        {
            var view = await ProfileBackend.TrayProfilesAsync();
            if (request != trayProfilesVersion || lifetime.IsCancellationRequested || tray == null) return;
            trayProfilesView = view;
            tray.PresentProfiles(view, (id, token) => _ = SelectTrayProfileAsync(id, token));
        }
        catch
        {
            if (request != trayProfilesVersion || tray == null) return;
            trayProfilesView = null;
            tray.ClearProfiles();
        }
        finally { if (request == trayProfilesVersion) trayProfilesPending = false; }
    }
    private async Task SelectTrayProfileAsync(string id, string token)
    {
        if (traySelectionPending || !connection.Actions.CanSelect || trayProfilesView is not { CanSelect: true } currentTrayView || currentTrayView.Token != token || !Guid.TryParse(id, out var requestedId)) return;
        int selectionRequest = traySelectionVersion;
        traySelectionPending = true;
        tray?.ClearProfiles();
        RestoreWindow();
        try
        {
            string selectedId = await ProfileBackend.SelectTrayProfileAsync(id, token);
            if (lifetime.IsCancellationRequested || selectionRequest != traySelectionVersion || !connection.Actions.CanSelect) return;
            if (!Guid.TryParse(selectedId, out var actualId) || actualId != requestedId || profiles.FirstOrDefault(profile => profile.Id == actualId) is not { } profile) throw new InvalidDataException();
            SearchBox.Clear();
            sidebarGroup = null;
            updatingLibraryControls = true;
            try { favoritesOnly = false; FavoritesOnlyBox.IsChecked = false; }
            finally { updatingLibraryControls = false; }
            pendingLibrarySelection = actualId;
            RefreshProfileFilter(true);
            await RefreshLibraryProjectionAsync();
            if (lifetime.IsCancellationRequested || selectionRequest != traySelectionVersion || !connection.Actions.CanSelect) return;
            if (profiles.FirstOrDefault(candidate => candidate.Id == actualId) is not { } exactProfile) throw new InvalidDataException();
            applyingTrayProfileSelection = true;
            try { ProfilesList.SelectedItem = exactProfile; ProfilesList.ScrollIntoView(exactProfile); }
            finally { applyingTrayProfileSelection = false; }
            if (!HasEditorContent()) ShowPanel("connection");
            Feedback.Text = "Профиль выбран из трея. Подключение не запускалось.";
        }
        catch { Feedback.Text = "Не удалось выбрать профиль из трея. Открой окно и обнови список профилей."; }
        finally
        {
            traySelectionPending = false;
            if (!lifetime.IsCancellationRequested) _ = RefreshTrayProfilesAsync();
        }
    }
    private void UpdateTray()
    {
        if (tray?.Present(connection.Display, connection.Actions) is { } notification) tray.Notify(notification);
    }
    private void UpdateDiagnosticsControls()
    {
        if (GenerateDiagnosticsButton == null) return;
        GenerateDiagnosticsButton.IsEnabled = !diagnosticsPending;
        bool available = !diagnosticsPending && DiagnosticsReport.Text.Length > 0;
        CopyDiagnosticsButton.IsEnabled = SaveDiagnosticsButton.IsEnabled = available;
    }
    private async void GenerateDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticsPending) return;
        diagnosticsPending = true;
        DiagnosticsReport.Clear();
        UpdateDiagnosticsControls();
        try
        {
            var report = await ProfileBackend.DiagnosticsAsync();
            if (report.Length == 0) throw new InvalidDataException();
            DiagnosticsReport.Text = report;
            Feedback.Text = "Диагностический отчёт сформирован. Проверь его перед копированием или сохранением.";
        }
        catch
        {
            DiagnosticsReport.Clear();
            Feedback.Text = "Не удалось сформировать диагностический отчёт.";
        }
        finally
        {
            diagnosticsPending = false;
            UpdateDiagnosticsControls();
        }
    }
    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticsPending || DiagnosticsReport.Text.Length == 0) return;
        try { Clipboard.SetText(DiagnosticsReport.Text); Feedback.Text = "Диагностический отчёт скопирован."; }
        catch { Feedback.Text = "Не удалось скопировать диагностический отчёт."; }
    }
    private async void SaveDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (diagnosticsPending || DiagnosticsReport.Text.Length == 0) return;
        try
        {
            var dialog = new SaveFileDialog { Filter = "Файл JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = "onetunnel-diagnostics.json", OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            await SaveDiagnosticsReportAsync(dialog.FileName);
            Feedback.Text = "Диагностический отчёт сохранён.";
        }
        catch { Feedback.Text = "Не удалось сохранить диагностический отчёт."; }
    }
    private Task SaveDiagnosticsReportAsync(string path) => File.WriteAllTextAsync(path, DiagnosticsReport.Text, new System.Text.UTF8Encoding(false));
    private void MinimizeWindow()
    {
        if (WindowState != WindowState.Minimized) previousWindowState = WindowState;
        WindowState = WindowState.Minimized;
    }
    private void RestoreWindow()
    {
        hiddenToTray = false;
        ReleaseRestoreHotkey();
        WindowState = previousWindowState == WindowState.Minimized ? WindowState.Normal : previousWindowState;
        ShowInTaskbar = true; Show(); Activate();
    }
    private void InitializeNativeWindow()
    {
        try
        {
            windowHandle = new WindowInteropHelper(this).Handle;
            int dark = 1;
            _ = DwmSetWindowAttribute(windowHandle, 20, ref dark, sizeof(int));
            windowSource = HwndSource.FromHwnd(windowHandle);
            if (windowSource == null || windowHandle == IntPtr.Zero) return;
            windowSource.AddHook(WindowNativeHook);
            taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        }
        catch { try { windowSource?.RemoveHook(WindowNativeHook); } catch { } windowSource = null; windowHandle = IntPtr.Zero; }
    }
    private bool TryHideToTray()
    {
        if (hiddenToTray) return true;
        if (tray == null || windowHandle == IntPtr.Zero || windowSource == null || taskbarCreatedMessage == 0)
        {
            Feedback.Text = "Не удалось подготовить восстановление окна. Оно остаётся доступным в панели задач.";
            return false;
        }
        if (!RegisterHotKey(windowHandle, RestoreHotkeyId, ModControl | ModShift | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(Key.O)))
        {
            Feedback.Text = "Не удалось скрыть окно: сочетание Ctrl+Shift+O уже занято или недоступно. Окно остаётся в панели задач.";
            return false;
        }
        hotkeyRegistered = true;
        if (WindowState != WindowState.Minimized) previousWindowState = WindowState;
        hiddenToTray = true;
        try { ShowInTaskbar = false; Hide(); return true; }
        catch
        {
            RestoreWindow();
            Feedback.Text = "Не удалось скрыть окно. Оно восстановлено и остаётся в панели задач.";
            return false;
        }
    }
    private void HideToTray_Click(object sender, RoutedEventArgs e) => TryHideToTray();
    private void TrayNotifications_Changed(object sender, RoutedEventArgs e)
    {
        if (syncingTrayNotifications || tray == null) return;
        syncingTrayNotifications = true;
        try { tray.NotificationsItem.Checked = TrayNotificationsBox.IsChecked == true; }
        finally { syncingTrayNotifications = false; }
    }
    private void TrayNotificationsItem_Changed(object? sender, EventArgs e)
    {
        if (syncingTrayNotifications) return;
        syncingTrayNotifications = true;
        try { TrayNotificationsBox.IsChecked = tray?.NotificationsItem.Checked == true; }
        finally { syncingTrayNotifications = false; }
    }
    private IntPtr WindowNativeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == (IntPtr)RestoreHotkeyId && hiddenToTray)
        {
            RestoreWindow(); handled = true;
        }
        else if (taskbarCreatedMessage != 0 && (uint)msg == taskbarCreatedMessage && hiddenToTray)
        {
            RestoreWindow(); handled = true;
        }
        return IntPtr.Zero;
    }
    private void ReleaseRestoreHotkey()
    {
        if (!hotkeyRegistered) return;
        hotkeyRegistered = false;
        try { if (windowHandle != IntPtr.Zero) UnregisterHotKey(windowHandle, RestoreHotkeyId); } catch { }
    }
    private async void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!applyingLibrarySelection && e.AddedItems.Count > 0) pendingLibrarySelection = null;
        if (!applyingLibrarySelection && !applyingTrayProfileSelection && !applyingProfileDocument && e.AddedItems.Count > 0) traySelectionVersion++;
        if (!IsInitialized || ProfileTitle == null) return;
        var p = selected;
        if (!applyingProfileDocument && p == null)
        {
            InvalidateProfileOrganization("Выбери профиль, чтобы управлять копией или группой.");
            InvalidateNetworkPreferences("Выбери профиль, чтобы настроить DNS и маршрутизацию.");
        }
        else if (!applyingProfileDocument && p != null)
        {
            if (profileOrganizationView?.ID != p.Id.ToString() || profileOrganizationRequestedId != p.Id)
            {
                InvalidateProfileOrganization("Сведения профиля обновляются…");
                _ = LoadProfileOrganizationAsync(p.Id);
            }
            if (networkPreferencesView?.ID != p.Id.ToString() || networkPreferencesRequestedId != p.Id)
            {
                InvalidateNetworkPreferences("Загрузка настроек сети…");
                _ = LoadNetworkPreferencesAsync(p.Id);
            }
        }        UpdateLibraryControls();
        ProfileTitle.Value = p?.Name ?? (profiles.Count == 0 ? "Добавь первое подключение" : "Выбери профиль из списка");
        ProfileDescription.Text = p == null ? "Импорт конфигурации или подписки" : $"{p.Protocol} · профиль сохранён локально";
        ModePanel.Visibility = p != null && (p.Protocol == Protocol.AmneziaWG || p.Protocol == Protocol.WireGuard) ? Visibility.Visible : Visibility.Collapsed;
        profileView = null;
        if (p == null) { ConnectionInfo.Text = "AmneziaWG · WireGuard · VLESS · SOCKS5 · Hysteria2 · Shadowsocks · HTTP(S) · Trojan · TUIC · AnyTLS · NaiveProxy"; ConnectionHint.Text = "Серверы не входят в приложение."; }
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
        if (tray != null) _ = RefreshTrayProfilesAsync();
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
        if (HasEditorContent()) { ShowPanel("editor"); Feedback.Text = "Продолжи или отмени текущий профиль перед созданием нового."; return; }
        BeginNewProfile(false);
    }
    private void Subscription_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanAdd) return;
        if (HasEditorContent()) { ShowPanel("editor"); Feedback.Text = "Продолжи или отмени текущий профиль перед импортом другого."; return; }
        BeginNewProfile(true);
    }
    private void Subscriptions_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel("subscriptions");
    }
    private async Task LoadSubscriptionsAsync()
    {
        if (!IsInitialized || busy) return;
        int version = ++subscriptionLoadVersion;
        string? selectedGroup = selectedSubscription?.Group;
        try
        {
            var result = await ProfileBackend.SubscriptionsAsync();
            if (version != subscriptionLoadVersion || !IsInitialized) return;
            subscriptions.Clear(); foreach (var item in result) subscriptions.Add(item);
            SubscriptionEmptyHint.Text = "Сохранённых подписок пока нет. Импортируй подписку, чтобы добавить группу.";
            SubscriptionEmptyHint.Visibility = subscriptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SubscriptionDetails.Visibility = Visibility.Collapsed;
            SubscriptionPreviewPanel.Visibility = pendingSubscriptionPreview == null ? Visibility.Collapsed : Visibility.Visible;
            var restore = subscriptions.FirstOrDefault(item => item.Group == selectedGroup) ?? subscriptions.FirstOrDefault();
            SubscriptionGroupsList.SelectedItem = restore;
            if (restore == null) { SubscriptionDetails.Visibility = Visibility.Collapsed; RefreshControls(); }
        }
        catch
        {
            if (version != subscriptionLoadVersion) return;
            SubscriptionEmptyHint.Text = "Не удалось загрузить список подписок. Повтори попытку.";
            SubscriptionEmptyHint.Visibility = Visibility.Visible;
            SubscriptionDetails.Visibility = Visibility.Collapsed;
            Feedback.Text = "Не удалось загрузить список подписок.";
        }
    }
    private void ReloadSubscriptions_Click(object sender, RoutedEventArgs e) => _ = LoadSubscriptionsAsync();
    private void SubscriptionGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || SubscriptionGroupTitle == null) return;
        if (selectedSubscription is not { } item)
        {
            subscriptionScheduleVersion++; subscriptionScheduleView = null;
            ScheduleEnabledBox.IsChecked = false; ScheduleIntervalBox.Clear(); ScheduleStatus.Text = "Выбери группу подписки, чтобы настроить расписание.";
            SubscriptionDetails.Visibility = Visibility.Collapsed; UpdateSubscriptionScheduleControls(); RefreshControls(); return;
        }
        SubscriptionDetails.Visibility = Visibility.Visible;
        if (subscriptionScheduleView?.Group != item.Group) _ = LoadSubscriptionScheduleAsync(item.Group, resetDraft: subscriptionScheduleDraftGroup != item.Group);
        SubscriptionGroupTitle.Text = item.Group;
        SubscriptionProfileCount.Text = $"{item.ProfileCount} проф.";
        SubscriptionUsage.Text = string.IsNullOrWhiteSpace(item.Usage) ? "Не сообщается" : item.Usage;
        SubscriptionExpiry.Text = string.IsNullOrWhiteSpace(item.Expiry) ? "Не сообщается" : item.Expiry;
        SubscriptionUpdatedAt.Text = string.IsNullOrWhiteSpace(item.UpdatedAt) ? "Успешное обновление ещё не выполнялось." : $"Последнее успешное обновление: {item.UpdatedAt}";
        RefreshControls();
    }
    private async void RefreshSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (busy || subscriptionDownload != null || !connection.Actions.CanAdd || selectedSubscription is not { CanRefresh: true } item || pendingSubscriptionPreview != null) return;
        await RefreshSubscriptionAsync(item);
    }
    private async Task RefreshSubscriptionAsync(SubscriptionSummary item)
    {
        if(busy || subscriptionDownload!=null || !connection.Actions.CanAdd || !item.CanRefresh || pendingSubscriptionPreview!=null)return;
        using var cancellation = new CancellationTokenSource();
        subscriptionDownload = cancellation;
        try { await Execute(async () =>
        {
            try {
                var previewResult = await ProfileBackend.PreviewSubscriptionAsync(item.Group, cancellation.Token);
                pendingSubscriptionPreview = previewResult;
                PresentSubscriptionPreview(previewResult);
                Feedback.Text = previewResult.CanApply ? "Предпросмотр готов. Изменения не сохранены." : "Предпросмотр готов, но Go-ядро не разрешает его применить.";
            }
            catch (OperationCanceledException) { Feedback.Text = "Загрузка отменена. Изменения не применялись; для обновления получи новый предпросмотр."; }
        }); }
        finally { subscriptionDownload = null; RefreshControls(); }
    }
    private void CancelSubscriptionDownload_Click(object sender, RoutedEventArgs e) { subscriptionDownload?.Cancel(); RefreshControls(); }
    private void PresentSubscriptionPreview(SubscriptionPreview result)
    {
        SubscriptionPreviewInfo.Text = $"{result.Group}: добавлено {result.Added}, обновлено {result.Updated}, удалено {result.Removed}, повторов {result.Duplicates}, неподдерживаемых {result.Skipped}.";
        SubscriptionPreviewMetadata.Text = $"Использование по данным провайдера: {(string.IsNullOrWhiteSpace(result.Usage) ? "не сообщается" : result.Usage)} · срок: {(string.IsNullOrWhiteSpace(result.Expiry) ? "не сообщается" : result.Expiry)} · предпросмотр доступен до {result.ExpiresAt}.";
        SubscriptionChangesList.ItemsSource = result.Changes;
        SubscriptionPreviewPanel.Visibility = Visibility.Visible;
        RefreshControls();
    }
    private async void ApplySubscription_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanSave || pendingSubscriptionPreview is not { CanApply: true } pending) return;
        await Execute(async () =>
        {
            try
            {
                var applied = await ProfileBackend.ApplySubscriptionAsync(pending.Token);
                pendingSubscriptionPreview = null;
                SubscriptionPreviewPanel.Visibility = Visibility.Collapsed;
                PresentSubscriptionApplied(applied);
            }
            catch
            {
                pendingSubscriptionPreview = null;
                SubscriptionPreviewPanel.Visibility = Visibility.Collapsed;
                try { await ProfileBackend.CancelSubscriptionPreviewAsync(pending.Token); } catch { }
                try {
                    var document = await ProfileBackend.LoadDocumentAsync();
                    PresentDocument(document, selected?.Id);
                    Feedback.Text = "Ответ об обновлении не получен или предпросмотр устарел. Профили перечитаны: проверь результат перед повторной попыткой.";
                }
                catch { Feedback.Text = "Результат обновления не подтверждён. Не удалось перечитать профили; проверь хранилище перед повторной попыткой."; }
            }
        });
        if (pendingSubscriptionPreview == null) await LoadSubscriptionsAsync();
    }
    private async void CancelSubscriptionPreview_Click(object sender, RoutedEventArgs e)
    {
        if (pendingSubscriptionPreview is not { } pending) return;
        await Execute(async () =>
        {
            try {
                await ProfileBackend.CancelSubscriptionPreviewAsync(pending.Token);
                Feedback.Text = "Предпросмотр отменён. Изменения не применялись.";
            }
            catch { Feedback.Text = "Предпросмотр закрыт. Для обновления получи новый предпросмотр."; }
            finally { pendingSubscriptionPreview = null; SubscriptionPreviewPanel.Visibility = Visibility.Collapsed; }
        });
    }
    private void PresentSubscriptionApplied(AppliedProfiles result)
    {
        SearchBox.Clear();
        PresentDocument(result.Document, result.Selected?.Id ?? selected?.Id);
        ShowPanel("subscriptions");
        Feedback.Text = $"Подписка обновлена. Обновлено: {result.Updated}. Добавлено: {result.Added}. Устаревших удалено: {result.Removed}.";
        Log("Ручное обновление подписки применено Go-ядром.");
    }
    private void UpdateSubscriptionScheduleControls()
    {
        if (!IsInitialized || ScheduleEnabledBox == null) return;
        var view = subscriptionScheduleView;
        bool selectedMatches = view != null && selectedSubscription?.Group == view.Group;
        bool pending = subscriptionSchedulePending || subscriptionScheduleSavePending || scheduleTickPending || busy;
        ScheduleEnabledBox.IsEnabled = ScheduleIntervalBox.IsEnabled = selectedMatches && view!.CanEdit && !pending;
        SaveSubscriptionScheduleButton.IsEnabled = selectedMatches && view!.CanEdit && connection.Actions.CanSave && !pending && pendingSubscriptionPreview == null;
    }
    private void PresentSubscriptionSchedule(SubscriptionScheduleView view, bool resetDraft = false)
    {
        subscriptionScheduleView = view;
        if (resetDraft || subscriptionScheduleDraftGroup != view.Group)
        {
            ScheduleEnabledBox.IsChecked = view.Enabled;
            ScheduleIntervalBox.Text = view.IntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            subscriptionScheduleDraftGroup = view.Group;
        }
        ScheduleStatus.Text = string.Join(" ", new[] { view.Message, string.IsNullOrWhiteSpace(view.NextCheckAt) ? "" : $"Следующая проверка: {view.NextCheckAt}." }.Where(value => !string.IsNullOrWhiteSpace(value)));
        UpdateSubscriptionScheduleControls();
    }
    private async Task LoadSubscriptionScheduleAsync(string group, bool resetDraft = false)
    {
        if (busy || !subscriptions.Any(item => item.Group == group)) return;
        int request = ++subscriptionScheduleVersion;
        subscriptionSchedulePending = true;
        subscriptionScheduleView = null;
        ScheduleStatus.Text = "Загрузка расписания…";
        UpdateSubscriptionScheduleControls();
        try
        {
            var view = await ProfileBackend.SubscriptionScheduleAsync(group);
            if (request != subscriptionScheduleVersion || lifetime.IsCancellationRequested || selectedSubscription?.Group != group) return;
            if (view.Group != group) throw new InvalidDataException();
            PresentSubscriptionSchedule(view, resetDraft);
        }
        catch { if (request == subscriptionScheduleVersion) ScheduleStatus.Text = "Не удалось загрузить расписание. Повтори попытку."; }
        finally
        {
            if (request == subscriptionScheduleVersion) { subscriptionSchedulePending = false; if (!lifetime.IsCancellationRequested) RefreshControls(); }
        }
    }
    private async void SaveSubscriptionSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (selectedSubscription is not { } item || subscriptionScheduleView is not { } view || view.Group != item.Group || !connection.Actions.CanSave || subscriptionScheduleSavePending || !int.TryParse(ScheduleIntervalBox.Text, out int minutes)) return;
        subscriptionScheduleSavePending = true; UpdateSubscriptionScheduleControls();
        try
        {
            var document = await ProfileBackend.SaveSubscriptionScheduleAsync(item.Group, view.Token, new(ScheduleEnabledBox.IsChecked == true, minutes));
            if (lifetime.IsCancellationRequested || selectedSubscription?.Group != item.Group) return;
            PresentDocument(document, selected?.Id);
            await LoadSubscriptionScheduleAsync(item.Group, resetDraft: true);
            Feedback.Text = "Расписание сохранено. Оно создаёт только предпросмотр обновления.";
        }
        catch { ScheduleStatus.Text = "Не удалось сохранить расписание. Проверь настройки и повтори попытку."; }
        finally { subscriptionScheduleSavePending = false; if (!lifetime.IsCancellationRequested) RefreshControls(); }
    }
    private async void SubscriptionScheduleTimer_Tick(object? sender, EventArgs e)
    {
        if (scheduleTickPending || busy || pendingSubscriptionPreview != null || lifetime.IsCancellationRequested) return;
        scheduleTickPending = true; UpdateSubscriptionScheduleControls();
        try
        {
            var result = await ProfileBackend.TickSubscriptionScheduleAsync(lifetime.Token);
            if (lifetime.IsCancellationRequested || result.Preview == null) return;
            pendingSubscriptionPreview = result.Preview; PresentSubscriptionPreview(result.Preview);
            if (!HasEditorContent()) ShowPanel("subscriptions");
            Feedback.Text = "Готов предпросмотр планового обновления. Профили не применены.";
        }
        catch (OperationCanceledException) { }
        catch { if (!lifetime.IsCancellationRequested) Feedback.Text = "Не удалось проверить расписание обновления."; }
        finally { scheduleTickPending = false; if (!lifetime.IsCancellationRequested) RefreshControls(); }
    }
    private bool HasEditorContent() => editing != null || NameBox.Text.Length > 0 || SourceBox.Text.Length > 0 || GroupBox.Text.Length > 0 || AutoReconnectCheck.IsChecked == true || FallbackCheck.IsChecked == true || QualityCheck.IsChecked == true;
    private void ClearEditorContent()
    {
        editing = null; NameBox.Clear(); SourceBox.Clear(); GroupBox.Text = ""; AutoReconnectCheck.IsChecked = false; FallbackCheck.IsChecked = false; QualityCheck.IsChecked = false;
    }
    private void BeginNewProfile(bool subscription)
    {
        AutoReconnectCheck.IsChecked=false;FallbackCheck.IsChecked=false;QualityCheck.IsChecked=false;
        editing = null; NameBox.Clear(); SourceBox.Clear(); GroupBox.Text = ""; ProtocolBox.SelectedIndex = 0;
        EditorTitle.Text = subscription ? "Импорт / обновление подписки" : "Новое подключение";
        SaveProfileButton.Content = subscription ? "Импортировать / обновить" : "Сохранить / импортировать";
        ShowPanel("editor"); NameBox.Focus();
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
    private static string ChoiceTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private bool UsesMuxConnectionLimit() => int.TryParse(MuxMaxConnectionsBox.Text, out var count) && count > 0;
    private void MuxProtocol_Changed(object sender, SelectionChangedEventArgs e) { if (MuxUDP443Box != null) UpdateMuxControls(); }
    private void MuxConnections_Changed(object sender, TextChangedEventArgs e) { if (MuxUDP443Box != null) UpdateMuxControls(); }
    private void UpdateMuxControls()
    {

        MuxMaxConnectionsBox.IsEnabled = MuxPaddingBox.IsEnabled = true;
        MuxMinStreamsBox.IsEnabled = UsesMuxConnectionLimit();
        MuxConcurrencyBox.IsEnabled = true;
        XUDPConcurrencyBox.IsEnabled = MuxUDP443Box.IsEnabled = true;
    }
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanEdit || selected is not { } p) return;
        if (HasEditorContent()) { ShowPanel("editor"); Feedback.Text = "Продолжи или отмени текущий профиль перед редактированием другого."; return; }
        await Execute(async () => {
            var edit = await ProfileBackend.LoadEditorAsync(p.Id);
 AutoReconnectCheck.IsChecked=edit.Reliability?.AutoReconnect??false;FallbackCheck.IsChecked=edit.Reliability?.UseFallbacks??false;QualityCheck.IsChecked=edit.Reliability?.AutoSelect??false;
            GroupBox.Text = edit.Group; editing = edit.Id; NameBox.Text = edit.Name; SourceBox.Text = edit.Source; ProtocolBox.SelectedItem = edit.Protocol; EditorTitle.Text = "Изменить подключение"; ShowPanel("editor");
        });
    }
    private async void CheckGroup_Click(object sender,RoutedEventArgs e)
    {
        if (groupCheck != null || groupResultsPending || !connection.Actions.CanCheckGroup || selected is not { } profile) return;
        using var cancellation = new CancellationTokenSource();
        groupCheck = cancellation;
        groupResultsRequestVersion++;
        groupResultsView = null;
        GroupResults.ItemsSource = null;
        GroupResultsInfo.Text = "Проверка группы выполняется…";
        GroupResultsPanel.Visibility = Visibility.Visible;
        GroupResultsPanel.IsExpanded = true;
        groupResultsPending = true;
        busy = true;
        RefreshControls();
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await ProfileBackend.CheckGroupAsync(profile.Group, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Feedback.Text = "Проверка группы завершена.";
            await RefreshGroupResultsAsync();
        }
        catch (OperationCanceledException)
        {
            await ClearGroupResultsQuietlyAsync();
            ClearGroupResultsDisplay("Проверка группы отменена. Текущая проверка сервера завершается в службе.");
            Feedback.Text = "Проверка группы отменена. Текущая проверка сервера завершается в службе.";
        }
        catch
        {
            await ClearGroupResultsQuietlyAsync();
            ClearGroupResultsDisplay("Не удалось получить результаты проверки.");
            Feedback.Text = "Не удалось подтвердить результаты проверки группы.";
        }
        finally
        {
            groupCheck = null;
            groupResultsPending = false;
            busy = false;
            if (!lifetime.IsCancellationRequested) RefreshControls();
        }
    }
    private void CancelGroup_Click(object sender,RoutedEventArgs e){if(connection.Actions.CanCancelGroup)groupCheck?.Cancel();}
    private void GroupResultOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || groupResultsView == null || fastestSelectionPending || busy) return;
        groupResultsSort = (GroupResultSortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stored";
        groupResultsFilter = (GroupResultFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        _ = RefreshGroupResultsAsync();
    }
    private async Task RefreshGroupResultsAsync()
    {
        int request = ++groupResultsRequestVersion;
        groupResultsPending = true;
        UpdateGroupResultControls();
        try
        {
            var view = await ProfileBackend.GroupResultsAsync(groupResultsSort, groupResultsFilter);
            if (request != groupResultsRequestVersion || lifetime.IsCancellationRequested) return;
            PresentGroupResults(view);
        }
        catch
        {
            if (request != groupResultsRequestVersion || lifetime.IsCancellationRequested) return;
            ClearGroupResultsDisplay("Не удалось обновить результаты проверки.");
            Feedback.Text = "Не удалось подтвердить результаты проверки группы.";
        }
        finally
        {
            if (request == groupResultsRequestVersion)
            {
                groupResultsPending = false;
                if (!lifetime.IsCancellationRequested) UpdateGroupResultControls();
            }
        }
    }
    private void PresentGroupResults(GroupResultsView view)
    {
        groupResultsView = view;
        GroupResults.Tag = view.Stale;
        GroupResults.ItemsSource = view.Rows;
        GroupResultsInfo.Text = view.HasResults
            ? $"{view.Group} · {view.CheckedAt}\n{view.Message}{(view.Stale ? "\nХранилище изменилось. Запусти новую проверку группы." : "")}" : view.Message;
        GroupResultsPanel.Visibility = Visibility.Visible;
        GroupResultsPanel.IsExpanded = true;
        UpdateGroupResultControls();
    }
    private void ClearGroupResultsDisplay(string message)
    {
        groupResultsView = null;
        GroupResults.ItemsSource = null;
        GroupResultsInfo.Text = message;
        GroupResultsPanel.Visibility = Visibility.Visible;
        GroupResultsPanel.IsExpanded = true;
        UpdateGroupResultControls();
    }
    private async Task ClearGroupResultsQuietlyAsync()
    {
        try { await ProfileBackend.ClearGroupResultsAsync(); } catch { }
    }
    private async void ClearGroupResults_Click(object sender, RoutedEventArgs e)
    {
        if (groupResultsPending || fastestSelectionPending || busy) return;
        groupResultsRequestVersion++;
        groupResultsPending = true;
        UpdateGroupResultControls();
        try
        {
            await ProfileBackend.ClearGroupResultsAsync(clearQuality: true);
            if (lifetime.IsCancellationRequested) return;
            ClearGroupResultsDisplay("Результаты очищены.");
            Feedback.Text = "Результаты проверки очищены.";
        }
        catch
        {
            ClearGroupResultsDisplay("Не удалось подтвердить очистку результатов.");
            Feedback.Text = "Не удалось подтвердить очистку результатов проверки.";
        }
        finally
        {
            groupResultsPending = false;
            if (!lifetime.IsCancellationRequested) UpdateGroupResultControls();
        }
    }
    private async void SelectFastestResult_Click(object sender, RoutedEventArgs e)
    {
        if (groupResultsPending || fastestSelectionPending || busy || groupResultsView is not { HasResults: true, CanSelectFastest: true } view || !connection.Actions.CanSelect) return;
        fastestSelectionPending = true;
        UpdateGroupResultControls();
        UpdateLibraryControls();
        RefreshControls();
        try
        {
            Guid? previousSelection = selected?.Id;
            Guid id = await ProfileBackend.FastestGroupResultAsync(view.Token);
            if (selected?.Id != previousSelection || !connection.Actions.CanSelect) return;
            if (lifetime.IsCancellationRequested) return;
            await PresentFastestSelectionAsync(id);
        }
        catch
        {
            groupResultsRequestVersion++;
            await ClearGroupResultsQuietlyAsync();
            ClearGroupResultsDisplay("Результаты устарели или больше недоступны. Запусти новую проверку группы.");
            Feedback.Text = "Не удалось выбрать результат. Запусти новую проверку группы.";
        }
        finally
        {
            fastestSelectionPending = false;
            if (!lifetime.IsCancellationRequested) { RefreshControls(); UpdateLibraryControls(); UpdateGroupResultControls(); }
        }
    }
    private async Task PresentFastestSelectionAsync(Guid id)
    {
        if (!connection.Actions.CanSelect) return;
        var profile = profiles.FirstOrDefault(item => item.Id == id) ?? throw new InvalidOperationException("Selected profile is no longer available.");
        bool refreshLibrary = favoritesOnly || FavoritesOnlyBox.IsChecked == true;
        sidebarGroup = null;
        pendingLibrarySelection = id;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Clear();
        if (refreshLibrary)
        {
            updatingLibraryControls = true;
            try { favoritesOnly = false; FavoritesOnlyBox.IsChecked = false; }
            finally { updatingLibraryControls = false; }
            await RefreshLibraryProjectionAsync();
            if (lifetime.IsCancellationRequested) return;
            if (appliedFavoritesOnly || !libraryProjectionLoaded || !libraryIDs.Contains(id))
                throw new InvalidOperationException("The selected profile is not visible in the current library.");
            if (selected?.Id != id || !connection.Actions.CanSelect) return;
        }
        RefreshProfileFilter(true);
        if (!MatchesProfile(profile)) throw new InvalidOperationException("The selected profile is not visible in the current library.");
        applyingLibrarySelection = true;
        try { ProfilesList.SelectedItem = profile; }
        finally { applyingLibrarySelection = false; pendingLibrarySelection = null; }
        Feedback.Text = "Лучший результат выбран. Подключение не запускалось.";
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!connection.Actions.CanSave) return;
        await Execute(async () =>
        {
            var result=await ProfileBackend.SaveEditorAsync(NameBox.Text,SourceBox.Text,GroupBox.Text,editing,null,new(AutoReconnectCheck.IsChecked==true,FallbackCheck.IsChecked==true,AutoReconnectCheck.IsChecked==true && FallbackCheck.IsChecked==true && QualityCheck.IsChecked==true));
            PresentApplied(result);
        });
    }
    private void UpdateBackupControls()
    {
        if (!IsInitialized || ExportBackupButton == null) return;
        bool pending = backupPending || backupApplyPending || busy;
        ExportBackupButton.IsEnabled = storageReady && !pending;
        PreviewBackupButton.IsEnabled = !pending && backupRestorePreview == null;
        CancelBackupPreviewButton.IsEnabled = !pending && backupRestorePreview != null;
        AddRestoreSchedulesBox.IsEnabled = !pending && backupRestorePreview?.EnabledSchedules > 0;
        ApplyBackupRestoreButton.IsEnabled = !pending && backupRestorePreview?.CanApply == true && connection.Actions.CanSave;
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Зашифрованная копия OneTunnel (*.otbackup)|*.otbackup", DefaultExt = ".otbackup", AddExtension = true, OverwritePrompt = false, FileName = "OneTunnel.otbackup" };
        if (dialog.ShowDialog(this) != true) return;
        await ExportBackupToPathAsync(dialog.FileName, BackupPasswordBox.Password);
    }

    private async Task ExportBackupToPathAsync(string path, string password)
    {
        if (backupPending || busy || !storageReady) return;
        backupPending = true; BackupStatus.Text = "Создание зашифрованной копии…"; UpdateBackupControls();
        try
        {
            var result = await ProfileBackend.ExportBackupAsync(path, password);
            BackupStatus.Text = $"Копия создана. Профилей: {result.ProfileCount}.";
        }
        catch { BackupStatus.Text = "Не удалось создать копию. Проверь пароль и выбери новый путь для файла."; }
        finally { BackupPasswordBox.Clear(); backupPending = false; UpdateBackupControls(); }
    }

    private async void PreviewBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Зашифрованная копия OneTunnel (*.otbackup)|*.otbackup|Все файлы (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await PreviewBackupFromPathAsync(dialog.FileName, RestorePasswordBox.Password);
    }

    private async Task PreviewBackupFromPathAsync(string path, string password)
    {
        if (backupPending || backupApplyPending || busy) return;
        backupPending = true; backupRestorePreview = null; AddRestoreSchedulesBox.IsChecked = false;
        BackupStatus.Text = "Проверка копии…"; UpdateBackupControls();
        try
        {
            var view = await ProfileBackend.PreviewBackupAsync(path, password);
            if (lifetime.IsCancellationRequested) { await ProfileBackend.CancelBackupRestoreAsync(view.Token); return; }
            PresentBackupRestorePreview(view);
        }
        catch { backupRestorePreview = null; BackupStatus.Text = "Не удалось проверить копию. Проверь пароль и целостность файла."; }
        finally { RestorePasswordBox.Clear(); backupPending = false; UpdateBackupControls(); }
    }

    private void PresentBackupRestorePreview(BackupRestorePreview view)
    {
        backupRestorePreview = view; AddRestoreSchedulesBox.IsChecked = false;
        BackupStatus.Text = $"Будет заменено профилей: {view.CurrentProfiles} → {view.ReplacementProfiles}. В копии расписаний: {view.EnabledSchedules}. Предпросмотр действует до {view.ExpiresAt}.";
        UpdateBackupControls();
        UpdateControlInspector();
    }

    private async void CancelBackupPreview_Click(object sender, RoutedEventArgs e) => await CancelBackupRestoreAsync();
    private async Task CancelBackupRestoreAsync()
    {
        if (backupRestorePreview is not { } view || backupPending || backupApplyPending) return;
        backupPending = true; UpdateBackupControls();
        try { await ProfileBackend.CancelBackupRestoreAsync(view.Token); BackupStatus.Text = "Предпросмотр отменён."; }
        catch { BackupStatus.Text = "Не удалось отменить предпросмотр. Он завершится автоматически."; }
        finally { backupRestorePreview = null; AddRestoreSchedulesBox.IsChecked = false; RestorePasswordBox.Clear(); backupPending = false; UpdateBackupControls(); }
    }

    private async void ApplyBackupRestore_Click(object sender, RoutedEventArgs e)
    {
        if (backupRestorePreview is not { CanApply: true } view || !connection.Actions.CanSave) return;
        bool restoreSchedules = AddRestoreSchedulesBox.IsChecked == true;
        string schedules = restoreSchedules ? " Расписания останутся включёнными и будут запрашивать источники, пока окно OneTunnel открыто." : " Расписания из копии будут отключены.";
        if (MessageBox.Show(this, $"Заменить локальную библиотеку? Сейчас профилей: {view.CurrentProfiles}; в копии: {view.ReplacementProfiles}. Включённых расписаний: {view.EnabledSchedules}.{schedules}", "Восстановление резервной копии", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await ApplyBackupRestoreAsync(restoreSchedules);
    }

    private async Task ApplyBackupRestoreAsync(bool restoreSchedules)
    {
        if (backupRestorePreview is not { CanApply: true } view || backupApplyPending || !connection.Actions.CanSave) return;
        backupApplyPending = true; busy = true; UpdateBackupControls();
        try
        {
            var document = await ProfileBackend.ApplyBackupRestoreAsync(view.Token, restoreSchedules);
            PresentDocument(document, document.Profiles.FirstOrDefault()?.Id);
            BackupStatus.Text = "Библиотека восстановлена из копии."; Feedback.Text = "Локальная библиотека заменена после подтверждения.";
            _ = LoadSubscriptionsAsync();
        }
        catch { BackupStatus.Text = "Не удалось применить копию. Библиотека могла измениться; обнови её и проверь состояние."; }
        finally { backupRestorePreview = null; AddRestoreSchedulesBox.IsChecked = false; RestorePasswordBox.Clear(); backupApplyPending = false; busy = false; if (!lifetime.IsCancellationRequested) RefreshControls(); }
    }

    private void PresentDocument(ProfileDocument document,Guid? selectedId=null,bool refreshLibrary=true)
    {
        InvalidateGroupSummaries();
        InvalidateProfileOrganization("Сведения профиля обновляются…", clearFields: false);
        InvalidateNetworkPreferences("Настройки сети обновляются…", clearFields: false);
        subscriptionScheduleVersion++; subscriptionScheduleView = null;
        Storage.Accept(document);RefreshGroups(document.Profiles);
        applyingProfileDocument = true;
        var automaticProfileId = (UserAutoConnectProfileBox.SelectedItem as Profile)?.Id;
        bool wasUpdatingUserSettings = updatingUserSettingsControls;
        updatingUserSettingsControls = true;
        try
        {
            profiles.Clear();foreach(var profile in document.Profiles)profiles.Add(profile);
            UserAutoConnectProfileBox.SelectedItem = profiles.FirstOrDefault(p => p.Id == automaticProfileId);
            pendingLibrarySelection = selectedId;
            RefreshProfileFilter(true);
            applyingLibrarySelection = true;
            try { ProfilesList.SelectedItem=profiles.FirstOrDefault(p=>p.Id==selectedId)??profiles.FirstOrDefault(); }
            finally { applyingLibrarySelection = false; }
        }
        finally { applyingProfileDocument = false; updatingUserSettingsControls = wasUpdatingUserSettings; }
        UpdateLibraryControls();
        if (refreshLibrary) {
            _ = RefreshLibraryProjectionAsync();
            if ((GroupsPanel.Visibility == Visibility.Visible||groupedProfiles)&&!groupsPending) _ = RefreshGroupSummariesAsync();
        }
        if (groupResultsView?.HasResults == true) _ = RefreshGroupResultsAsync();
        if (tray != null) _ = RefreshTrayProfilesAsync();
        if (selected is { } currentProfile) { _ = LoadProfileOrganizationAsync(currentProfile.Id); _ = LoadNetworkPreferencesAsync(currentProfile.Id); }
    }
    private void PresentApplied(AppliedProfiles result)
    {
        if(result.Batch)SetGrouping(true);
        sidebarGroup = null;
        SearchBox.Clear();PresentDocument(result.Document,result.Selected?.Id??selected?.Id);ClearEditorContent();ShowPanel("connection");
        Feedback.Text=result.Batch?$"Подписка обновлена / импортирована. Обновлено: {result.Updated}. Добавлено: {result.Added}. Устаревших удалено: {result.Removed}. Повторов: {result.Duplicates}. Неподдерживаемых: {result.Skipped}.":"Профиль сохранён. Ключи защищены учётной записью Windows.";
        Log(result.Batch?"Импорт завершён Go-ядром.":"Профиль сохранён.");
    }
    private bool MatchesProfile(object item)
    {
        if (item is not Profile profile) return false;
        if (connection.ProfileId == profile.Id) return true;
        if (sidebarGroup != null && profile.Group != sidebarGroup) return false;
        if (libraryProjectionLoaded && !libraryIDs.Contains(profile.Id)) return false;
        string query = SearchBox?.Text.Trim() ?? "";
        return query.Length == 0 || profile.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || profile.Group.Contains(query, StringComparison.OrdinalIgnoreCase) || profile.Protocol.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshProfileFilter();
    private void RefreshProfileFilter(bool force = false)
    {
        if (!IsInitialized || SearchBox == null || ProfilesList == null || EmptyProfilesHint == null) return;
        UpdateControlNavigation();
        string query = SearchBox.Text.Trim();
        if (!force && appliedSearch == query && appliedSession == connection.ProfileId) return;
        // Record inputs before Refresh raises synchronous selection events.
        appliedSearch = query;
        appliedSession = connection.ProfileId;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(profiles);
        view.Refresh();
        int visible = view.Cast<Profile>().Count();
        ProfileCount.Text = profiles.Count == visible ? $"{profiles.Count} проф." : $"{visible} из {profiles.Count} проф.";
        EmptyProfilesHint.Text = profiles.Count == 0 ? "Профилей пока нет. Импортируй подключение, чтобы начать." : FavoritesOnlyBox.IsChecked == true && query.Length == 0 ? "В избранном пока нет подключений. Добавь профиль в избранное." : "Профили не найдены. Очисти поиск или измени условия списка.";
        EmptyProfilesHint.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LibraryFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || updatingLibraryControls) return;
        favoritesOnly = FavoritesOnlyBox.IsChecked == true;
        _ = RefreshLibraryProjectionAsync();
    }
    private void LibrarySort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || updatingLibraryControls) return;
        librarySort = (LibrarySortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stored";
        _ = RefreshLibraryProjectionAsync();
    }
    private async void Favorite_Click(object sender, RoutedEventArgs e) { if (selected is { } profile) await FavoriteProfileAsync(profile.Id); }
    private async Task FavoriteProfileAsync(Guid id)
    {
        if (favoritePending || libraryPending || busy || !connection.Actions.CanEdit || !profiles.Any(profile => profile.Id == id)) return;
        var preserveSelection = selected?.Id;
        favoritePending = true; UpdateLibraryControls();
        try {
            var result = await ProfileBackend.FavoriteAsync(id, !Storage.FavoriteIDs.Contains(id));
            if (lifetime.IsCancellationRequested) return;
            PresentDocument(result, preserveSelection, refreshLibrary: false);
            Feedback.Text = "Избранное обновлено.";
            await RefreshLibraryProjectionAsync();
        }
        catch { Feedback.Text = "Не удалось подтвердить изменение избранного. Повтори действие, чтобы обновить состояние."; }
        finally { favoritePending = false; if (!lifetime.IsCancellationRequested) { UpdateLibraryControls(); UpdateControlInspector(); } }
    }
    private async Task RefreshLibraryProjectionAsync()
    {
        int request = ++libraryRequestVersion;
        libraryPending = true;
        UpdateLibraryControls();
        try
        {
            var projection = await ProfileBackend.LibraryAsync(librarySort, favoritesOnly);
            if (request != libraryRequestVersion || lifetime.IsCancellationRequested) return;
            libraryIDs = projection.IDs.ToHashSet();
            libraryRanks = projection.IDs.Select((id, rank) => (id, rank)).ToDictionary(pair => pair.id, pair => pair.rank);
            libraryProjectionLoaded = true;
            appliedLibrarySort = librarySort;
            appliedFavoritesOnly = favoritesOnly;
            var selectionToRestore = (fastestSelectionPending || traySelectionPending) && !connection.Actions.CanSelect ? selected?.Id : pendingLibrarySelection ?? selected?.Id;
            if (profileCollectionView != null) profileCollectionView.CustomSort = new ProfileRankComparer(libraryRanks);
            LibraryStatus.Text = "";
            RefreshProfileFilter(true);
            if (selectionToRestore is { } selectedId && profiles.FirstOrDefault(p => p.Id == selectedId) is { } requested && MatchesProfile(requested))
            {
                applyingLibrarySelection = true;
                try { ProfilesList.SelectedItem = requested; }
                finally { applyingLibrarySelection = false; }
            }
            pendingLibrarySelection = null;
        }
        catch
        {
            if (request != libraryRequestVersion || lifetime.IsCancellationRequested) return;
            librarySort = appliedLibrarySort;
            favoritesOnly = appliedFavoritesOnly;
            updatingLibraryControls = true;
            try
            {
                FavoritesOnlyBox.IsChecked = appliedFavoritesOnly;
                LibrarySortBox.SelectedItem = LibrarySortBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == appliedLibrarySort);
            }
            finally { updatingLibraryControls = false; }
            LibraryStatus.Text = "Не удалось обновить список. Показан предыдущий вариант.";
            Feedback.Text = "Не удалось обновить список профилей. Показан предыдущий вариант.";
            RefreshProfileFilter(true);
        }
        finally
        {
            if (request == libraryRequestVersion)
            {
                libraryPending = false;
                if (!lifetime.IsCancellationRequested) UpdateLibraryControls();
            }
        }
    }
    private void UpdateLibraryControls()
    {
        if (!IsInitialized || LibrarySortBox == null) return;
        FavoritesOnlyBox.IsEnabled = LibrarySortBox.IsEnabled = SearchBox.IsEnabled = !libraryPending && !favoritePending && !fastestSelectionPending;
        FavoriteButton.Content = selected != null && Storage.FavoriteIDs.Contains(selected.Id) ? "Убрать из избранного" : "Добавить в избранное";
        FavoriteButton.IsEnabled = selected != null && connection.Actions.CanEdit && !busy && !libraryPending && !favoritePending;
    }
    private void UpdateGroupResultControls()
    {
        if (!IsInitialized || GroupResultSortBox == null) return;
        bool locked = fastestSelectionPending || busy;
        bool pending = groupResultsPending || locked;
        GroupResultSortBox.IsEnabled = GroupResultFilterBox.IsEnabled = !locked;
        SelectFastestResultButton.IsEnabled = !pending && groupResultsView is { HasResults: true, CanSelectFastest: true } && connection.Actions.CanSelect;
        ClearGroupResultsButton.IsEnabled = !pending && groupResultsView != null;
    }
    private void UpdateProfileOrganizationControls()
    {
        if (!IsInitialized || ProfileOrganizationNameBox == null) return;
        var view = profileOrganizationView;
        bool pending = profileOrganizationPending || profileOrganizationMutationPending;
        bool matchesSelection = view != null && Guid.TryParse(view.ID, out var id) && selected?.Id == id;
        bool canEdit = matchesSelection && connection.Actions.CanEdit && !busy && !pending;
        ProfileOrganizationNameBox.IsEnabled = canEdit && view?.CanDuplicate == true;
        ProfileOrganizationGroupBox.IsEnabled = canEdit && (view?.CanDuplicate == true || view?.CanMove == true);
        DuplicateProfileButton.IsEnabled = canEdit && view?.CanDuplicate == true;
        MoveProfileButton.IsEnabled = canEdit && view?.CanMove == true;
        ReloadProfileOrganizationButton.IsEnabled = selected != null && !busy && !pending;
    }
    private void InvalidateProfileOrganization(string message, bool clearFields = true)
    {
        profileOrganizationRequestVersion++;
        profileOrganizationRequestedId = null;
        profileOrganizationPending = false;
        profileOrganizationView = null;
        ProfileOrganizationGroupBox.ItemsSource = null;
        if (clearFields)
        {
            profileOrganizationFieldsId = null;
            profileOrganizationFieldsToken = null;
            ProfileOrganizationNameBox.Clear();
            ProfileOrganizationGroupBox.Text = "";
        }
        ProfileOrganizationStatus.Text = message;
        UpdateProfileOrganizationControls();
        UpdateNetworkPreferencesControls();
    }
    private async Task LoadProfileOrganizationAsync(Guid id)
    {
        if (profileOrganizationPending && profileOrganizationRequestedId == id) return;
        int request = ++profileOrganizationRequestVersion;
        profileOrganizationRequestedId = id;
        profileOrganizationPending = true;
        ProfileOrganizationStatus.Text = "Загрузка сведений профиля…";
        UpdateProfileOrganizationControls();
        UpdateNetworkPreferencesControls();
        try
        {
            await connection.SetContextAsync(new(busy, storageReady, id.ToString(), groupCheck != null));
            if (request != profileOrganizationRequestVersion || lifetime.IsCancellationRequested || selected?.Id != id) return;
            var view = await ProfileBackend.ProfileOrganizationAsync(id);
            if (request != profileOrganizationRequestVersion || lifetime.IsCancellationRequested || selected?.Id != id) return;
            if (!Guid.TryParse(view.ID, out var returnedId) || returnedId != id) throw new InvalidDataException();
            PresentProfileOrganization(view);
        }
        catch
        {
            if (request != profileOrganizationRequestVersion || lifetime.IsCancellationRequested) return;
            profileOrganizationView = null;
            ProfileOrganizationStatus.Text = "Не удалось обновить сведения профиля. Повтори попытку.";
        }
        finally
        {
            if (request == profileOrganizationRequestVersion)
            {
                profileOrganizationPending = false;
                if (!lifetime.IsCancellationRequested) UpdateProfileOrganizationControls();
            }
        }
    }
    private void PresentProfileOrganization(ProfileOrganizationView view)
    {
        bool sameSnapshot = Guid.TryParse(view.ID, out var viewId) && profileOrganizationFieldsId == viewId && profileOrganizationFieldsToken == view.Token;
        if (!sameSnapshot)
        {
            ProfileOrganizationNameBox.Text = view.SuggestedName;
            ProfileOrganizationGroupBox.Text = view.SuggestedGroup;
        }
        profileOrganizationFieldsId = Guid.TryParse(view.ID, out viewId) ? viewId : null;
        profileOrganizationFieldsToken = view.Token;
        profileOrganizationView = view;
        profileOrganizationRequestedId = Guid.TryParse(view.ID, out var id) ? id : null;
        ProfileOrganizationGroupBox.ItemsSource = view.ManualTargets;
        ProfileOrganizationStatus.Text = "";
        UpdateProfileOrganizationControls();
        UpdateNetworkPreferencesControls();
    }
    private async Task RefreshProfileOrganizationAsync()
    {
        if (selected is { } profile) await LoadProfileOrganizationAsync(profile.Id);
        else InvalidateProfileOrganization("Выбери профиль, чтобы управлять копией или группой.");
    }
    private async void ReloadProfileOrganization_Click(object sender, RoutedEventArgs e) => await RefreshProfileOrganizationAsync();
    private async void DuplicateProfile_Click(object sender, RoutedEventArgs e) => await DuplicateSelectedProfileAsync();
    private async void MoveProfile_Click(object sender, RoutedEventArgs e) => await MoveSelectedProfileAsync();
    private Task DuplicateSelectedProfileAsync() => ApplyProfileOrganizationAsync(duplicate: true);
    private Task MoveSelectedProfileAsync() => ApplyProfileOrganizationAsync(duplicate: false);
    private async Task ApplyProfileOrganizationAsync(bool duplicate)
    {
        if (profileOrganizationView is not { } view || !Guid.TryParse(view.ID, out var id) || selected?.Id != id ||
            busy || profileOrganizationPending || profileOrganizationMutationPending || !connection.Actions.CanEdit ||
            (duplicate ? !view.CanDuplicate : !view.CanMove)) return;
        string name = ProfileOrganizationNameBox.Text;
        string group = ProfileOrganizationGroupBox.Text;
        string token = view.Token;
        profileOrganizationMutationPending = true;
        busy = true;
        RefreshControls();
        try
        {
            var applied = duplicate
                ? await ProfileBackend.DuplicateProfileAsync(id, name, group, token)
                : await ProfileBackend.MoveProfileAsync(id, group, token);
            if (lifetime.IsCancellationRequested) return;
            Guid? currentSelection = selected?.Id;
            Guid? copyId = duplicate ? applied.Selected?.Id ?? throw new InvalidDataException() : null;
            Guid? selectionToPreserve = currentSelection != id ? currentSelection : duplicate ? copyId : applied.Selected?.Id ?? id;
            PresentDocument(applied.Document, selectionToPreserve, refreshLibrary: false);
            bool revealCopy = duplicate && selectionToPreserve == copyId && copyId is not null;
            if (revealCopy) PrepareOrganizationProfileReveal(copyId!.Value);
            await RefreshLibraryProjectionAsync();
            if (GroupsPanel.Visibility == Visibility.Visible) await RefreshGroupSummariesAsync();
            bool copyVisible = copyId is { } newCopyId && profiles.Any(p => p.Id == newCopyId) && MatchesProfile(profiles.First(p => p.Id == newCopyId));
            Feedback.Text = duplicate
                ? (!revealCopy || copyVisible ? "Копия профиля создана. Подключение не запускалось." : "Копия создана, но список не удалось обновить. Проверь список профилей перед повтором.")
                : "Профиль перемещён. Порядок хранения сохранён.";
        }
        catch
        {
            InvalidateProfileOrganization("Сведения могут быть устаревшими. Обнови их перед повтором.", clearFields: false);
            try
            {
                var document = await ProfileBackend.LoadDocumentAsync();
                if (!lifetime.IsCancellationRequested)
                {
                    PresentDocument(document, selected?.Id ?? id, refreshLibrary: false);
                    await RefreshLibraryProjectionAsync();
                    if (GroupsPanel.Visibility == Visibility.Visible) await RefreshGroupSummariesAsync();
                }
            }
            catch { }
            Feedback.Text = "Не удалось подтвердить результат операции. Проверь список профилей перед повтором.";
            ProfileOrganizationStatus.Text = "Сведения могут быть устаревшими. Обнови их перед повтором.";
        }
        finally
        {
            profileOrganizationMutationPending = false;
            busy = false;
            if (!lifetime.IsCancellationRequested) RefreshControls();
        }
    }
    private void PrepareOrganizationProfileReveal(Guid id)
    {
        bool reloadLibrary = favoritesOnly || FavoritesOnlyBox.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) SearchBox.Clear();
        if (reloadLibrary)
        {
            updatingLibraryControls = true;
            try { favoritesOnly = false; FavoritesOnlyBox.IsChecked = false; }
            finally { updatingLibraryControls = false; }
            pendingLibrarySelection = id;
        }
        RefreshProfileFilter(true);
    }
    private void UpdateNetworkPreferencesControls()
    {
        if (!IsInitialized || NetworkDNSModeBox == null) return;
        var view = networkPreferencesView;
        bool matching = view != null && Guid.TryParse(view.ID, out var id) && selected?.Id == id;
        bool pending = networkPreferencesPending || networkPreferencesSavePending || busy || policyToolsPending;
        bool supported = matching && view!.Supported;
        bool customDNS = supported && GetNetworkDNSMode() != "default";
        NetworkDNSModeBox.IsEnabled = supported && !pending;
        NetworkServerIPBox.IsEnabled = NetworkServerNameBox.IsEnabled = NetworkPortBox.IsEnabled = customDNS && !pending;
        NetworkPathBox.IsEnabled = supported && (GetNetworkDNSMode() is "https" or "h3") && !pending;
        NetworkFakeIPBox.IsEnabled = NetworkCacheModeBox.IsEnabled = supported && !pending;
        NetworkCacheCapacityBox.IsEnabled = supported && !pending && ChoiceTag(NetworkCacheModeBox) == "bounded";
        NetworkRulesBox.IsEnabled = supported && !pending;
        RoutingModeBox.IsEnabled = supported && !pending && !policyToolsPending;
        CustomRoutingPanel.IsEnabled=LoyalRulesPanel.IsEnabled=supported&&!pending;
        UpdateRoutingVisibility();
        NetworkResolversBox.IsEnabled = NetworkDNSRulesBox.IsEnabled = NetworkAllowLANBox.IsEnabled = supported && !pending;
        SaveNetworkPreferencesButton.IsEnabled = matching && view!.CanSave && supported && connection.Actions.CanSave && !pending;
        ReloadNetworkPreferencesButton.IsEnabled = selected != null && !pending;
        UpdatePolicyTools();
    }
    private string GetNetworkDNSMode()
    {
        return NetworkDNSModeBox.SelectedItem is ComboBoxItem item && item.Tag is string value ? value : "default";
    }
    private void InvalidateNetworkPreferences(string message, bool clearFields = true)
    {
        networkPreferencesRequestVersion++;
        networkPreferencesRequestedId = null;
        networkPreferencesPending = false;
        networkPreferencesView = null;
        if (clearFields)
        {
            networkPreferencesFieldsId = null;
            networkPreferencesFieldsToken = null;
            NetworkDNSModeBox.SelectedIndex = 0;
            NetworkServerIPBox.Clear(); NetworkServerNameBox.Clear(); NetworkPortBox.Clear(); NetworkPathBox.Clear(); NetworkRulesBox.Clear();
            ResetRoutingDrafts();
            NetworkResolversBox.Clear(); NetworkDNSRulesBox.Clear(); NetworkAllowLANBox.IsChecked = false;
            NetworkFakeIPBox.IsChecked = false; NetworkCacheModeBox.SelectedIndex = 0; NetworkCacheCapacityBox.Clear();
        }
        NetworkPreferencesStatus.Text = message;
        UpdateNetworkPreferencesControls();
    }
    private async Task LoadNetworkPreferencesAsync(Guid id, bool resetFields = false)
    {
        if (networkPreferencesPending && networkPreferencesRequestedId == id) return;
        int request = ++networkPreferencesRequestVersion;
        networkPreferencesRequestedId = id;
        networkPreferencesPending = true;
        NetworkPreferencesStatus.Text = "Загрузка настроек сети…";
        UpdateNetworkPreferencesControls();
        try
        {
            await connection.SetContextAsync(new(busy, storageReady, id.ToString(), groupCheck != null));
            if (request != networkPreferencesRequestVersion || lifetime.IsCancellationRequested || selected?.Id != id) return;
            var view = await ProfileBackend.NetworkPreferencesAsync(id);
            if (request != networkPreferencesRequestVersion || lifetime.IsCancellationRequested || selected?.Id != id) return;
            if (!Guid.TryParse(view.ID, out var returnedId) || returnedId != id) throw new InvalidDataException();
            PresentNetworkPreferences(view, resetFields);
        }
        catch
        {
            if (request != networkPreferencesRequestVersion || lifetime.IsCancellationRequested) return;
            networkPreferencesView = null;
            NetworkPreferencesStatus.Text = "Не удалось обновить настройки сети. Обнови сведения перед сохранением.";
        }
        finally
        {
            if (request == networkPreferencesRequestVersion)
            {
                networkPreferencesPending = false;
                if (!lifetime.IsCancellationRequested) UpdateNetworkPreferencesControls();
            }
        }
    }
    private void PresentNetworkPreferences(NetworkPreferencesView view, bool resetFields = false)
    {
        bool sameSnapshot = Guid.TryParse(view.ID, out var id) && networkPreferencesFieldsId == id && networkPreferencesFieldsToken == view.Token;
        if (resetFields || !sameSnapshot)
        {
            int modeIndex = view.Mode switch { "https" => 1, "tls" => 2, "quic" => 3, "h3" => 4, _ => 0 };
            NetworkDNSModeBox.SelectedIndex = modeIndex;
            NetworkServerIPBox.Text = view.ServerIP;
            NetworkServerNameBox.Text = view.ServerName;
            NetworkPortBox.Text = view.Port;
            NetworkPathBox.Text = view.Path;
            PresentRoutingDrafts(view);
            NetworkResolversBox.Text = view.ResolversText;
            NetworkDNSRulesBox.Text = view.DNSRulesText;
            NetworkAllowLANBox.IsChecked = view.AllowLAN;
            NetworkFakeIPBox.IsChecked = view.FakeIP;
            NetworkCacheModeBox.SelectedIndex = view.CacheMode switch { "disabled" => 1, "bounded" => 2, _ => 0 };
            NetworkCacheCapacityBox.Text = view.CacheCapacity;
        }
        networkPreferencesFieldsId = Guid.TryParse(view.ID, out id) ? id : null;
        networkPreferencesFieldsToken = view.Token;
        networkPreferencesView = view;
        PolicyPreferencesRefreshed();
        networkPreferencesRequestedId = Guid.TryParse(view.ID, out id) ? id : null;
        NetworkPreferencesStatus.Text = view.Supported ? view.Message : (string.IsNullOrWhiteSpace(view.Message) ? "Настройки DNS и маршрутизации недоступны для этого профиля или режима." : view.Message);
        UpdateNetworkPreferencesControls();
    }
    private void NetworkDNSMode_Changed(object sender, SelectionChangedEventArgs e) => UpdateNetworkPreferencesControls();
    private async void ReloadNetworkPreferences_Click(object sender, RoutedEventArgs e)
    {
        if (selected is { } profile) await LoadNetworkPreferencesAsync(profile.Id, resetFields: true);
    }
    private async void SaveNetworkPreferences_Click(object sender, RoutedEventArgs e) => await SaveNetworkPreferencesAsync();
    private async Task SaveNetworkPreferencesAsync()
    {
        if (networkPreferencesView is not { } view || !Guid.TryParse(view.ID, out var id) || selected?.Id != id ||
            !view.Supported || !view.CanSave || !connection.Actions.CanSave || busy || networkPreferencesPending || networkPreferencesSavePending || policyToolsPending) return;
        string mode = GetNetworkDNSMode();
        var input = new NetworkInput(mode, mode == "default" ? "" : NetworkServerIPBox.Text,
            mode == "default" ? "" : NetworkServerNameBox.Text, mode == "default" ? "" : NetworkPortBox.Text,
            mode is "https" or "h3" ? NetworkPathBox.Text : "", NetworkRulesBox.Text, NetworkResolversBox.Text,
            NetworkDNSRulesBox.Text, NetworkAllowLANBox.IsChecked == true, NetworkFakeIPBox.IsChecked == true, ChoiceTag(NetworkCacheModeBox),
            ChoiceTag(NetworkCacheModeBox) == "default" ? "" : NetworkCacheCapacityBox.Text)
        { RoutingMode=routingDraftMode,CustomRulesText=CustomRoutingText(),PresetRulesText=PresetRoutingText(),PresetConfigText=presetConfigDraft };
        string token = view.Token;
        networkPreferencesSavePending = true;
        busy = true;
        NetworkPreferencesStatus.Text = "Сохранение настроек сети…";
        RefreshControls();
        try
        {
            var document = await ProfileBackend.SaveNetworkPreferencesAsync(id, token, input);
            if (lifetime.IsCancellationRequested) return;
            PresentDocument(document, selected?.Id ?? id, refreshLibrary: false);
            await RefreshLibraryProjectionAsync();
            NetworkPreferencesStatus.Text = "Настройки сохранены. Они будут использованы при следующем подключении.";
            Feedback.Text = "DNS и правила сохранены. Текущее соединение не изменено.";
        }
        catch
        {
            InvalidateNetworkPreferences("Не удалось подтвердить сохранение. Обнови сведения перед повтором.", clearFields: false);
            NetworkPreferencesStatus.Text = "Не удалось подтвердить сохранение. Обнови сведения перед повтором.";
            Feedback.Text = "Не удалось подтвердить сохранение DNS и правил. Проверь профиль перед повтором.";
        }
        finally
        {
            networkPreferencesSavePending = false;
            busy = false;
            if (!lifetime.IsCancellationRequested) RefreshControls();
        }
    }
    private sealed class ProfileRankComparer(Dictionary<Guid, int> ranks) : IComparer
    {
        public int Compare(object? left, object? right)
        {
            int leftRank = Rank(left);
            int rightRank = Rank(right);
            return leftRank.CompareTo(rightRank);
        }
        private int Rank(object? value)
        {
            if (value is Profile profile && ranks.TryGetValue(profile.Id, out var rank)) return rank;
            if (value is System.Windows.Data.CollectionViewGroup group)
                return group.Items.OfType<Profile>().Select(item => ranks.TryGetValue(item.Id, out var itemRank) ? itemRank : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();
            return int.MaxValue;
        }
    }
    private bool CanMutateGroups => connection.Actions.CanAdd && !busy && !groupsPending && !groupMutationPending;
    private void InvalidateGroupSummaries()
    {
        groupsLoaded = false;
        ++groupRequestVersion;
        groupsPending = false;
    }
    private void UpdateGroupControls()
    {
        if (!IsInitialized || ConfigurationGroupsList == null) return;
        bool canMutate = CanMutateGroups && selectedGroup != null;
        ConfigurationGroupsList.IsEnabled = !groupsPending && !groupMutationPending;
        ReloadGroupsButton.IsEnabled = !busy && !groupsPending && !groupMutationPending;
        RenameGroupBox.IsEnabled = RenameGroupButton.IsEnabled = DeleteGroupButton.IsEnabled = canMutate;
        ExpandAllGroupsButton.IsEnabled = CollapseAllGroupsButton.IsEnabled = connection.Actions.CanSelect && !busy && !groupsPending && !groupMutationPending;
        UpdateProfileGroupHeaders();
    }
    private async Task RefreshGroupSummariesAsync(string? preferredName = null)
    {
        int request = ++groupRequestVersion;
        string? preserveName = preferredName ?? selectedGroup?.Name;
        groupsPending = true;
        UpdateGroupControls();
        try
        {
            var result = await ProfileBackend.GroupsAsync();
            if (request != groupRequestVersion || lifetime.IsCancellationRequested) return;
            applyingGroupSelection = true;
            try
            {
                groupSummaries.Clear();
                foreach (var group in result) groupSummaries.Add(group);
                var keep = groupSummaries.FirstOrDefault(group => group.Name == preserveName) ?? groupSummaries.FirstOrDefault();
                ConfigurationGroupsList.SelectedItem = keep;
                RenameGroupBox.Text = keep?.Name ?? "";
            }
            finally { applyingGroupSelection = false; }
            groupsLoaded = true;
            GroupsEmptyHint.Visibility = groupSummaries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GroupsStatus.Text = "";
            UpdateGroupControls();
        }
        catch
        {
            if (request != groupRequestVersion || lifetime.IsCancellationRequested) return;
            GroupsStatus.Text = "Не удалось обновить список. Показан прежний список.";
            Feedback.Text = "Не удалось подтвердить список групп. Обнови его перед повтором.";
        }
        finally
        {
            if (request == groupRequestVersion)
            {
                groupsPending = false;
                if (!lifetime.IsCancellationRequested) UpdateGroupControls();
            }
        }
    }
    private void ConfigurationGroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (applyingGroupSelection) return;
        RenameGroupBox.Text = selectedGroup?.Name ?? "";
        UpdateGroupControls();
    }
    private void Groups_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel("groups");
        if (!groupsLoaded) _ = RefreshGroupSummariesAsync();
    }
    private void ReloadGroups_Click(object sender, RoutedEventArgs e)
    {
        if (!busy && !groupsPending && !groupMutationPending) _ = RefreshGroupSummariesAsync();
    }
    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutateGroups || selectedGroup is not { } group) return;
        string targetName = RenameGroupBox.Text;
        await ApplyGroupMutationAsync(() => ProfileBackend.RenameGroupAsync(group.Name, targetName, group.Token), targetName.Trim(), "Группа переименована.");
    }
    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!CanMutateGroups || selectedGroup is not { } group) return;
        await ConfirmDeleteGroupAsync(group);
    }
    private async Task ConfirmDeleteGroupAsync(GroupSummary group)
    {
        if(!CanMutateGroups)return;
        string subscriptionNote = group.IsSubscription ? " Локальный источник обновления подписки тоже будет удалён." : "";
        string prompt = $"Удалить группу «{group.Name}» и {group.ProfileCount} проф.?{subscriptionNote}\n\nЭто действие нельзя отменить.";
        if (MessageBox.Show(this, prompt, "Удалить группу", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await DeleteSelectedGroupAsync(group);
    }
    private async Task DeleteSelectedGroupAsync(GroupSummary group)
    {
        if (!CanMutateGroups) return;
        await ApplyGroupMutationAsync(() => ProfileBackend.DeleteGroupAsync(group.Name, group.Token), null, "Группа удалена.");
    }
    private async Task ApplyGroupMutationAsync(Func<Task<ProfileDocument>> mutation, string? preferredGroup, string successMessage)
    {
        if (!CanMutateGroups) return;
        Guid? preserveProfile = selected?.Id;
        groupMutationPending = true; busy = true;
        UpdateGroupControls(); RefreshControls();
        try
        {
            var document = await mutation();
            if (lifetime.IsCancellationRequested) return;
            PresentDocument(document, preserveProfile, refreshLibrary: false);
            Feedback.Text = successMessage;
            await RefreshLibraryProjectionAsync();
            await RefreshGroupSummariesAsync(preferredGroup);
        }
        catch { Feedback.Text = "Не удалось подтвердить изменение группы. Обнови список перед повтором."; GroupsStatus.Text = "Результат не подтверждён. Обнови список групп."; }
        finally
        {
            groupMutationPending = false; busy = false;
            if (!lifetime.IsCancellationRequested) { RefreshControls(); UpdateGroupControls(); }
        }
    }
    private async void ExpandAllGroups_Click(object sender, RoutedEventArgs e) => await SetAllGroupsExpandedAsync(true);
    private async void CollapseAllGroups_Click(object sender, RoutedEventArgs e) => await SetAllGroupsExpandedAsync(false);
    private async Task SetAllGroupsExpandedAsync(bool expanded)
    {
        if (!connection.Actions.CanSelect||busy||groupsPending||groupMutationPending) return;
        bool wasRestoringGroupExpansion = restoringGroupExpansion;
        groupMutationPending = true; busy = true; applyingBulkGroupExpansion = true;
        restoringGroupExpansion = true;
        UpdateGroupControls(); RefreshControls();
        try
        {
            var document = await ProfileBackend.SetAllGroupsExpandedAsync(expanded);
            if (lifetime.IsCancellationRequested) return;
            PresentDocument(document, selected?.Id, refreshLibrary: false);
            await RefreshLibraryProjectionAsync();
            Feedback.Text = expanded ? "Все группы развёрнуты." : "Все группы свёрнуты.";
        }
        catch { Feedback.Text = "Не удалось подтвердить изменение групп. Обнови список и повтори действие."; }
        finally
        {
            restoringGroupExpansion = wasRestoringGroupExpansion;
            applyingBulkGroupExpansion = false; groupMutationPending = false; busy = false;
            if (!lifetime.IsCancellationRequested) { RefreshControls(); UpdateGroupControls(); }
        }
    }
    private void RefreshGroups(IEnumerable<Profile>? items = null) => GroupBox.ItemsSource = (items ?? profiles).Select(p => p.Group).Distinct().ToList();
    private void Group_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander exp || exp.DataContext is not System.Windows.Data.CollectionViewGroup group) return;
        bool wasRestoring = restoringGroupExpansion;
        restoringGroupExpansion = true;
        try { exp.IsExpanded = !Storage.CollapsedGroups.Contains(group.Name.ToString()!); }
        finally { restoringGroupExpansion = wasRestoring; }
        UpdateProfileGroupHeader(exp);
        if(!groupsLoaded&&!groupsPending)_=RefreshGroupSummariesAsync();
    }
    private int groupExpansionVersion;
    private async void Group_Changed(object sender, RoutedEventArgs e)
    {
        if(sender is Expander header)UpdateProfileGroupHeader(header);
        if (restoringGroupExpansion || applyingBulkGroupExpansion || sender is not Expander exp || !exp.IsLoaded || !storageReady || exp.DataContext is not System.Windows.Data.CollectionViewGroup group) return;
        string name = group.Name.ToString()!;
        if (Storage.CollapsedGroups.Contains(name) == !exp.IsExpanded) return;
        int expansionRequest=++groupExpansionVersion;
        var before = Storage.CollapsedGroups.ToHashSet();
        if (exp.IsExpanded) Storage.CollapsedGroups.Remove(name); else Storage.CollapsedGroups.Add(name);
        try { var document=await ProfileBackend.SetGroupExpandedAsync(name,exp.IsExpanded); if(expansionRequest!=groupExpansionVersion||lifetime.IsCancellationRequested)return; Storage.Accept(document); InvalidateGroupSummaries(); _ = RefreshGroupSummariesAsync(); InvalidateProfileOrganization("Сведения профиля обновляются…", clearFields: false); InvalidateNetworkPreferences("Настройки сети обновляются…", clearFields: false); if (selected is { } currentProfile) { _ = LoadProfileOrganizationAsync(currentProfile.Id); _ = LoadNetworkPreferencesAsync(currentProfile.Id); } } catch { if(expansionRequest==groupExpansionVersion){Storage.CollapsedGroups.Clear(); Storage.CollapsedGroups.UnionWith(before); Feedback.Text = "Не удалось сохранить состояние группы.";} }
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
            finally { if(connection.Prepared is { } prepared) PresentDocument(await ProfileBackend.DocumentAsync(),selected?.Id??prepared.Selected?.Id); }
            PresentConnectionState(); Log("Команда подключения завершена Go-ядром.");
        });
    }
    private async void Probe_Click(object sender, RoutedEventArgs e) => await Execute(async () => { Feedback.Text = "Проверка…"; Feedback.Text = await connection.ProbeAsync(); Log("Проверка завершена."); });
    private async void Recovery_Click(object sender, RoutedEventArgs e) => await Execute(async () => { await connection.RecoverAsync(); Feedback.Text = "Подключение очищено, блокировка kill switch снята. Обычная сеть восстановлена."; Log("Восстановление завершено."); });
    private void Cancel_Click(object sender, RoutedEventArgs e) { ShowPanel("connection"); }
    private void CancelEditor_Click(object sender, RoutedEventArgs e) { ClearEditorContent(); ShowPanel("connection"); }
    private void Engines_Click(object sender, RoutedEventArgs e) { ShowPanel("engines"); }
    private void Home_Click(object sender, RoutedEventArgs e) { sidebarGroup = null; FavoritesOnlyBox.IsChecked = false; ShowPanel("connection"); RefreshProfileFilter(true); }
    private void Events_Click(object sender, RoutedEventArgs e) { ShowPanel("activity"); }
    private void UserSettings_Click(object sender, RoutedEventArgs e) { ShowPanel("settings"); }

    private async Task LoadUserSettingsAsync(bool resetDraft = false)
    {
        if (userSettingsPending || userSettingsTickPending || userSettingsActionPending || userSettingsSavePending || lifetime.IsCancellationRequested) return;
        if (resetDraft) { userSettingsDraftDirty = false; userSettingsDraftToken = null; }
        int request = ++userSettingsRequestVersion;
        userSettingsPending = true; UpdateUserSettingsControls();
        try
        {
            var view = await ProfileBackend.UserSettingsAsync(lifetime.Token);
            if (request != userSettingsRequestVersion || lifetime.IsCancellationRequested) return;
            PresentUserSettings(view, resetDraft);
        }
        catch
        {
            if (request == userSettingsRequestVersion && !lifetime.IsCancellationRequested)
                UserSettingsStatus.Text = "Настройки недоступны. Обнови сведения и повтори попытку.";
        }
        finally
        {
            userSettingsPending = false; UpdateUserSettingsControls();
        }
    }

    private async void UserSettingsTimer_Tick(object? sender, EventArgs e)
    {
        if (busy || userSettingsTickPending || userSettingsPending || userSettingsActionPending || userSettingsSavePending || lifetime.IsCancellationRequested) return;
        int request = ++userSettingsRequestVersion;
        userSettingsTickPending = true;
        UpdateUserSettingsControls();
        try
        {
            var result = await ProfileBackend.TickUserSettingsAsync(lifetime.Token);
            if (request != userSettingsRequestVersion || lifetime.IsCancellationRequested) return;
            if (result.Connection is { } connectionReply)
            {
                connection.ReceiveAutomationReply(connectionReply);
                PresentConnectionState();
                RefreshControls();
            }
            PresentUserSettings(result.View);
        }
        catch
        {
            if (request == userSettingsRequestVersion && !lifetime.IsCancellationRequested && UserSettingsPanel.Visibility == Visibility.Visible)
                UserSettingsStatus.Text = "Состояние автоматического подключения недоступно. Проверка будет повторена.";
        }
        finally
        {
            userSettingsTickPending = false;
            UpdateUserSettingsControls();
        }
    }

    private void PresentUserSettings(UserSettingsView view, bool resetDraft = false)
    {
        userSettingsView = view;
        if (resetDraft || !userSettingsDraftDirty)
        {
            userSettingsDraftDirty = false;
            userSettingsDraftToken = view.Token;
            updatingUserSettingsControls = true;
            try
            {
                UserSystemProxyBox.IsChecked = view.Preferences.UseSystemProxy;
                UserAutoConnectBox.IsChecked = view.Preferences.AutoConnect;
                UserAutoConnectProfileBox.SelectedItem = profiles.FirstOrDefault(p => string.Equals(p.Id.ToString(), view.Preferences.ProfileID, StringComparison.OrdinalIgnoreCase));
                UserFullDeviceBox.IsChecked = view.Preferences.FullDevice;
                UserAllowTrustedBox.IsChecked = view.Preferences.AllowTrusted;
                UserAllowPublicBox.IsChecked = view.Preferences.AllowPublic;
                LoadGlobalTransport(view.Preferences.Transport); LoadBalance(view.Preferences.Balance);
            }
            finally { updatingUserSettingsControls = false; }
        }
        UserSettingsStatus.Text = LocalizeUserNotice(view.Message);
        UpdateUserSettingsControls();
    }

    private void UserSettingsDraft_Changed(object sender, RoutedEventArgs e)
    {
        if (updatingUserSettingsControls) return;
        if (!userSettingsDraftDirty) userSettingsDraftToken = userSettingsView?.Token;
        userSettingsDraftDirty = true;
        UpdateUserSettingsControls();
    }

    private void UserAutoConnectProfile_Changed(object sender, SelectionChangedEventArgs e) => UserSettingsDraft_Changed(sender, e);

    private void UpdateUserSettingsControls()
    {
        if (!IsInitialized || UserSettingsStatus == null) return;
        var view = userSettingsView;
        bool pending = userSettingsPending || userSettingsTickPending || userSettingsSavePending || userSettingsActionPending;
        bool draftEnabled = view != null && !userSettingsPending && !userSettingsSavePending && !userSettingsActionPending;
        UserSystemProxyBox.IsEnabled = draftEnabled;
        TransportExpander.IsEnabled = draftEnabled; BalanceExpander.IsEnabled=draftEnabled;
        UserAutoConnectBox.IsEnabled = draftEnabled;
        UserAutoConnectProfileBox.IsEnabled = draftEnabled && UserAutoConnectBox.IsChecked == true;
        UserFullDeviceBox.IsEnabled = draftEnabled;
        UserAllowTrustedBox.IsEnabled = draftEnabled;
        UserAllowPublicBox.IsEnabled = draftEnabled;
        bool staleDraft = userSettingsDraftDirty && view != null && !string.Equals(userSettingsDraftToken, view.Token, StringComparison.Ordinal);
        SaveUserSettingsButton.IsEnabled = !busy && !pending && view?.CanSave == true && userSettingsDraftDirty && !staleDraft && BalanceDraftValid();
        ReloadUserSettingsButton.IsEnabled = !pending;
        EnableUserProxyButton.IsEnabled = !busy && !pending && !userSettingsDraftDirty && view?.CanEnableProxy == true;
        DisableUserProxyButton.IsEnabled = !busy && !pending && !userSettingsDraftDirty && view?.CanDisableProxy == true;
        EnableUserStartupButton.IsEnabled = !busy && !pending && !userSettingsDraftDirty && view?.CanEnableStartup == true;
        DisableUserStartupButton.IsEnabled = !busy && !pending && !userSettingsDraftDirty && view?.CanDisableStartup == true;
        UserNetworkCategory.Text = "Сеть: " + (view?.NetworkCategory switch { "public" => "общедоступная", "private" => "частная", "domain" => "доменная", _ => "не определена" });
        UserAutoConnectInhibited.Visibility = view?.AutoConnectInhibited == true ? Visibility.Visible : Visibility.Collapsed;
        UserProxyStatus.Text = view == null ? "Системный прокси: состояние недоступно" : view.ProxyEnabled ? "Системный прокси включён для текущего пользователя" : "Системный прокси выключен";
        UserStartupStatus.Text = view == null ? "Автозапуск: состояние недоступно" : !view.StartupRecorded ? "Автозапуск выключен" : view.StartupEnabled ? "Автозапуск включён для текущего пользователя" : "Запись автозапуска отсутствует или изменена другим приложением";
        UserStartupPath.Text = Environment.ProcessPath ?? "Unavailable";
        if (staleDraft) UserSettingsStatus.Text = "Настройки изменились. Обнови сведения перед сохранением; введённые значения сохранены.";
    }

    private async void SaveUserSettings_Click(object sender, RoutedEventArgs e)
    {
        if (userSettingsView is not { CanSave: true } view || !userSettingsDraftDirty || userSettingsPending || userSettingsTickPending || userSettingsSavePending || userSettingsActionPending) return;
        string token = userSettingsDraftToken ?? view.Token;
        var preferences = new UserPreferences(
            UserSystemProxyBox.IsChecked == true,
            UserAutoConnectBox.IsChecked == true,
            UserAutoConnectProfileBox.SelectedItem is Profile selectedProfile ? selectedProfile.Id.ToString() : "",
            UserFullDeviceBox.IsChecked == true,
            UserAllowTrustedBox.IsChecked == true,
            UserAllowPublicBox.IsChecked == true) { Transport=ReadGlobalTransport(),Balance=ReadBalance() };
        int request = ++userSettingsRequestVersion;
        userSettingsSavePending = true; UpdateUserSettingsControls();
        bool persisted = false;
        try
        {
            var document = await ProfileBackend.SaveUserSettingsAsync(token, preferences);
            persisted = true;
            if (request != userSettingsRequestVersion || lifetime.IsCancellationRequested) return;
            Storage.Accept(document);
            var saved = await ProfileBackend.UserSettingsAsync(lifetime.Token);
            if (request != userSettingsRequestVersion || lifetime.IsCancellationRequested) return;
            PresentUserSettings(saved, resetDraft: true);
            UserSettingsStatus.Text = LocalizeUserNotice(saved.Message);
        }
        catch
        {
            if (request == userSettingsRequestVersion && !lifetime.IsCancellationRequested)
                UserSettingsStatus.Text = persisted ? "Preferences were saved, but current settings could not be reloaded. Refresh settings before making another change." : "Preferences were not confirmed. Refresh settings before trying again.";
        }
        finally { userSettingsSavePending = false; UpdateUserSettingsControls(); }
    }

    private async void ReloadUserSettings_Click(object sender, RoutedEventArgs e) => await LoadUserSettingsAsync(resetDraft: true);

    private async Task RunUserSettingsActionAsync(Func<string, Task<UserSettingsView>> action, string confirmation, string completed)
    {
        if (userSettingsView is not { } view || userSettingsActionPending || userSettingsPending || userSettingsTickPending || userSettingsSavePending || userSettingsDraftDirty || lifetime.IsCancellationRequested) return;
        if (MessageBox.Show(this, confirmation, "OneTunnel user settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (userSettingsView is not { } current || !string.Equals(current.Token, view.Token, StringComparison.Ordinal))
        {
            UserSettingsStatus.Text = "Настройки изменились во время подтверждения. Проверь их текущее состояние и повтори действие.";
            return;
        }
        int request = ++userSettingsRequestVersion;
        userSettingsActionPending = true; UserSettingsStatus.Text = "Применение подтверждённого изменения…"; UpdateUserSettingsControls();
        try
        {
            var updated = await action(current.Token);
            if (request != userSettingsRequestVersion || lifetime.IsCancellationRequested) return;
            PresentUserSettings(updated, resetDraft: true);
            UserSettingsStatus.Text = string.IsNullOrWhiteSpace(updated.Message) ? completed : LocalizeUserNotice(updated.Message);
        }
        catch
        {
            if (request == userSettingsRequestVersion && !lifetime.IsCancellationRequested)
                UserSettingsStatus.Text = "Не удалось подтвердить или завершить действие. Обнови сведения и повтори попытку.";
        }
        finally { userSettingsActionPending = false; UpdateUserSettingsControls(); }
    }

    private async void EnableUserProxy_Click(object sender, RoutedEventArgs e) => await RunUserSettingsActionAsync(
        ProfileBackend.EnableUserProxyAsync,
        "Enable the current-user Windows proxy using OneTunnel's confirmed local listener? OneTunnel will preserve the previous user proxy values and restore only values it still owns. Machine-wide WinHTTP settings are not changed.",
        "Proxy status updated.");

    private async void DisableUserProxy_Click(object sender, RoutedEventArgs e) => await RunUserSettingsActionAsync(
        ProfileBackend.DisableUserProxyAsync,
        "Disable OneTunnel's current-user proxy lease and restore unchanged values that OneTunnel owns? External changes will be preserved.",
        "Proxy status updated.");

    private async void EnableUserStartup_Click(object sender, RoutedEventArgs e)
    {
        string executable = Environment.ProcessPath ?? "";
        await RunUserSettingsActionAsync(
            token => ProfileBackend.EnableUserStartupAsync(token, executable),
            $"Register this application to start for the current Windows user?\n\nPath: {executable}\n\nThis does not connect automatically. Go will validate the path.",
            "Startup status updated.");
    }

    private async void DisableUserStartup_Click(object sender, RoutedEventArgs e) => await RunUserSettingsActionAsync(
        ProfileBackend.DisableUserStartupAsync,
        "Remove OneTunnel's unchanged current-user startup entry? Other applications' entries will be preserved.",
        "Startup status updated.");

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
        if (!allowClose && (!connection.Actions.CanClose || backupPending || backupApplyPending)) { e.Cancel = true; Feedback.Text = "Дождись завершения текущей операции."; return; }
        hiddenToTray = false; ReleaseRestoreHotkey();
        BackupPasswordBox.Clear(); RestorePasswordBox.Clear();
        if (backupRestorePreview is { } restorePreview) _ = ProfileBackend.CancelBackupRestoreAsync(restorePreview.Token);
        lifetime.Cancel(); preview?.Cancel();
        // The service owns the connection. Closing the UI never tears it down.
    }
}
