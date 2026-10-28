using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    public static readonly DependencyProperty FavoriteSnapshotProperty = DependencyProperty.Register(nameof(FavoriteSnapshot), typeof(Guid[]), typeof(MainWindow), new PropertyMetadata(Array.Empty<Guid>()));
    public Guid[] FavoriteSnapshot { get => (Guid[])GetValue(FavoriteSnapshotProperty); set => SetValue(FavoriteSnapshotProperty, value); }
    public static readonly DependencyProperty ActiveProfileProperty = DependencyProperty.Register(nameof(ActiveProfile), typeof(Guid?), typeof(MainWindow));
    public Guid? ActiveProfile { get => (Guid?)GetValue(ActiveProfileProperty); set => SetValue(ActiveProfileProperty, value); }
    private bool groupedProfiles;
    private readonly DispatcherTimer feedbackTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private sealed record DetailRow(string Label, string Value);

    private void InitializeControlV2()
    {
        feedbackTimer.Tick += (_, _) => { feedbackTimer.Stop(); FeedbackNotice.Visibility = Visibility.Collapsed; };
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(Feedback, FeedbackChanged);
        SizeChanged += (_, _) => ConstrainInspector();
        InspectorPanel.SizeChanged += (_, _) => ConstrainInspector();
        Closed += (_, _) => { feedbackTimer.Stop(); DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).RemoveValueChanged(Feedback, FeedbackChanged); };
        Loaded += (_, _) => { ApplyControlLayout(visiblePanel); UpdateControlInspector(); ConstrainInspector(); };
        SetGrouping(false);
    }
    private void FeedbackChanged(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Feedback.Text) || Feedback.Text == "Готов к настройке") return;
        Log(Feedback.Text);
        FeedbackNotice.Visibility = Visibility.Visible;
        feedbackTimer.Stop(); feedbackTimer.Start();
    }
    private void ConstrainInspector()
    {
        if (InspectorPanel.ActualHeight > 0) InspectorTabs.MaxHeight = Math.Max(100, Math.Min(360, InspectorPanel.ActualHeight - 310));
    }
    private void PresentControlNavigation()
    {
        if (SidebarGroups == null) return;
        var groups = profiles.GroupBy(profile => profile.Group).Select(group => new SidebarGroup(group.Key, group.Count(), group.Key == sidebarGroup)).ToArray();
        if (sidebarGroup != null && !groups.Any(group => group.Name == sidebarGroup)) sidebarGroup = null;
        if (SidebarGroups.ItemsSource is not SidebarGroup[] previous || !previous.SequenceEqual(groups)) SidebarGroups.ItemsSource = groups;
        var favorites = Storage.FavoriteIDs.ToArray();
        if (!FavoriteSnapshot.Order().SequenceEqual(favorites.Order())) FavoriteSnapshot = favorites;
        AllServerCount.Text = profiles.Count.ToString(CultureInfo.CurrentCulture);
        FavoriteServerCount.Text = profiles.Count(profile => favorites.Contains(profile.Id)).ToString(CultureInfo.CurrentCulture);
        bool settings = IsSettingsSurface(visiblePanel);
        LibraryNavigation.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsSections.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        UserSettingsNavigationButton.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        HomeNavigationButton.Background = Brush(visiblePanel == "connection" && sidebarGroup == null && FavoritesOnlyBox.IsChecked != true ? "Selected" : "Rail");
        SidebarFavoritesButton.Background = Brush(visiblePanel == "connection" && FavoritesOnlyBox.IsChecked == true ? "Selected" : "Rail");
        if (visiblePanel == "connection") PageTitle.Text = sidebarGroup ?? (FavoritesOnlyBox.IsChecked == true ? "Избранное" : "Все серверы");
        UpdateControlInspector();
    }
    private Brush Brush(string key) => (Brush)FindResource(key);
    private static bool IsSettingsSurface(string panel) => panel is "settings" or "network" or "activity" or "engines" or "about";
    private void PresentControlSettings()
    {
        AutoConnectSettingsSection.Visibility = settingsSection == "connection" ? Visibility.Visible : Visibility.Collapsed;
        ProxySettingsSection.Visibility = settingsSection == "proxy" ? Visibility.Visible : Visibility.Collapsed;
        StartupSettingsSection.Visibility = settingsSection is "general" or "startup" ? Visibility.Visible : Visibility.Collapsed;
        GeneralSettingsSection.Visibility = settingsSection is "general" or "startup" ? Visibility.Visible : Visibility.Collapsed;
        SettingsSectionTitle.Text = settingsSection switch { "proxy" => "Системный прокси", "general" or "startup" => "Основные", _ => "Подключение" };
        if (visiblePanel == "settings") PageTitle.Text = SettingsSectionTitle.Text;
        foreach (var button in SettingsSections.Children.OfType<Button>().Where(button => button.Tag is string)) {
            string target = (string)button.Tag;
            bool active = target == (visiblePanel == "settings" ? settingsSection : visiblePanel) || target == "general" && settingsSection == "startup" && visiblePanel == "settings";
            button.Background = Brush(active ? "AccentFill" : "Rail"); button.Foreground = Brush("Ink");
        }
    }
    private void ApplyControlLayout(string panel)
    {
        bool library = panel == "connection";
        bool settings = IsSettingsSurface(panel);
        ((Grid)Content).ColumnDefinitions[0].Width = new GridLength(settings ? 216 : 208);
        Grid.SetColumnSpan(WorkspaceHost, library ? 1 : 3);
        WorkspaceHost.Margin = new Thickness(library ? 16 : 24, library ? 16 : 24, library ? 16 : 24, 0);
        InspectorPanel.Visibility = InspectorSeparator.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        SearchContainer.Visibility = LibraryMenuButton.Visibility = ImportConnectionButton.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceScroll.VerticalScrollBarVisibility = library ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        MinWidth = settings ? 816 : 1076; MinHeight = settings ? 599 : 680;
        ProfileToolsPanel.Visibility = panel is "network" or "organization" or "results" ? Visibility.Visible : Visibility.Collapsed;
        NetworkPreferencesPanel.Visibility = panel == "network" ? Visibility.Visible : Visibility.Collapsed;
        ProfileOrganizationPanel.Visibility = panel == "organization" ? Visibility.Visible : Visibility.Collapsed;
        GroupResultsPanel.Visibility = panel == "results" ? Visibility.Visible : Visibility.Collapsed;
        if (panel == "network") { NetworkPreferencesPanel.IsExpanded = true; PageTitle.Text = "DNS и маршрутизация"; }
        if (panel == "organization") { ProfileOrganizationPanel.IsExpanded = true; PageTitle.Text = "Копирование и группа профиля"; }
        if (panel == "results") { GroupResultsPanel.IsExpanded = true; PageTitle.Text = "Результаты проверки"; }
        AboutPanel.Visibility = panel == "about" ? Visibility.Visible : Visibility.Collapsed;
        if (panel == "about") { PageTitle.Text = "О приложении"; AboutVersion.Text = "Версия приложения: " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3); }
        PresentControlNavigation(); PresentControlSettings();
        WorkspaceScroll.ScrollToTop();
    }
    private void UpdateControlInspector()
    {
        if (InspectorDetailRows == null) return;
        var p = selected;
        bool selectedSession = connection.ProfileId == null || connection.ProfileId == p?.Id;
        ActiveProfile = connection.ProfileId;
        var rows = new List<DetailRow>();
        if (p != null) {
            rows.Add(new("Протокол", p.Protocol.ToString())); rows.Add(new("Группа", p.Group));
            if (p.Transport?.Mux == true) rows.Add(new("MUX", string.IsNullOrEmpty(p.Transport.MuxProtocol) ? "Xray" : p.Transport.MuxProtocol));
            if (p.Network?.DNS is { } dns) rows.Add(new("DNS", dns.Transport));
            InspectorCountry.Text = CountryName(p.Name);
        } else InspectorCountry.Text = "";
        InspectorDetailRows.ItemsSource = rows;
        InspectorStar.Fill = p != null && Storage.FavoriteIDs.Contains(p.Id) ? Brush("AccentText") : Brushes.Transparent;
        InspectorFavorite.IsEnabled = FavoriteButton.IsEnabled;
        PrimaryActionCaption.Text = !selectedSession && connection.Actions.CanDisconnect ? "Отключить активное соединение" : connection.Actions.ConnectCaption; PrimaryConnectButton.IsEnabled = connection.Actions.CanToggle;
        PrimaryActionIcon.Visibility = connection.Active ? Visibility.Visible : Visibility.Collapsed;
        FooterStatus.Text = connection.ProfileId is { } activeId && profiles.FirstOrDefault(profile => profile.Id == activeId) is { } activeProfile ? activeProfile.Name + " · " + connection.Display.Status : connection.Display.Status;
        FooterTime.Text = connection.Display.ShowStatistics ? "Время подключения: " + connection.Display.Elapsed : "";

        StatusLabel.Text = selectedSession ? connection.Display.Status : "Не подключено";
        InspectorStatusDot.Fill = connection.Display.ShowSession && selectedSession ? Brush("AccentText") : Brush("TextMuted");
        InspectorSent.Text = connection.Display.ShowStatistics && selectedSession ? connection.Display.Uploaded : "—";
        InspectorReceived.Text = connection.Display.ShowStatistics && selectedSession ? connection.Display.Downloaded : "—";
        StatisticsPanel.Visibility = connection.Display.ShowStatistics && selectedSession ? Visibility.Visible : Visibility.Collapsed;
        var row = groupResultsView is { Stale: false } results ? results.Rows.FirstOrDefault(row => row.ID == p?.Id.ToString()) : null;
        InspectorLatency.Text = row?.HTTPSLatencyMilliseconds is { } latency ? latency + " мс" : "";
    }
    private static string CountryName(string name)
    {
        var runes = name.EnumerateRunes().ToArray();
        if (runes.Length < 2 || runes[0].Value is < 0x1F1E6 or > 0x1F1FF || runes[1].Value is < 0x1F1E6 or > 0x1F1FF) return "";
        string code = new([(char)('A' + runes[0].Value - 0x1F1E6), (char)('A' + runes[1].Value - 0x1F1E6)]);
        return code switch { "NL" => "Нидерланды", "FI" => "Финляндия", "US" => "США", "DE" => "Германия", "JP" => "Япония", "SG" => "Сингапур", "FR" => "Франция", "GB" => "Великобритания", "RU" => "Россия", _ => code };
    }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
    private string LocalizeUserNotice(string message)
    {
        if (UserSettingsDetails.Text != message) { UserSettingsDetails.Text = message; if (!string.IsNullOrWhiteSpace(message)) Log(message); }
        return message.Replace("Automatic actions are off by default. Unknown networks are public. Startup registration does not enable auto-connect.", "Автоматические действия по умолчанию выключены. Неизвестные сети считаются общедоступными. Автозапуск приложения не включает подключение.")
            .Replace("Automatic connection is unavailable because its UI session could not be confirmed.", "Автоподключение недоступно: не удалось подтвердить пользовательскую сессию.")
            .Replace("Previous proxy ownership could not be reconciled. Explicitly restore it before enabling proxy changes.", "Не удалось восстановить состояние прежнего системного прокси. Сначала выполни его восстановление.")
            .Replace("External proxy changes were left unchanged; only OneTunnel-owned values were restored.", "Внешние изменения прокси сохранены; восстановлены только значения OneTunnel.")
            .Replace("External startup changes were left unchanged; recorded ownership was retained for review.", "Внешние изменения автозапуска сохранены; прежняя запись OneTunnel оставлена для проверки.");
    }
    private void PrimaryConnect_Click(object sender, RoutedEventArgs e) => ConnectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private void InspectorFavorite_Click(object sender, RoutedEventArgs e) => FavoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private void ControlSettings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: string section }) return;
        if (section is "general" or "connection" or "proxy") { settingsSection = section; if (visiblePanel == "settings") { PresentControlSettings(); WorkspaceScroll.ScrollToTop(); } else ShowPanel("settings"); }
        else ShowPanel(section);
    }
    private void SetGrouping(bool enabled)
    {
        groupedProfiles = enabled;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(profiles);
        using (view.DeferRefresh()) { view.GroupDescriptions.Clear(); if (enabled) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Profile.Group))); }
    }
    private MenuItem MenuCommand(string label, Button command)
    {
        var item = new MenuItem { Header = label };
        item.SetBinding(MenuItem.IsEnabledProperty, new Binding(nameof(Button.IsEnabled)) { Source = command });
        item.Click += (_, _) => command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return item;
    }
    private void LibraryMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var pair in new[] { ("Добавить профиль", AddProfileButton), ("Подписки", SubscriptionButton), ("Управление группами", GroupsNavigationButton), ("Проверить группу", CheckGroupButton), ("Отменить проверку", CancelGroupButton) }) menu.Items.Add(MenuCommand(pair.Item1, pair.Item2));
        var results = new MenuItem { Header = "Результаты проверки" }; results.Click += (_, _) => ShowPanel("results"); menu.Items.Add(results);
        var grouping = new MenuItem { Header = "Группировать список", IsCheckable = true, IsChecked = groupedProfiles }; grouping.Click += (_, _) => SetGrouping(grouping.IsChecked); menu.Items.Add(grouping);
        menu.Items.Add(MenuCommand("Развернуть группы", ExpandAllGroupsButton)); menu.Items.Add(MenuCommand("Свернуть группы", CollapseAllGroupsButton));
        var sort = new MenuItem { Header = "Порядок списка" };
        for (int i = 0; i < LibrarySortBox.Items.Count; i++) { int index = i; var option = new MenuItem { Header = ((ComboBoxItem)LibrarySortBox.Items[i]).Content, IsCheckable = true, IsChecked = LibrarySortBox.SelectedIndex == i }; option.Click += (_, _) => LibrarySortBox.SelectedIndex = index; sort.Items.Add(option); }
        menu.Items.Add(sort); menu.PlacementTarget = LibraryMenuButton; menu.IsOpen = true;
    }
    private void InspectorMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); foreach (var pair in new[] { ("Редактировать профиль", EditButton), ("Проверить доступ", ProbeButton), ("Удалить профиль…", DeleteButton) }) menu.Items.Add(MenuCommand(pair.Item1, pair.Item2));
        var organize = new MenuItem { Header = "Копировать или переместить", IsEnabled = selected != null }; organize.Click += (_, _) => ShowPanel("organization"); menu.Items.Add(organize);
        var network = new MenuItem { Header = "DNS и маршрутизация", IsEnabled = selected != null }; network.Click += (_, _) => ShowPanel("network"); menu.Items.Add(network);
        menu.PlacementTarget = InspectorMenuButton; menu.IsOpen = true;
    }
    private async void RowFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: Guid id }) await FavoriteProfileAsync(id);
    }
}
