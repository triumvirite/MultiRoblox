using System.Windows;
using System.Windows.Threading;
using MultiRoblox.App.Services;
using MultiRoblox.App.ViewModels;
using Serilog;

namespace MultiRoblox.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        Services = new AppServices();
        try
        {
            Services.LoadAccounts();
        }
        catch (Exception ex)
        {
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

    public void ShutdownApp()
    {
        _tray?.Dispose();
        Services.Dispose();
        Shutdown();
    }
}
