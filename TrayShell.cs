using OneTunnel.Core;

namespace OneTunnel;

/// <summary>Thin notification-area presentation for the existing Go UI projection.</summary>
public sealed class TrayShell : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon icon;
    private readonly System.Windows.Forms.ContextMenuStrip menu;
    private string? lastStatus;
    private bool disposed;

    public System.Windows.Forms.ToolStripMenuItem StatusItem { get; }
    public System.Windows.Forms.ToolStripMenuItem ToggleItem { get; }
    public System.Windows.Forms.ToolStripMenuItem RecoverItem { get; }
    public System.Windows.Forms.ToolStripMenuItem NotificationsItem { get; }
    public System.Windows.Forms.ToolStripMenuItem ExitItem { get; }
    public System.Windows.Forms.ToolStripMenuItem OpenItem { get; }
    public System.Windows.Forms.ToolStripMenuItem MinimizeItem { get; }

    public TrayShell(Action open, Action minimize, Action toggle, Action recover, Action exit)
    {
        menu = new System.Windows.Forms.ContextMenuStrip();
        StatusItem = new("Проверка службы…") { Enabled = false };
        ToggleItem = new("Подключить") { Enabled = false }; ToggleItem.Click += (_, _) => { if (!disposed && ToggleItem.Enabled) toggle(); };
        RecoverItem = new("Движки и восстановление") { Enabled = false }; RecoverItem.Click += (_, _) => { if (!disposed && RecoverItem.Enabled) recover(); };
        NotificationsItem = new("Уведомлять об изменении статуса") { CheckOnClick = true };
        menu.Items.Add(StatusItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(ToggleItem); menu.Items.Add(RecoverItem);
        menu.Items.Add(NotificationsItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        OpenItem = new System.Windows.Forms.ToolStripMenuItem("Открыть OneTunnel"); OpenItem.Click += (_, _) => { if (!disposed) open(); };
        MinimizeItem = new System.Windows.Forms.ToolStripMenuItem("Свернуть"); MinimizeItem.Click += (_, _) => { if (!disposed) minimize(); };
        ExitItem = new System.Windows.Forms.ToolStripMenuItem("Выход") { Enabled = false }; ExitItem.Click += (_, _) => { if (!disposed && ExitItem.Enabled) exit(); };
        menu.Items.Add(OpenItem); menu.Items.Add(MinimizeItem); menu.Items.Add(ExitItem);
        icon = new System.Windows.Forms.NotifyIcon();
        try { icon.Icon = System.Drawing.SystemIcons.Application; icon.Text = "OneTunnel"; icon.ContextMenuStrip = menu; }
        catch { icon.Dispose(); menu.Dispose(); throw; }
        icon.DoubleClick += (_, _) => { if (!disposed) open(); };
        icon.BalloonTipClicked += (_, _) => { if (!disposed) open(); };
    }

    public void Show() { if (!disposed) icon.Visible = true; }

    public string? Present(DesktopDisplay display, DesktopActions actions)
    {
        if (disposed) return null;
        StatusItem.Text = display.Status;
        ToggleItem.Text = actions.ConnectCaption;
        ToggleItem.Enabled = actions.CanToggle;
        RecoverItem.Enabled = actions.CanRecover;
        ExitItem.Enabled = actions.CanClose;
        string? notification = lastStatus is not null && lastStatus != display.Status && NotificationsItem.Checked ? display.Status : null;
        lastStatus = display.Status;
        return notification;
    }

    public void Notify(string status)
    {
        if (disposed || !NotificationsItem.Checked) return;
        try { icon.BalloonTipTitle = "OneTunnel"; icon.BalloonTipText = status; icon.ShowBalloonTip(3000); }
        catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { icon.Visible = false; } catch { }
        try { icon.Dispose(); } catch { }
        try { menu.Dispose(); } catch { }
    }
}
