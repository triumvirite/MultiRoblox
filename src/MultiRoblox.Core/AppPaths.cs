namespace MultiRoblox.Core;

/// <summary>Well-known on-disk locations for the app. All under %AppData%\MultiRoblox.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiRoblox");

    public static string AccountsFile => Path.Combine(Root, "accounts.dat");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ThemesDir => Path.Combine(Root, "themes");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string WebViewData => Path.Combine(Root, "webview");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ThemesDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(WebViewData);
    }
}
