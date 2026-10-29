using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private bool presentingProviderRouting;
    private int providerSourcesVersion;
    private void RoutingSource_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(ProviderRoutingPanel==null||LoyalsoldierSourcePanel==null)return;
        bool provider=RoutingSourceBox.SelectedIndex==1;
        ProviderRoutingPanel.Visibility=provider?Visibility.Visible:Visibility.Collapsed;
        LoyalsoldierSourcePanel.Visibility=provider?Visibility.Collapsed:Visibility.Visible;
        if(provider&&!presentingProviderRouting)_=LoadImportedRoutingSourcesAsync();
    }
    private async void ProviderRoutingGroup_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(presentingProviderRouting||!PolicyContextReady()||policyToolsPending||routingDraftMode!="preset"||ProviderRoutingGroupBox.SelectedItem is not ImportedRoutingSource source)return;
        var id=selected!.Id;int version=policyToolsVersion;
        policyToolsPending=true;ProviderRoutingReview.Text="Подготавливаю правила из импортированной подписки…";
        ProviderRoutingGroupBox.IsEnabled=RoutingSourceBox.IsEnabled=false;
        UpdateNetworkPreferencesControls();
        try {
            var result=await ProfileBackend.SelectImportedRoutingAsync(source.Group,new("",0,source.SHA256));
            if(selected?.Id!=id||version!=policyToolsVersion||routingDraftMode!="preset"||RoutingSourceBox.SelectedIndex!=1||!ReferenceEquals(ProviderRoutingGroupBox.SelectedItem,source)){ProviderRoutingReview.Text="Черновик изменился. Выберите подписку заново.";return;}
            if(result.Draft is not {} draft)return;
            NetworkRulesBox.Text=draft.RulesText;presetConfigDraft=draft.PresetConfigText;presetRoutingSummary=draft.PresetSummary;PresetCatalogList.ItemsSource=draft.PresetRows;
            UpdateRoutingVisibility();PolicyDraftChanged();
            ProviderRoutingReview.Text=$"В черновике правил: {draft.Count}. Сохраните DNS и правила; изменения применятся после переподключения.\n\n"+string.Join("\n\n",result.Warnings??[]);
        }catch(Exception ex){ProviderRoutingReview.Text="Не выполнено. "+ex.Message;}
        finally{policyToolsPending=false;ProviderRoutingGroupBox.IsEnabled=RoutingSourceBox.IsEnabled=true;UpdateNetworkPreferencesControls();}
    }
    private async Task LoadImportedRoutingSourcesAsync()
    {
        if(ProviderRoutingGroupBox==null)return;
        int request=++providerSourcesVersion;var id=selected?.Id;
        try {
            var sources=await ProfileBackend.ImportedRoutingSourcesAsync();
            if(request!=providerSourcesVersion||id!=selected?.Id)return;
            string group="";
            using(var doc=JsonDocument.Parse(string.IsNullOrWhiteSpace(presetConfigDraft)?"[]":presetConfigDraft)){
                var saved=doc.RootElement.EnumerateArray().FirstOrDefault(s=>s.TryGetProperty("Format",out var f)&&f.GetString()=="provider-xray");
                if(saved.ValueKind==JsonValueKind.Object){if(saved.TryGetProperty("Group",out var g))group=g.GetString()??"";if(saved.TryGetProperty("Scope",out var scope)&&scope.GetString()=="addresses-legacy")group="";}
            }
            presentingProviderRouting=true;
            ProviderRoutingGroupBox.ItemsSource=sources;
            var source=sources.FirstOrDefault(s=>s.Group==group);ProviderRoutingGroupBox.SelectedItem=source;
            if(sources.Count==0)ProviderRoutingReview.Text="В импортированных подписках нет маршрутизации. Добавьте подписку с правилами или обновите существующую в списке серверов.";
        }catch(Exception ex){if(request==providerSourcesVersion)ProviderRoutingReview.Text="Не удалось прочитать импортированные правила. "+ex.Message;}
        finally{if(request==providerSourcesVersion)presentingProviderRouting=false;}
    }
    private void PresentProviderRoutingSource()
    {
        if(RoutingSourceBox==null)return;
        presentingProviderRouting=true;
        try {
            using var doc=JsonDocument.Parse(string.IsNullOrWhiteSpace(presetConfigDraft)?"[]":presetConfigDraft);
            var source=doc.RootElement.EnumerateArray().FirstOrDefault(s=>s.TryGetProperty("Format",out var f)&&f.GetString()=="provider-xray");
            RoutingSourceBox.SelectedIndex=source.ValueKind==JsonValueKind.Object?1:0;
            ProviderRoutingReview.Text=source.ValueKind==JsonValueKind.Object?"Сохранена маршрутизация: "+(source.TryGetProperty("Label",out var label)?label.GetString():"провайдер")+".\n\n"+(source.TryGetProperty("Warnings",out var warnings)?string.Join("\n\n",warnings.EnumerateArray().Select(w=>w.GetString())):""):"";
        }catch(JsonException){RoutingSourceBox.SelectedIndex=0;}
        finally{presentingProviderRouting=false;RoutingSource_Changed(this,null!);}
    }
}
