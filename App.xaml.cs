using System.Windows;
using System.Threading;
namespace OneTunnel;
public partial class App : Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        bool first;
        try { mutex = new Mutex(true, @"Local\OneTunnel-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, out first); }
        catch (UnauthorizedAccessException) { MessageBox.Show("Другая копия OneTunnel уже запущена с повышенными правами."); Shutdown(); return; }
        if (!first) { MessageBox.Show("OneTunnel уже запущен."); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}

