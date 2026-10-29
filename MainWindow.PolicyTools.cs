using System.Globalization;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private bool policyToolsPending, policySnapshotStale;
    private int policyToolsVersion;
    private string policyMessage = "Выберите проверку. Профиль и соединение не изменяются.";
    private string? policyResultToken;

    private void InitializePolicyTools()
    {
        foreach (var field in new[] { NetworkServerIPBox, NetworkServerNameBox, NetworkPortBox, NetworkPathBox, NetworkRulesBox, NetworkResolversBox, NetworkDNSRulesBox, NetworkCacheCapacityBox, PolicyAddressBox, PolicyPortBox, PolicyIPBox, PolicyProcessBox })
            field.TextChanged += (_, _) => PolicyDraftChanged();
        foreach (var field in new[] { NetworkDNSModeBox, NetworkCacheModeBox, PolicyTransportBox })
            field.SelectionChanged += (_, _) => PolicyDraftChanged();
        foreach (var field in new[] { NetworkAllowLANBox, NetworkFakeIPBox }) {
            field.Checked += (_, _) => PolicyDraftChanged();
            field.Unchecked += (_, _) => PolicyDraftChanged();
        }
        UpdatePolicyTools();
    }
    private bool NetworkPolicyDraftChanged()
    {
        if (networkPreferencesView is not { } v) return false;
        string cacheMode = v.CacheMode is "disabled" or "bounded" ? v.CacheMode : "default";
        return RoutingDraftChanged(v) || GetNetworkDNSMode() != v.Mode || NetworkServerIPBox.Text != v.ServerIP || NetworkServerNameBox.Text != v.ServerName || NetworkPortBox.Text != v.Port || NetworkPathBox.Text != v.Path || NetworkResolversBox.Text != v.ResolversText || NetworkDNSRulesBox.Text != v.DNSRulesText || (NetworkAllowLANBox.IsChecked == true) != v.AllowLAN || (NetworkFakeIPBox.IsChecked == true) != v.FakeIP || ChoiceTag(NetworkCacheModeBox) != cacheMode || NetworkCacheCapacityBox.Text != v.CacheCapacity;
    }
    private void ResetPolicyResults(string message)
    {
        policyToolsVersion++;
        policyResultToken = null;
        policyMessage = message;
        PolicyAnalysisResult.Visibility = PolicyRouteResult.Visibility = Visibility.Collapsed;
        PolicyAnalysisResult.Text = PolicyDecision.Text = PolicyExplanation.Text = PolicyOutbound.Text = PolicyTechnicalDetails.Text = "";
        foreach (var field in new[] { PolicyAddressBox, PolicyPortBox, PolicyIPBox }) field.ClearValue(Control.BorderBrushProperty);
        foreach (var error in new[] { PolicyAddressError, PolicyPortError, PolicyIPError }) { error.Text = ""; error.Visibility = Visibility.Collapsed; }
    }
    private void PolicyDraftChanged()
    {
        ResetPolicyResults("Данные изменены. Выполните проверку заново.");
        UpdatePolicyTools();
    }
    private void PolicyPreferencesRefreshed()
    {
        policySnapshotStale = false;
        ResetPolicyResults("Выберите проверку. Профиль и соединение не изменяются.");
    }
    private bool PolicyContextReady() => selected != null && networkPreferencesView is { Supported: true } v && v.ID == selected.Id.ToString() && !networkPreferencesPending && !networkPreferencesSavePending && !busy;
    private bool NativePolicyUnavailable() => selected is { Protocol: Protocol.WireGuard or Protocol.AmneziaWG, Network: null };
    private void UpdatePolicyTools()
    {
        if (PolicyToolsPanel == null) return;
        var v = networkPreferencesView;
        bool matching = selected != null && v?.ID == selected.Id.ToString();
        bool dirty = matching && NetworkPolicyDraftChanged();
        if (policyResultToken != null && (!matching || v!.Token != policyResultToken || dirty || networkPreferencesPending || networkPreferencesSavePending))
            ResetPolicyResults("Результат больше не актуален. Выполните проверку заново.");
        bool ready = PolicyContextReady() && !dirty && !policySnapshotStale && !policyToolsPending;
        if (LoyalFileButton != null) LoyalFileButton.IsEnabled = LoyalDownloadButton.IsEnabled = PolicyContextReady() && !policyToolsPending && routingDraftMode=="preset";
        if(RefreshPresetsButton!=null){bool enabled=PolicyContextReady()&&!policyToolsPending&&routingDraftMode=="preset"; RefreshPresetsButton.IsEnabled=ConfigurePresetButton.IsEnabled=enabled;RemovePresetButton.IsEnabled=enabled&&PresetCatalogList.SelectedItem!=null;}
        if(AddCustomRuleButton!=null)AddCustomRuleButton.IsEnabled=PolicyContextReady()&&!policyToolsPending&&routingDraftMode=="custom";
        if(routingDraftMode=="preset" && PresetCatalogList.Items.OfType<PresetRow>().Any(row=>row.Pending))SaveNetworkPreferencesButton.IsEnabled=false;
        PolicyContext.Text = selected == null ? "Профиль не выбран" : $"Профиль: {selected.Name}";
        PolicySnapshotLabel.Visibility = matching ? Visibility.Visible : Visibility.Collapsed;
        PolicyStatus.Text = !matching ? "Выберите профиль и загрузите его настройки сети." : !v!.Supported ? "Проверка политики недоступна для этого профиля." : dirty ? "Сохраните изменения или отмените их для проверки. Черновик не проверяется." : policySnapshotStale ? "Сохранённая политика изменилась или не подтверждена. Обновите сведения перед проверкой." : networkPreferencesPending || networkPreferencesSavePending ? "Обновление настроек…" : policyToolsPending ? "Выполняется проверка сохранённой политики…" : policyMessage;
        CheckPolicyDuplicatesButton.IsEnabled = ready;
        ExplainPolicyButton.IsEnabled = ready && !NativePolicyUnavailable() && !string.IsNullOrWhiteSpace(PolicyAddressBox.Text);
        PolicyNativeHint.Visibility = matching && NativePolicyUnavailable() ? Visibility.Visible : Visibility.Collapsed;
        bool queryEnabled = matching && v!.Supported && !NativePolicyUnavailable();
        PolicyAddressBox.IsEnabled = PolicyPortBox.IsEnabled = PolicyTransportBox.IsEnabled = PolicyProcessBox.IsEnabled = queryEnabled;
        PolicyIPBox.IsEnabled = queryEnabled && !IPAddress.TryParse(PolicyAddressBox.Text.Trim(), out _);
    }
    private void PolicyInputError(TextBox field, TextBlock error, string message)
    {
        field.SetResourceReference(Control.BorderBrushProperty, "Error");
        error.Text = message; error.Visibility = Visibility.Visible;
        if (field == PolicyIPBox) PolicyAdditionalDataPanel.IsExpanded = true;
        field.BringIntoView(); field.Focus();
        policyMessage = "Проверьте отмеченное поле.";
        UpdatePolicyTools();
    }
    private async void CheckPolicyDuplicates_Click(object sender, RoutedEventArgs e) => await RunPolicyToolsAsync(false);
    private async void ExplainPolicy_Click(object sender, RoutedEventArgs e) => await RunPolicyToolsAsync(true);
    private async Task RunPolicyToolsAsync(bool explain)
    {
        if (!PolicyContextReady() || NetworkPolicyDraftChanged() || policyToolsPending || policySnapshotStale || selected == null) return;
        feedbackTimer.Stop(); FeedbackNotice.Visibility = Visibility.Collapsed;
        ResetPolicyResults("Выберите проверку.");
        RouteQuery? query = null;
        if (explain) {
            if (NativePolicyUnavailable()) { policyMessage = "Исходные AllowedIPs этой проверкой не анализируются."; UpdatePolicyTools(); return; }
            string address = PolicyAddressBox.Text.Trim(), ip = PolicyIPBox.Text.Trim();
            ushort port = 0;
            if (address.Length == 0 || address.Contains("://") || address.Contains('/')) { PolicyInputError(PolicyAddressBox, PolicyAddressError, "Укажите домен или IP-адрес без URL и пути."); return; }
            if (PolicyPortBox.Text.Length > 0 && (!ushort.TryParse(PolicyPortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port == 0)) { PolicyInputError(PolicyPortBox, PolicyPortError, "Целое число 1–65535, либо пустое поле."); return; }
            bool literal = IPAddress.TryParse(address, out _);
            if (!literal && ip.Length > 0 && !IPAddress.TryParse(ip, out _)) { PolicyInputError(PolicyIPBox, PolicyIPError, "Неверный формат IP-адреса."); return; }
            query = new(literal ? "" : address, literal ? address : ip, PolicyProcessBox.Text.Trim(), ChoiceTag(PolicyTransportBox), port);
        }
        var id = selected.Id;
        string token = networkPreferencesView!.Token;
        int version = policyToolsVersion;
        policyToolsPending = true;
        UpdateNetworkPreferencesControls();
        bool confirming = false;
        try {
            PolicyAnalysis? analysis = null;
            RoutePrediction? prediction = null;
            if (explain) prediction = await ProfileBackend.ExplainRouteAsync(id, query!);
            else analysis = await ProfileBackend.AnalyzePolicyAsync(id);
            if (version != policyToolsVersion || selected?.Id != id || lifetime.IsCancellationRequested) return;
            // API replies have no snapshot token. Confirm the saved profile after
            // the operation without applying this view over the user's draft.
            confirming = true;
            var fresh = await ProfileBackend.NetworkPreferencesAsync(id);
            if (version != policyToolsVersion || selected?.Id != id || lifetime.IsCancellationRequested) return;
            if (fresh.ID != id.ToString() || fresh.Token != token || networkPreferencesView?.Token != token) {
                policySnapshotStale = true;
                ResetPolicyResults("Сохранённая политика изменилась. Обновите сведения.");
                return;
            }
            if (analysis != null) {
                var text = new StringBuilder($"Правил: {analysis.Rules} · Доп. серверов: {analysis.Targets} · Наборов: {analysis.RuleSets}\n");
                if (analysis.Warnings.Count == 0) text.Append("Правила с одинаковыми условиями не найдены.");
                foreach (var warning in analysis.Warnings) text.AppendLine(warning.Code == "shadowed-identical-match" ? $"Правило №{warning.Rule + 1}: такие же условия уже встречаются выше. Первым применяется более раннее правило." : $"Правило №{warning.Rule + 1}: замечание ядра ({warning.Code}).");
                PolicyAnalysisResult.Text = text.ToString();
                PolicyAnalysisResult.Visibility = Visibility.Visible;
            }
            if (prediction != null) {
                if (!prediction.PredictionOnly) throw new InvalidDataException();
                PolicyDecision.Text = prediction.Decision switch { "proxy" => "Через прокси", "direct" => "Напрямую", "reject" => "Блокировка", "dns" => "Через настроенный DNS-резолвер", "unknown" => "Недостаточно данных для прогноза", _ => "Неизвестный результат прогноза" };
                PolicyExplanation.Text = prediction.Reason switch {
                    "first-matching-rule" => prediction.Rule >= 0 ? $"Правило №{prediction.Rule + 1} · первое совпадение" : "Первое совпадение сохранённой политики",
                    "default-policy" => "Действие по умолчанию",
                    "explicit-lan-policy" => "Правило доступа к локальной сети",
                    "tun-dns-hijack" => "Обработка DNS в туннеле",
                    "required-metadata-missing" => (prediction.Rule >= 0 ? $"Для правила №{prediction.Rule + 1} не хватает данных. " : "") + "Укажите IP-адрес, порт, протокол или процесс, если они известны.",
                    _ => "Ядро вернуло дополнительное основание; см. технические подробности."
                };
                PolicyOutbound.Text = prediction.Decision == "proxy" && prediction.Outbound != "proxy" ? "Сервер: " + prediction.Outbound : "";
                PolicyTechnicalDetails.Text = $"Decision: {prediction.Decision}\nOutbound: {prediction.Outbound}\nReason: {prediction.Reason}\nPredictionOnly: true";
                PolicyRouteResult.Visibility = Visibility.Visible;
            }
            policyResultToken = token;
            policyMessage = "Проверка завершена по сохранённой политике. Соединение не проверялось.";
        }
        catch {
            if (version == policyToolsVersion && !lifetime.IsCancellationRequested) {
                if (confirming) policySnapshotStale = true;
                ResetPolicyResults(confirming ? "Не удалось подтвердить сохранённую политику. Обновите сведения." : "Не удалось выполнить проверку. Проверьте введённые данные и повторите попытку.");
            }
        }
        finally {
            policyToolsPending = false;
            if (!lifetime.IsCancellationRequested) UpdateNetworkPreferencesControls();
        }
    }
}
