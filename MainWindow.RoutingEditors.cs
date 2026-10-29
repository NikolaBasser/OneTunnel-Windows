using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private bool presentingPresetSource;
    private void UpdatePresetSourceVisibility(){if(PresetGeoKindBox==null||PresetCategoryHint==null)return;LoyalCategoryBox.IsReadOnly=false;PresetCategoryHint.Text=PresetGeoKindBox.SelectedIndex==1?"Например: ru или telegram. Наборы geosite и geoip можно добавить в один список.":"Например: youtube, google или category-ads-all. Добавляйте категории отдельными наборами.";}
    private NetworkInput PresetDraftInput()=>new("default","","","","",PresetRoutingText(),"","",false){PresetConfigText=presetConfigDraft};
    private RuleImportInput SelectedPresetSource()=>new(ChoiceTag(LoyalFormatBox),LoyalCategoryBox.Text.Trim(),LoyalNameBox.Text.Trim(),ChoiceTag(LoyalActionBox),"",true,PresetCatalogList.SelectedItem is PresetRow row&&row.Name==LoyalNameBox.Text.Trim());
    private void PresetSource_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!IsInitialized || presentingPresetSource)return;
        UpdatePresetSourceVisibility();
        LoyalFormatBox.SelectedIndex=PresetGeoKindBox.SelectedIndex==1?1:0;LoyalCategoryBox.Clear();
        PresetCategoryError.Visibility=Visibility.Collapsed;LoyalCategoryBox.ClearValue(Control.BorderBrushProperty);
    }
    private void PresetCatalog_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(PresetCatalogList.SelectedItem is PresetRow row){
            PresetCategoryError.Visibility=Visibility.Collapsed;LoyalCategoryBox.ClearValue(Control.BorderBrushProperty);
            presentingPresetSource=true;
            PresetGeoKindBox.SelectedIndex=row.Format=="geoip"?1:0;
            LoyalFormatBox.SelectedIndex=row.Format=="geoip"?1:0;
            LoyalCategoryBox.Text=row.Category;LoyalNameBox.Text=row.Name;LoyalActionBox.SelectedIndex=row.Action switch{"direct"=>1,"reject"=>2,_=>0};
            presentingPresetSource=false;
            UpdatePresetSourceVisibility();
        }
        UpdatePolicyTools();
    }
    private bool PresetCategoryReady()
    {
        if(!string.IsNullOrWhiteSpace(LoyalCategoryBox.Text)){PresetCategoryError.Visibility=Visibility.Collapsed;LoyalCategoryBox.ClearValue(Control.BorderBrushProperty);return true;}
        PresetCategoryError.Text=ChoiceTag(LoyalFormatBox)=="geoip"?"Укажите категорию IP-адресов, например ru или telegram.":"Укажите категорию доменов, например youtube или category-ads-all.";
        PresetCategoryError.Visibility=Visibility.Visible;LoyalCategoryBox.SetResourceReference(Control.BorderBrushProperty,"Error");LoyalCategoryBox.BringIntoView();LoyalCategoryBox.Focus();return false;
    }
    private async void ConfigurePreset_Click(object sender,RoutedEventArgs e){if(PresetCategoryReady())await RunPresetCatalogAsync("configure");}
    private async void RemovePreset_Click(object sender,RoutedEventArgs e)=>await RunPresetCatalogAsync("remove");
    private async void RefreshPresets_Click(object sender,RoutedEventArgs e)=>await RunPresetCatalogAsync("recommended");
    private async Task RunPresetCatalogAsync(string operation)
    {
        if(!PolicyContextReady()||policyToolsPending||routingDraftMode!="preset")return;
        var id=selected!.Id;int version=policyToolsVersion;var input=PresetDraftInput();var request=SelectedPresetSource();
        if(operation=="remove"&&PresetCatalogList.SelectedItem is PresetRow remove)request=request with{Name=remove.Name};
        policyToolsPending=true;LoyalStatus.Text=operation is "refresh" or "recommended"?"Обновляю выбранные наборы с GitHub…":"Изменяю список пресетов…";UpdateNetworkPreferencesControls();
        try{
            var result=operation=="recommended"?await ProfileBackend.RecommendedPresetsAsync():operation=="refresh"?await ProfileBackend.RefreshPresetsAsync(input):await ProfileBackend.ConfigurePresetAsync(input,request,operation=="remove");
            if(selected?.Id!=id||version!=policyToolsVersion||routingDraftMode!="preset"){LoyalStatus.Text="Настройки изменились. Повторите действие для текущего черновика.";return;}
            NetworkRulesBox.Text=result.RulesText;presetConfigDraft=result.PresetConfigText;presetRoutingSummary=result.PresetSummary;PresetCatalogList.ItemsSource=result.PresetRows;UpdateRoutingVisibility();PolicyDraftChanged();
            LoyalStatus.Text=operation is "refresh" or "recommended"?$"geosite + geoip обновлены: {result.Count:N0} записей. Реклама блокируется, RU — напрямую, остальное — через VPN. Сохраните настройки.":operation=="remove"?"Набор убран из черновика. Сохраните настройки.":"Список изменён. Нажмите «Обновить», чтобы загрузить выбранные наборы.";
            if(operation=="configure"){PresetCatalogList.SelectedItem=null;LoyalNameBox.Clear();}
        }catch(Exception ex){LoyalStatus.Text="Не выполнено. "+ex.Message;}
        finally{policyToolsPending=false;UpdateNetworkPreferencesControls();}
    }
    private void RuleBuilder_Changed(object sender,SelectionChangedEventArgs e){if(IsInitialized)UpdateRuleBuilderControls();}
    private void UpdateRuleBuilderControls()
    {
        if(RuleValueHint==null||RulePortBox==null)return;
        string kind=ChoiceTag(RuleKindBox);
        RuleValueHint.Text=kind switch{"domain"=>"Например: login.example.com","domain-suffix"=>"Например: example.com — домен и все его поддомены","cidr"=>"Например: 192.0.2.0/24 или 2001:db8::/32","process"=>"Полный путь к .exe. Можно выбрать программу через проводник.","port"=>"Например: 443 или 8000-9000","network"=>"Введите tcp или udp","rule-set"=>"Имя набора из расширенного редактора.",_=>""};
        RuleExecutableButton.Visibility=kind=="process"?Visibility.Visible:Visibility.Collapsed;
        RulePortBox.IsEnabled=kind!="port";RuleNetworkBox.IsEnabled=kind!="network";RuleProcessBox.IsEnabled=RuleConditionExecutableButton.IsEnabled=kind!="process";RuleOutboundBox.IsEnabled=ChoiceTag(RuleActionBox)=="proxy";
    }
    private string? PickRuleExecutable()
    {
        var dialog=new OpenFileDialog{Filter="Программы Windows (*.exe)|*.exe",CheckFileExists=true,Multiselect=false,Title="Выбрать программу для маршрутизации"};
        return dialog.ShowDialog(this)==true?dialog.FileName:null;
    }
    private void ChooseRuleExecutable_Click(object sender,RoutedEventArgs e){if(PickRuleExecutable() is {} path)ApplyRuleExecutable(path,false);}
    private void ChooseRuleConditionExecutable_Click(object sender,RoutedEventArgs e){if(PickRuleExecutable() is {} path)ApplyRuleExecutable(path,true);}
    private void ApplyRuleExecutable(string path,bool condition){if(condition)RuleProcessBox.Text=path;else RuleValueBox.Text=path;}
    private async void AddCustomRule_Click(object sender,RoutedEventArgs e)=>await AppendCustomRuleAsync();
    private async Task AppendCustomRuleAsync()
    {
        if(!PolicyContextReady()||policyToolsPending||routingDraftMode!="custom")return;
        string kind=ChoiceTag(RuleKindBox),action=ChoiceTag(RuleActionBox);
        var rule=new NetworkRule(kind,RuleValueBox.Text.Trim(),action){Port=kind=="port"?null:RulePortBox.Text.Trim(),Network=kind=="network"?null:ChoiceTag(RuleNetworkBox),Process=kind=="process"?null:RuleProcessBox.Text.Trim(),Outbound=action=="proxy"?RuleOutboundBox.Text.Trim():null};
        var id=selected!.Id;int version=policyToolsVersion;string draft=NetworkRulesBox.Text;policyToolsPending=true;UpdateNetworkPreferencesControls();
        try{var result=await ProfileBackend.AppendRuleAsync(new("default","","","","",draft,"","",false),rule);
            if(selected?.Id!=id||version!=policyToolsVersion||routingDraftMode!="custom"||NetworkRulesBox.Text!=draft)return;
            NetworkRulesBox.Text=result.RulesText;RuleBuilderStatus.Text="Правило добавлено в конец. Сохраните настройки; первым применяется первое совпадение.";
        }catch(Exception ex){RuleBuilderStatus.Text=ex.Message;}
        finally{policyToolsPending=false;UpdateNetworkPreferencesControls();}
    }
}
