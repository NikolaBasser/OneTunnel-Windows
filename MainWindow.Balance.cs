using System.Windows;
using System.Windows.Controls;
using OneTunnel.Core;

namespace OneTunnel;
public partial class MainWindow
{
    private sealed class BalanceRow {public string Id{get;init;}="";public string Label{get;init;}="";public bool Selected{get;set;}}
    private BalanceInput ReadBalance()=>new(BalanceEnabledBox.IsChecked==true,BalanceModeBox.SelectedIndex==1?"balanced":"best",string.Join(",",BalanceProfilesList.Items.OfType<BalanceRow>().Where(p=>p.Selected).Select(p=>p.Id)),ReadBalanceInt(BalanceBestBox),ReadBalanceInt(BalanceIntervalBox),ReadBalanceInt(BalanceMaxRTTBox),BalanceSelectionBox.SelectedIndex==1);
    private static int ReadBalanceInt(TextBox box)=>int.TryParse(box.Text,out int value)?value:0;
    private bool BalanceDraftValid(){if(BalanceEnabledBox.IsChecked!=true)return true;var b=ReadBalance();int count=b.ProfileIDs.Split(',',StringSplitOptions.RemoveEmptyEntries).Length;return (b.Manual?count>=2&&count<=8&&b.Best>=1&&b.Best<=count:profiles.Count>0)&&b.Interval>=15&&b.Interval<=300&&b.MaxRTT>=100&&b.MaxRTT<=10000;}
    private void LoadBalance(BalanceInput? value)
    {
        var b=value??new(false,"balanced","",2,30,1500);var selectedIDs=b.ProfileIDs.Split(',',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        BalanceSelectionBox.SelectedIndex=b.Manual?1:0;BalanceEnabledBox.IsChecked=b.Enabled;BalanceModeBox.SelectedIndex=b.Mode=="balanced"?1:0;BalanceBestBox.Text=(b.Best==0?2:b.Best).ToString();BalanceIntervalBox.Text=(b.Interval==0?30:b.Interval).ToString();BalanceMaxRTTBox.Text=(b.MaxRTT==0?1500:b.MaxRTT).ToString();
        BalanceProfilesList.ItemsSource=profiles.Select(p=>new BalanceRow{Id=p.Id.ToString(),Label=$"{p.Name} · {p.Group} · {p.Protocol}",Selected=selectedIDs.Contains(p.Id.ToString())}).ToList();UpdateBalanceSummary();
    }
    private void UpdateBalanceSummary()
    {
        if(BalanceSelectionStatus==null||BalanceProfilesList==null||BalanceSelectionBox==null||BalanceBestPanel==null)return;
        bool manual=BalanceSelectionBox.SelectedIndex==1;
        BalanceProfilesList.Visibility=BalanceBestPanel.Visibility=manual?Visibility.Visible:Visibility.Collapsed;
        int count=BalanceProfilesList.Items.OfType<BalanceRow>().Count(p=>p.Selected);
        BalanceSelectionStatus.Text=manual?$"Выбрано серверов: {count}/8.":$"Будут проверены все серверы библиотеки ({profiles.Count}). Подбор занимает 20 секунд при каждом подключении; недоступные и непроверенные серверы исключаются. Если подходящих меньше 8 — используются доступные.";
        if(!BalanceDraftValid())BalanceSelectionStatus.Text+=" Проверьте состав пула, период 15–300 с и задержку 100–10000 мс.";
    }
    private void BalanceDraft_Changed(object sender,RoutedEventArgs e){if(!IsInitialized||BalanceProfilesList==null)return;UpdateBalanceSummary();UserSettingsDraft_Changed(sender,e);}
    private void BalanceMember_Changed(object sender,RoutedEventArgs e){if(!IsInitialized)return;Dispatcher.BeginInvoke(()=>{UpdateBalanceSummary();UserSettingsDraft_Changed(sender,e);});}
}
