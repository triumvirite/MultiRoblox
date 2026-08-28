using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MultiRoblox.Core;
using MultiRoblox.Core.Models;

namespace MultiRoblox.App.Views;

/// <summary>Opens Roblox in an isolated WebView profile seeded with the account's cookie.</summary>
public partial class BrowserWindow : Window
{
    private readonly Account _account;

    public BrowserWindow(Account account)
    {
        _account = account;
        InitializeComponent();
        Title = $"Roblox — {account.DisplayLabel}";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Per-account profile folder so multiple browser windows don't clash.
        string profile = Path.Combine(AppPaths.WebViewData, "browser", _account.Id.ToString("N"));
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        await Web.EnsureCoreWebView2Async(env);

        var cm = Web.CoreWebView2.CookieManager;
        foreach (var domain in new[] { ".roblox.com", "www.roblox.com" })
        {
            var cookie = cm.CreateCookie(".ROBLOSECURITY", _account.SecurityToken, domain, "/");
            cookie.IsSecure = true;
            cookie.IsHttpOnly = true;
            cm.AddOrUpdateCookie(cookie);
        }
        Web.CoreWebView2.Navigate("https://www.roblox.com/home");
    }
}
