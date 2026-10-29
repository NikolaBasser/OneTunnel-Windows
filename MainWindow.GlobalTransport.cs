using System.Windows;
using OneTunnel.Core;

namespace OneTunnel;

public partial class MainWindow
{
    private void InitializeGlobalTransport()
    {
        foreach(var field in new[]{FragmentLengthBox,FragmentIntervalBox,NoiseLengthBox,NoiseDelayBox,MuxConcurrencyBox,MuxMaxConnectionsBox,MuxMinStreamsBox,XUDPConcurrencyBox})field.TextChanged+=(_,e)=>UserSettingsDraft_Changed(field,e);
        foreach(var check in new[]{FragmentCheck,NoiseCheck,MuxCheck,MuxPaddingBox}){check.Checked+=(_,e)=>UserSettingsDraft_Changed(check,e);check.Unchecked+=(_,e)=>UserSettingsDraft_Changed(check,e);}
        MuxProtocolBox.SelectionChanged+=(_,e)=>UserSettingsDraft_Changed(MuxProtocolBox,e);
        MuxUDP443Box.SelectionChanged+=(_,e)=>UserSettingsDraft_Changed(MuxUDP443Box,e);
        UpdateMuxControls();
    }
    private TransportInput ReadGlobalTransport()=>new(FragmentCheck.IsChecked==true,FragmentLengthBox.Text,FragmentIntervalBox.Text,NoiseCheck.IsChecked==true,NoiseLengthBox.Text,NoiseDelayBox.Text,MuxCheck.IsChecked==true,MuxConcurrencyBox.Text,ChoiceTag(MuxProtocolBox),MuxMaxConnectionsBox.Text,UsesMuxConnectionLimit()?MuxMinStreamsBox.Text:"0",MuxPaddingBox.IsChecked==true,XUDPConcurrencyBox.Text,ChoiceTag(MuxUDP443Box));
    private void LoadGlobalTransport(TransportInput? value)
    {
        GlobalTransportLegacyNotice.Visibility=value==null&&profiles.Any(p=>p.Transport is {} o&&(o.Fragment||o.Noise||o.Mux))?Visibility.Visible:Visibility.Collapsed;
        var v=value??new(false,"100-200","10-20",false,"50-150","10-20",false,"8", "","0","0",false,"0","");
        FragmentCheck.IsChecked=v.Fragment;FragmentLengthBox.Text=v.FragmentLength;FragmentIntervalBox.Text=v.FragmentInterval;
        NoiseCheck.IsChecked=v.Noise;NoiseLengthBox.Text=v.NoiseLength;NoiseDelayBox.Text=v.NoiseDelay;
        MuxCheck.IsChecked=v.Mux;MuxConcurrencyBox.Text=v.MuxConcurrency;MuxMaxConnectionsBox.Text=string.IsNullOrEmpty(v.MuxMaxConnections)?"0":v.MuxMaxConnections;MuxMinStreamsBox.Text=string.IsNullOrEmpty(v.MuxMinStreams)?"0":v.MuxMinStreams;MuxPaddingBox.IsChecked=v.MuxPadding;
        MuxProtocolBox.SelectedIndex=v.MuxProtocol switch{"h2mux"=>1,"smux"=>2,"yamux"=>3,_=>0};XUDPConcurrencyBox.Text=string.IsNullOrEmpty(v.XUDPConcurrency)?"0":v.XUDPConcurrency;
        MuxUDP443Box.SelectedIndex=v.UDP443Policy switch{"skip"=>1,"allow"=>2,"reject"=>3,_=>0};UpdateMuxControls();
    }
}
