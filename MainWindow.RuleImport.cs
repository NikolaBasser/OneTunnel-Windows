using System.Windows;
using Microsoft.Win32;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private async void LoyalFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Наборы geosite / geoip (*.dat)|*.dat", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ImportLoyalRulesAsync(dialog.FileName, false);
    }
    private async void LoyalDownload_Click(object sender, RoutedEventArgs e) => await ImportLoyalRulesAsync("", true);
    private async Task ImportLoyalRulesAsync(string path, bool download)
    {
        if (!PolicyContextReady() || policyToolsPending || routingDraftMode!="preset") return;
        if(ChoiceTag(LoyalFormatBox) is "geosite" or "geoip" && !PresetCategoryReady())return;
        var id = selected!.Id;
        var draft = NetworkRulesBox.Text;
        var version = policyToolsVersion;
        var request = new RuleImportInput(ChoiceTag(LoyalFormatBox), LoyalCategoryBox.Text.Trim(), LoyalNameBox.Text.Trim(), ChoiceTag(LoyalActionBox), path, download, LoyalReplaceBox.IsChecked == true || PresetCatalogList.SelectedItem is PresetRow row && row.Name==LoyalNameBox.Text.Trim());
        var input = new NetworkInput("default", "", "", "", "", draft, "", "", false){PresetConfigText=presetConfigDraft};
        policyToolsPending = true;
        LoyalStatus.Text = download ? "Загружаю набор с GitHub…" : "Читаю набор…";
        UpdateNetworkPreferencesControls();
        try
        {
            var result = await ProfileBackend.ImportRulesAsync(input, request);
            if (selected?.Id != id || policyToolsVersion != version || NetworkRulesBox.Text != draft)
            {
                LoyalStatus.Text = "Настройки изменились во время импорта. Повторите импорт для текущего черновика.";
                return;
            }
            NetworkRulesBox.Text = result.RulesText;
            presetRoutingSummary=result.PresetSummary; UpdateRoutingVisibility();
            presetConfigDraft=result.PresetConfigText; PresetCatalogList.ItemsSource=result.PresetRows;
            LoyalStatus.Text = $"В черновик добавлен набор «{request.Name}»: {result.Count:N0} записей. Сохраните настройки сети. SHA-256 файла: {result.SHA256}";
        }
        catch (Exception ex) { LoyalStatus.Text = "Импорт не выполнен. " + ex.Message; }
        finally { policyToolsPending = false; UpdateNetworkPreferencesControls(); }
    }
}
