using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find,
            (_, e) => { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; },
            (_, e) => { e.CanExecute = visiblePanel == "connection" && SearchBox.IsVisible && SearchBox.IsEnabled; e.Handled = true; }));
        feedbackTimer.Tick += (_, _) => { feedbackTimer.Stop(); FeedbackNotice.Visibility = Visibility.Collapsed; };
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(Feedback, FeedbackChanged);
        SizeChanged += (_, _) => ConstrainInspector();
        InspectorPanel.SizeChanged += (_, _) => ConstrainInspector();
        Closed += (_, _) => { feedbackTimer.Stop(); DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).RemoveValueChanged(Feedback, FeedbackChanged); };
        Loaded += (_, _) => { ApplyControlLayout(visiblePanel); UpdateControlInspector(); ConstrainInspector(); };
        SetGrouping(true);
        InitializePolicyTools();
        InitializeGlobalTransport();
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
        WorkspaceHeader.Visibility = panel == "network" ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceHeader.ColumnDefinitions[1].Width = new GridLength(library ? 244 : 0);
        WorkspaceHeader.ColumnDefinitions[2].Width = WorkspaceHeader.ColumnDefinitions[3].Width = new GridLength(library ? 44 : 0);
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
        if (panel == "network") { PageTitle.Text = "DNS и маршрутизация"; ResetPolicyResults("Выберите проверку. Профиль и соединение не изменяются."); }
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
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;
        SearchBox.Clear(); e.Handled = true;
    }
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
        if (sender is not Button { Tag: string section }) return;
        if (section is "general" or "connection" or "proxy") { settingsSection = section; if (visiblePanel == "settings") { PresentControlSettings(); WorkspaceScroll.ScrollToTop(); } else ShowPanel("settings"); }
        else ShowPanel(section);
    }
    private void SetGrouping(bool enabled)
    {
        groupedProfiles = enabled;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(profiles);
        using (view.DeferRefresh()) { view.GroupDescriptions.Clear(); if (enabled) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Profile.Group))); }
    }
    private static IEnumerable<DependencyObject> GroupVisuals(DependencyObject root)
    {
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in GroupVisuals(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    private static T? FindParent<T>(DependencyObject child) where T:DependencyObject
    {
        for(var p=VisualTreeHelper.GetParent(child);p!=null;p=VisualTreeHelper.GetParent(p))if(p is T found)return found;
        return null;
    }
    private static T? GroupPart<T>(Expander exp,string name) where T:FrameworkElement => exp.Header is DependencyObject root?GroupVisuals(root).OfType<T>().FirstOrDefault(e=>e.Name==name):null;
    private void UpdateProfileGroupHeaders()
    {
        if(ProfilesList==null)return;
        foreach(var exp in GroupVisuals(ProfilesList).OfType<Expander>().Where(e=>e.DataContext is CollectionViewGroup))UpdateProfileGroupHeader(exp);
    }
    private void UpdateProfileGroupHeader(Expander exp)
    {
        if(exp.DataContext is not CollectionViewGroup view)return;
        string name=view.Name.ToString()!;
        var group=groupsLoaded?groupSummaries.FirstOrDefault(g=>g.Name==name):null;
        if(GroupPart<TextBlock>(exp,"GroupCount") is {} count){count.Text=group!=null&&group.ProfileCount!=view.ItemCount?$"{view.ItemCount} из {group.ProfileCount}":view.ItemCount.ToString();count.ToolTip=$"Серверов в группе: {group?.ProfileCount??view.ItemCount}";}
        if(GroupPart<Button>(exp,"GroupRefreshAction") is {} refresh){refresh.IsEnabled=CanMutateGroups&&group?.IsSubscription==true&&subscriptionDownload==null&&pendingSubscriptionPreview==null;refresh.ToolTip=group?.IsSubscription==false?"Обновление доступно только для подписок":"Обновить подписку с просмотром изменений";}
        if(GroupPart<Button>(exp,"GroupDeleteAction") is {} delete)delete.IsEnabled=CanMutateGroups&&group!=null;
    }
    private void ProfileGroupCollapse_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is DependencyObject node&&FindParent<Expander>(node) is {} exp)exp.IsExpanded=!exp.IsExpanded;
    }
    private async void ProfileGroupRefresh_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is Button {Tag:string name})await RefreshProfileGroupSubscriptionAsync(name);
    }
    private async void ProfileGroupDelete_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(!CanMutateGroups||sender is not Button {Tag:string name} button||FindParent<Expander>(button) is not {} exp)return;
        var panel=GroupPart<Border>(exp,"GroupDeleteConfirmation");
        if(panel==null)return;
        if(panel.Visibility==Visibility.Visible){panel.Visibility=Visibility.Collapsed;return;}
        button.IsEnabled=false;
        try{
            var groups=await ProfileBackend.GroupsAsync();
            if(!exp.IsLoaded||!CanMutateGroups||groups.FirstOrDefault(g=>g.Name==name) is not {} group)return;
            panel.Tag=group;
            GroupPart<TextBlock>(exp,"GroupDeleteTitle")!.Text=$"Удалить группу «{group.Name}»?";
            GroupPart<TextBlock>(exp,"GroupDeleteDescription")!.Text=$"Профилей для удаления: {group.ProfileCount}. "+(group.IsSubscription?"Источник подписки тоже будет удалён. ":"")+"Это действие нельзя отменить.";
            GroupPart<TextBlock>(exp,"GroupDeleteError")!.Text="";
            panel.Visibility=Visibility.Visible;panel.BringIntoView();GroupPart<Button>(exp,"GroupDeleteCancel")!.Focus();
        }catch{Feedback.Text="Не удалось загрузить сведения группы. Повтори попытку.";}
        finally{UpdateProfileGroupHeaders();}
    }
    private void ProfileGroupDeleteCancel_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(sender is DependencyObject node&&FindParent<Expander>(node) is {} exp){GroupPart<Border>(exp,"GroupDeleteConfirmation")!.Visibility=Visibility.Collapsed;GroupPart<Button>(exp,"GroupDeleteAction")!.Focus();}
    }
    private void ProfileGroupDelete_KeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){ProfileGroupDeleteCancel_Click(sender,e);e.Handled=true;}
    }
    private async void ProfileGroupDeleteConfirm_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(!CanMutateGroups||sender is not DependencyObject node||FindParent<Expander>(node) is not {} exp||GroupPart<Border>(exp,"GroupDeleteConfirmation")?.Tag is not GroupSummary group)return;
        await DeleteSelectedGroupAsync(group);
        if(exp.IsLoaded&&profiles.Any(p=>p.Group==group.Name))GroupPart<TextBlock>(exp,"GroupDeleteError")!.Text="Не удалось подтвердить удаление. Отмени и заново открой подтверждение для актуального списка.";
    }
    private async Task RefreshProfileGroupSubscriptionAsync(string name)
    {
        if(!CanMutateGroups || pendingSubscriptionPreview!=null || subscriptionDownload!=null)return;
        ShowPanel("subscriptions");
        await LoadSubscriptionsAsync();
        if(subscriptions.FirstOrDefault(s=>s.Group==name) is not {CanRefresh:true} item)return;
        SubscriptionGroupsList.SelectedItem=item;
        await RefreshSubscriptionAsync(item);
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
