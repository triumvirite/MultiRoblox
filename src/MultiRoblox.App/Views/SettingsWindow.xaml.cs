using System.Diagnostics;
using System.IO;
using System.Windows;
using MultiRoblox.App.Services;
using MultiRoblox.Core;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppServices _svc;

    public SettingsWindow(AppServices svc)
    {
        _svc = svc;
        InitializeComponent();
        DataPathText.Text = AppPaths.Root;

        var s = svc.Settings.Current;
        MultiInstance.IsChecked = s.AllowMultipleInstances;
        RobloxPath.Text = s.RobloxPlayerPathOverride;
        RefreshMinutes.Text = s.CookieRefreshMinutes.ToString();
        AutoRelaunch.IsChecked = s.AutoRelaunchOnDisconnect;
        CloseToTray.IsChecked = s.CloseToTray;
        ApiEnabled.IsChecked = s.WebApiEnabled;
        ApiPort.Text = s.WebApiPort.ToString();
        ApiKey.Text = s.WebApiKey;

        FpsEnabled.IsChecked = s.FpsUnlockerEnabled;
        FpsTarget.Text = s.FpsUnlockerTarget.ToString();
        FpsRow.IsEnabled = s.FpsUnlockerEnabled;

        foreach (var t in ThemeManager.AvailableThemes()) ThemeBox.Items.Add(t);
        ThemeBox.SelectedItem = s.ThemeName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _svc.Settings.Current;
        s.AllowMultipleInstances = MultiInstance.IsChecked == true;
        s.RobloxPlayerPathOverride = RobloxPath.Text.Trim();
        s.CookieRefreshMinutes = int.TryParse(RefreshMinutes.Text, out var m) ? Math.Max(0, m) : 60;
        s.AutoRelaunchOnDisconnect = AutoRelaunch.IsChecked == true;
        s.CloseToTray = CloseToTray.IsChecked == true;
        s.WebApiEnabled = ApiEnabled.IsChecked == true;
        s.WebApiPort = int.TryParse(ApiPort.Text, out var p) ? p : 7963;
        s.WebApiKey = ApiKey.Text.Trim();
        s.FpsUnlockerEnabled = FpsEnabled.IsChecked == true;
        s.FpsUnlockerTarget = int.TryParse(FpsTarget.Text, out var f) ? Math.Max(0, f) : 0;
        s.ThemeName = ThemeBox.SelectedItem as string ?? "Dark";
        _svc.Settings.Save();

        // Apply the FPS cap now so it takes effect on the next launch even without one first.
        var exe = RobloxPlayerLocator.Resolve(s.RobloxPlayerPathOverride);
        if (exe is not null)
            FpsCap.Apply(exe, s.FpsUnlockerEnabled, s.FpsUnlockerTarget);

        _svc.KeepAlive.Start();
        _svc.ApplyMultiInstance();

        try
        {
            await _svc.ControlApi.StopAsync();
            if (s.WebApiEnabled) await _svc.ControlApi.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Web API: " + ex.Message, "MultiRoblox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void FpsEnabled_Changed(object sender, RoutedEventArgs e) =>
        FpsRow.IsEnabled = FpsEnabled.IsChecked == true;

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "MultiRoblox"); }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Root);
        OpenInExplorer(AppPaths.Root);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        OpenInExplorer(AppPaths.LogsDir);
    }
}
