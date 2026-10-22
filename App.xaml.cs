using System.Windows;
using System.Threading;
namespace OneTunnel;
public partial class App : Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        int wait = Array.IndexOf(e.Args, "--wait-for");
        if (wait >= 0 && e.Args.Length > wait + 1 && int.TryParse(e.Args[wait + 1], out int pid))
        {
            try { using var old = System.Diagnostics.Process.GetProcessById(pid); old.WaitForExit(15000); } catch (ArgumentException) { }
        }
        bool first;
        try { mutex = new Mutex(true, @"Local\OneTunnel-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, out first); }
        catch (UnauthorizedAccessException) { MessageBox.Show("Другая копия OneTunnel уже запущена с повышенными правами."); Shutdown(); return; }
        if (!first) { MessageBox.Show("OneTunnel уже запущен."); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
