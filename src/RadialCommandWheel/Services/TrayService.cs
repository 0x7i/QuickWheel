using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace RadialCommandWheel.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Action _openManagement;
    private readonly Action _showWheel;
    private readonly Action _exit;

    public TrayService(Action openManagement, Action showWheel, Action exit)
    {
        _openManagement = openManagement;
        _showWheel = showWheel;
        _exit = exit;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = LocalizationService.Get("WindowTitle"),
            Visible = true,
            ContextMenuStrip = CreateMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => DispatcherAction(openManagement);
    }

    public void RefreshLanguage()
    {
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = CreateMenu();
        oldMenu?.Dispose();
        _notifyIcon.Text = LocalizationService.Get("WindowTitle");
    }

    public void Notify(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Notify(title, message, icon));
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        var icon = _notifyIcon.Icon;
        _notifyIcon.Dispose();
        icon?.Dispose();
    }

    private static void DispatcherAction(Action action) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(action);

    private Forms.ContextMenuStrip CreateMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            RightToLeft = LocalizationService.CurrentLanguage == Core.AppLanguage.Arabic
                ? Forms.RightToLeft.Yes
                : Forms.RightToLeft.No
        };
        menu.Items.Add(LocalizationService.Get("TrayOpen"), null,
            (_, _) => DispatcherAction(_openManagement));
        menu.Items.Add(LocalizationService.Get("TrayShowWheel"), null,
            (_, _) => DispatcherAction(_showWheel));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Get("TrayExit"), null,
            (_, _) => DispatcherAction(_exit));
        return menu;
    }
}

