using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using MultiRoblox.App.Services;
using MultiRoblox.App.ViewModels;
using MultiRoblox.Core;
using Serilog;

namespace MultiRoblox.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    private TrayIcon? _tray;
    private SingleInstance? _singleInstance;

    /// <summary>
    /// Append-only startup/shutdown trace at <c>%AppData%\MultiRoblox\bootstrap.log</c>, independent
    /// of Serilog. Records which binary ran, from where, and how far startup got — invaluable when
    /// the app is launched from different environments (a plain double-click vs. a sandboxed host)
    /// that resolve <c>%AppData%</c> to different folders.
    /// </summary>
    internal static void Bootstrap(string msg)
    {
        try
        {
            var p = Path.Combine(AppPaths.Root, "bootstrap.log");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.AppendAllText(p, $"{DateTime.Now:O} pid={Environment.ProcessId} {msg}{Environment.NewLine}");
        }
        catch { /* diagnostics must never throw */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Bootstrap($"start v{ver} exe='{Environment.ProcessPath}' data='{AppPaths.Root}'");

        _singleInstance = SingleInstance.Acquire();
        if (_singleInstance is null)
        {
            Bootstrap("another instance is already running - activating it and exiting");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        MultiRoblox.App.Services.TitleBarTheme.HookGlobally();
        Services = new AppServices();
        try
        {
            Services.LoadAccounts();
            Bootstrap($"loaded {Services.Accounts.Accounts.Count} account(s)");
        }
        catch (Exception ex)
        {
            Bootstrap($"LoadAccounts failed: {ex.Message}");
            MessageBox.Show(
                "Could not open your account file. It may be corrupt, or was created under a different " +
                "Windows user.\n\n" + ex.Message, "MultiRoblox", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        ThemeManager.Apply(Services.Settings.Current.ThemeName);

        var mainVm = new MainViewModel(Services);
        var window = new MainWindow { DataContext = mainVm };
        MainWindow = window;

        _tray = new TrayIcon(window, () => ShutdownApp());

        window.Show();

        _singleInstance.ListenForActivation(() =>
        {
            try
            {
                if (MainWindow is null) return;
                MainWindow.Show();
                if (MainWindow.WindowState == WindowState.Minimized)
                    MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
                MainWindow.Topmost = true;
                MainWindow.Topmost = false;
            }
            catch (InvalidOperationException) { /* window closed */ }
        });

        Services.KeepAlive.Start();
        _ = StartWebApiAsync();
    }

    private async Task StartWebApiAsync()
    {
        try
        {
            if (Services.Settings.Current.WebApiEnabled)
                await Services.ControlApi.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Web API failed to start");
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "MultiRoblox — error", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private bool _disposed;

    public void ShutdownApp()
    {
        Cleanup();
        Shutdown();
        // Guarantee the process is really gone before any relaunch — a lingering instance would
        // still hold the single-instance lock and could race the account file.
        Serilog.Log.CloseAndFlush();
        Bootstrap("exit");
        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_disposed) return;
        _disposed = true;
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        try { Services?.Dispose(); } catch { }
    }
}
