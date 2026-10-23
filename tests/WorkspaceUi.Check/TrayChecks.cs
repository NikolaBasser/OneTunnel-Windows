using OneTunnel;
using OneTunnel.Core;

internal static class TrayChecks
{
    public static void Run()
    {
        int opened = 0, minimized = 0, toggled = 0, recoveryOpened = 0, exited = 0;
        using var tray = new TrayShell(() => opened++, () => minimized++, () => toggled++, () => recoveryOpened++, () => exited++);
        Check(!tray.ToggleItem.Enabled && !tray.RecoverItem.Enabled && !tray.ExitItem.Enabled, "Tray commands start disabled until Go supplies a projection");
        Check(!tray.NotificationsItem.Checked && !tray.StatusItem.Enabled, "Tray notifications start off and status is read-only");
        tray.ToggleItem.PerformClick(); tray.RecoverItem.PerformClick(); tray.ExitItem.PerformClick();
        Check(toggled == 0 && recoveryOpened == 0 && exited == 0, "Unprojected tray commands cannot invoke callbacks");
        tray.OpenItem.PerformClick(); tray.MinimizeItem.PerformClick();
        Check(opened == 1 && minimized == 1, "Tray window actions forward to presentation callbacks");

        var idle = new DesktopActions(CanToggle: true, CanRecover: true, CanClose: true, ConnectCaption: "Synthetic connect");
        var disconnected = new DesktopDisplay(Status: "Synthetic disconnected");
        Check(tray.Present(disconnected, idle) == null, "Initial status does not create a notification");
        Check(tray.StatusItem.Text == disconnected.Status && tray.ToggleItem.Text == idle.ConnectCaption, "Tray captions are the unchanged Go projection");
        Check(tray.ToggleItem.Enabled == idle.CanToggle && tray.RecoverItem.Enabled == idle.CanRecover && tray.ExitItem.Enabled == idle.CanClose, "Tray availability follows Go exactly");
        tray.ToggleItem.PerformClick(); tray.RecoverItem.PerformClick(); tray.ExitItem.PerformClick();
        Check(toggled == 1 && recoveryOpened == 1 && exited == 1, "Enabled actions only forward their supplied callbacks");

        var busy = new DesktopActions(CanClose: false, ConnectCaption: "Synthetic busy");
        Check(tray.Present(new(Status: "Synthetic pending"), busy) == null, "Opt-out suppresses status notifications");
        tray.ToggleItem.PerformClick(); tray.RecoverItem.PerformClick(); tray.ExitItem.PerformClick();
        Check(toggled == 1 && recoveryOpened == 1 && exited == 1, "Go busy projection prevents command callbacks");
        tray.NotificationsItem.PerformClick();
        Check(tray.NotificationsItem.Checked && tray.Present(new(Status: "Synthetic pending"), busy) == null, "Opt-in does not replay previous status");
        var active = new DesktopActions(CanToggle: true, CanRecover: true, CanClose: true, ConnectCaption: "Synthetic disconnect");
        Check(tray.Present(new(Status: "Synthetic connected"), active) == "Synthetic connected", "A changed status returns one notification intent");
        Check(tray.Present(new(Status: "Synthetic connected"), active) == null, "Repeated projections do not duplicate notifications");
        Check(tray.ToggleItem.Text == active.ConnectCaption, "Disconnect uses the Go caption without local classification");
        var locked = new DesktopActions(CanRecover: true, CanClose: true, ConnectCaption: "Synthetic recovery");
        tray.Present(new(Status: "Synthetic protection state unconfirmed"), locked);
        Check(!tray.ToggleItem.Enabled && tray.RecoverItem.Enabled, "Go recovery policy remains independent of status wording");
        tray.NotificationsItem.PerformClick();
        Check(tray.Present(new(Status: "Synthetic unavailable"), new()) == null, "Disabled notifications still track the latest status");
        tray.NotificationsItem.PerformClick();
        Check(tray.Present(new(Status: "Synthetic unavailable"), new()) == null, "Re-enabling notifications does not replay disabled-period changes");

        tray.Dispose(); tray.Dispose();
        tray.OpenItem.PerformClick(); tray.ToggleItem.PerformClick(); tray.RecoverItem.PerformClick(); tray.ExitItem.PerformClick();
        Check(opened == 1 && toggled == 1 && recoveryOpened == 1 && exited == 1, "Disposed tray cannot invoke any callback");
        Check(tray.StatusItem.IsDisposed && tray.ExitItem.IsDisposed && tray.Present(disconnected, idle) == null, "Tray disposal releases the menu and suppresses later rendering");
        tray.Notify("Ignored after disposal"); tray.Show();
        Console.WriteLine("PASS: tray uses synthetic Go projections and fake callbacks; no connection/recovery commands or native balloons were invoked.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
