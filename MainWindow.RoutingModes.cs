using System.Windows;
using System.Windows.Controls;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private string routingDraftMode="none", customRoutingDraft="", presetRoutingDraft="";
    private bool presentingRouting;
    private bool syncingRoutingToggles;
    private string presetConfigDraft="";
    private string presetRoutingSummary="Пресеты не добавлены.";
    private string CustomRoutingText() => routingDraftMode=="custom" ? NetworkRulesBox.Text : customRoutingDraft;
    private string PresetRoutingText() => routingDraftMode=="preset" ? NetworkRulesBox.Text : presetRoutingDraft;
    private bool RoutingDraftChanged(NetworkPreferencesView v) => routingDraftMode!=v.RoutingMode || CustomRoutingText()!=v.CustomRulesText || PresetRoutingText()!=v.PresetRulesText || presetConfigDraft!=v.PresetConfigText;
    private void ResetRoutingDrafts()
    {
        presentingRouting=true;
        routingDraftMode="none"; customRoutingDraft=presetRoutingDraft="";
        presetRoutingSummary="Пресеты не добавлены.";
        presetConfigDraft=""; PresetCatalogList.ItemsSource=null;
        RoutingModeBox.SelectedIndex=0; NetworkRulesBox.Clear();
        presentingRouting=false; PresentProviderRoutingSource(); UpdateRoutingVisibility();
        if(routingDraftMode=="preset")LoyalRulesPanel.IsExpanded=true;
    }
    private void PresentRoutingDrafts(NetworkPreferencesView view)
    {
        presentingRouting=true;
        customRoutingDraft=view.CustomRulesText; presetRoutingDraft=view.PresetRulesText;
        presetRoutingSummary=view.PresetSummary;
        presetConfigDraft=view.PresetConfigText; PresetCatalogList.ItemsSource=view.PresetRows;
        routingDraftMode=view.RoutingMode is "preset" or "custom" ? view.RoutingMode : "none";
        RoutingModeBox.SelectedIndex=routingDraftMode switch {"preset"=>1,"custom"=>2,_=>0};
        NetworkRulesBox.Text=routingDraftMode switch {"preset"=>presetRoutingDraft,"custom"=>customRoutingDraft,_=>""};
        presentingRouting=false; PresentProviderRoutingSource(); UpdateRoutingVisibility();
        if(routingDraftMode=="preset")LoyalRulesPanel.IsExpanded=true;
    }
    private void RoutingMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || presentingRouting) return;
        customRoutingDraft=CustomRoutingText(); presetRoutingDraft=PresetRoutingText();
        routingDraftMode=ChoiceTag(RoutingModeBox);
        if(routingDraftMode=="preset")LoyalRulesPanel.IsExpanded=true;
        NetworkRulesBox.Text=routingDraftMode switch {"preset"=>presetRoutingDraft,"custom"=>customRoutingDraft,_=>""};
        UpdateRoutingVisibility(); PolicyDraftChanged(); UpdateNetworkPreferencesControls();
    }
    private void UpdateRoutingVisibility()
    {
        if (RoutingModeDescription==null || LoyalRulesPanel==null) return;
        bool preset=routingDraftMode=="preset", custom=routingDraftMode=="custom";
        syncingRoutingToggles=true;
        RoutingPresetToggle.IsChecked=preset; RoutingCustomToggle.IsChecked=custom; RoutingNoneToggle.IsChecked=!preset&&!custom;
        RoutingPresetToggle.IsEnabled=RoutingCustomToggle.IsEnabled=RoutingNoneToggle.IsEnabled=RoutingModeBox.IsEnabled;
        syncingRoutingToggles=false;
        CustomRoutingPanel.Visibility=custom?Visibility.Visible:Visibility.Collapsed;
        NetworkAllowLANBox.Visibility=routingDraftMode=="none"?Visibility.Collapsed:Visibility.Visible;
        LoyalRulesPanel.Visibility=preset ? Visibility.Visible : Visibility.Collapsed;
        RoutingRulesHelp.Visibility=custom ? Visibility.Visible : Visibility.Collapsed;
        NetworkRulesBox.Visibility=custom ? Visibility.Visible : Visibility.Collapsed;
        RoutingPresetSummary.Visibility=Visibility.Collapsed;
        RoutingPresetSummary.Text=presetRoutingSummary;
        NetworkRulesBox.IsReadOnly=!custom;
        RoutingModeDescription.Text=routingDraftMode switch {
            "preset"=>"В режиме «Всё устройство» применяются пресеты выбранного источника. Свои правила хранятся отдельно.",
            "custom"=>"В режиме «Всё устройство» применяются ваши правила, в том числе правила по программам. Сохранённые пресеты отключены и не меняются.",
            _=>"В режиме «Всё устройство» весь трафик идёт через VPN/прокси, прямой доступ к LAN выключен. Пресеты и свои правила сохраняются. Локальный прокси обслуживает только использующие его приложения."
        };
        UpdateRuleBuilderControls();
        UpdatePresetSourceVisibility();
        var paired=PresetCatalogList.Items.OfType<PresetRow>().ToArray();
        if(PresetSourceStatus!=null)PresetSourceStatus.Text=paired.Length==0?"Данные ещё не загружены.":paired.Length==3&&paired.Select(r=>r.Name).SequenceEqual(new[]{"loyal-ads","loyal-ru-sites","loyal-ru-ip"})&&paired.All(r=>!r.Pending)?"Оба файла загружены в черновик. После сохранения переподключитесь.":"Сохранены прежние пресеты. Кнопка «Обновить» заменит их указанной схемой.";
    }
    private void RoutingToggle_Changed(object sender,RoutedEventArgs e)
    {
        if(!IsInitialized || presentingRouting || syncingRoutingToggles)return;
        if(sender is CheckBox {IsChecked:true,Tag:string tag} && int.TryParse(tag,out int index)) RoutingModeBox.SelectedIndex=index;
        UpdateRoutingVisibility();
    }
}
