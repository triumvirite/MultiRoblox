using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MultiRoblox.App.Services;

/// <summary>System-tray presence: minimise-to-tray, restore, quit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;
    private readonly Func<bool> _onQuit;

    /// <param name="onQuit">Attempt to quit; returns false if the user cancelled (e.g. declined the
    /// "close running instances?" warning).</param>
    public TrayIcon(Window window, Func<bool> onQuit)
    {
        _window = window;
        _onQuit = onQuit;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open MultiRoblox", null, (_, _) => Restore());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _onQuit());

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
            else if (!_onQuit())
            {
                // User backed out of the "close running instances?" warning — keep the window open.
                e.Cancel = true;
            }
            // else: quitting (ShutdownApp calls Environment.Exit, so this handler won't return).
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
