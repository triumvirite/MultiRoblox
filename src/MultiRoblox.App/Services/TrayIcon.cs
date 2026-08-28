using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MultiRoblox.App.Services;

/// <summary>System-tray presence: minimise-to-tray, restore, quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;
    private readonly Action _onQuit;

    public TrayIcon(Window window, Action onQuit)
    {
        _window = window;
        _onQuit = onQuit;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MultiRoblox", null, (_, _) => Restore());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => onQuit());

        _icon = new Forms.NotifyIcon
        {
            Text = "MultiRoblox",
            Visible = true,
            Icon = LoadAppIcon() ?? SystemIcons.Application,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => Restore();

        _window.StateChanged += (_, _) =>
        {
            if (_window.WindowState == WindowState.Minimized && ShouldHideToTray())
                _window.Hide();
        };
        _window.Closing += (_, e) =>
        {
            if (ShouldHideToTray())
            {
                // Keep the window alive & hidden so the tray can bring it back.
                e.Cancel = true;
                _window.Hide();
            }
            else
            {
                // No close-to-tray: the X button means quit. Without this the app (ShutdownMode
                // = OnExplicitShutdown) would keep running headless and a later tray click would
                // throw "Cannot call Show after a Window has closed".
                _onQuit();
            }
        };
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            return stream is null ? null : new Icon(stream);
        }
        catch { return null; }
    }

    private static bool ShouldHideToTray() => App.Services.Settings.Current.CloseToTray;

    private void Restore()
    {
        try
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
        catch (InvalidOperationException)
        {
            // Window was already closed (shouldn't happen now that X = quit) — nothing to restore.
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
