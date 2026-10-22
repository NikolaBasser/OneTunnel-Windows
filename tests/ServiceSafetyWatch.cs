using System.Diagnostics;
using System.Text.Json;
using OneTunnel.Client;
using OneTunnel.Core;

// Test infrastructure only. Task Scheduler starts this as SYSTEM, outside the
// runner's process tree, so closing the runner cannot cancel network recovery.
internal static class ServiceSafetyWatch
{
    internal static string? DirectoryPath;
    internal static void Arm()
    {
        var path = DirectoryPath ?? throw new Exception("Independent recovery watchdog required.");
        if (!File.Exists(Path.Combine(path, "watch-ready"))) throw new Exception("Recovery watchdog unavailable.");
        var armed = Path.Combine(path, "watch-armed");
        File.WriteAllText(armed + ".new", DateTime.UtcNow.AddSeconds(75).Ticks.ToString());
        File.Move(armed + ".new", armed, true);
    }
    internal static async Task Disarm(ServiceConnection client)
    {
        await client.RefreshAsync();
        if (!client.Available || client.Active || client.NeedsRecovery || client.TrafficLocked)
            throw new Exception("Network cleanup not confirmed; recovery watchdog remains armed.");
        File.Delete(Path.Combine(DirectoryPath!, "watch-armed"));
    }
    internal static async Task Run(string directory, int pid, long birth)
    {
        if (!System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem) throw new Exception("SYSTEM watchdog required.");
        directory = Path.GetFullPath(directory);
        // A non-reparse, administrator-protected directory created by the UAC
        // launcher contains only test coordination files, never profiles/keys.
        for (var p = new DirectoryInfo(directory); p != null; p = p.Parent)
            if ((p.Attributes & FileAttributes.ReparsePoint) != 0) throw new Exception("Reparse watchdog path forbidden.");
        DirectoryPath = directory;
        File.WriteAllText(Path.Combine(directory, "watch-ready"), "ready");
        bool ParentAlive()
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == birth; }
            catch (ArgumentException) { return false; }
        }
        var limit = DateTime.UtcNow.AddMinutes(30);
        while (DateTime.UtcNow < limit)
        {
            var armed = Path.Combine(directory, "watch-armed");
            if (File.Exists(armed))
            {
                long expiry = 0;
                try { long.TryParse(File.ReadAllText(armed), out expiry); } catch (IOException) { }
                if (!ParentAlive() || DateTime.UtcNow.Ticks >= expiry) break;
            }
            else if (!ParentAlive() || File.Exists(Path.Combine(directory, "watch-finished"))) return;
            await Task.Delay(250);
        }
        File.WriteAllText(Path.Combine(directory, "watch-triggered"), ParentAlive() ? "Bounded network-test window exceeded." : "Runner process exited before cleanup was confirmed.");
        if (ParentAlive()) { using var p = Process.GetProcessById(pid); p.Kill(true); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        // Stop the known service before direct recovery to avoid racing its
        // operation lock. All cleanup is performed by the installed Go core.
        try
        {
            using var p = Process.GetProcessById((int)ServicePeer.RunningProcessId());
            p.Kill(); await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (IOException) { }
        catch (ArgumentException) { }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OneTunnel", "Core", "OneTunnel.Service.exe");
        using var recovery = Process.Start(new ProcessStartInfo(installed, "--recover-network") { UseShellExecute = false, CreateNoWindow = true })!;
        await recovery.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(50));
        if (recovery.ExitCode != 0) throw new Exception("Independent Go recovery failed; inspect protected watchdog log.");
        using var start = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), "start OneTunnelCore") { UseShellExecute = false, CreateNoWindow = true })!;
        await start.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        File.WriteAllText(Path.Combine(directory, "watch-recovered"), "Owned Go network state cleared.");
    }
}
