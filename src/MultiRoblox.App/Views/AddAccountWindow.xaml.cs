using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MultiRoblox.Core;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Services;

namespace MultiRoblox.App.Views;

public partial class AddAccountWindow : Window
{
    private readonly AppServices _svc;
    private readonly Account? _relogin;
    private bool _adding;

    /// <param name="relogin">When set, the captured cookie replaces this account's session
    /// instead of adding a new account.</param>
    public AddAccountWindow(AppServices svc, Account? relogin = null)
    {
        _svc = svc;
        _relogin = relogin;
        InitializeComponent();
        if (_relogin is not null)
            Title = $"Re-login — {_relogin.Username}";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: AppPaths.WebViewData);
            await Web.EnsureCoreWebView2Async(env);
            Web.CoreWebView2.Navigate("https://www.roblox.com/login");
            Web.NavigationCompleted += async (_, _) => await TryCaptureCookieAsync();
            LoginStatus.Text = "Waiting for login…";
        }
        catch (Exception ex)
        {
            LoginStatus.Text = "WebView2 unavailable: " + ex.Message;
        }
    }

    private async Task TryCaptureCookieAsync()
    {
        if (_adding) return;
        try
        {
            var cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var rs = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY");
            if (rs is null || string.IsNullOrWhiteSpace(rs.Value)) return;

            _adding = true;
            LoginStatus.Text = "Cookie found — validating…";
            await AddFromTokenAsync(rs.Value, s => LoginStatus.Text = s);
        }
        catch (Exception ex)
        {
            LoginStatus.Text = "Error: " + ex.Message;
            _adding = false;
        }
    }

    private async void AddPasted_Click(object sender, RoutedEventArgs e)
    {
        string token = ExtractToken(CookieBox.Text);
        if (string.IsNullOrWhiteSpace(token))
        {
            PasteStatus.Text = "No .ROBLOSECURITY value found.";
            return;
        }
        await AddFromTokenAsync(token, s => PasteStatus.Text = s);
    }

    private async Task AddFromTokenAsync(string token, Action<string> status)
    {
        if (_relogin is null && _svc.Accounts.Accounts.Any(a => a.SecurityToken == token))
        {
            status("That account is already added.");
            return;
        }

        using var client = new RobloxClient(token);
        try
        {
            var user = await client.ValidateAsync();

            if (_relogin is not null)
            {
                if (user.Id != _relogin.UserId)
                {
                    status($"That's a different account ({user.Name}). Log in as {_relogin.Username}.");
                    _adding = false;
                    return;
                }
                _relogin.Username = user.Name;
                _relogin.DisplayName = user.DisplayName;
                _relogin.SecurityToken = client.CurrentToken;
                _svc.Accounts.Update(_relogin);
                _svc.Pool.Invalidate(_relogin.Id);
                status($"Re-logged in as {user.Name}.");
            }
            else
            {
                _svc.Accounts.Add(new Account
                {
                    Username = user.Name,
                    DisplayName = user.DisplayName,
                    UserId = user.Id,
                    SecurityToken = client.CurrentToken,
                });
                status($"Added {user.Name}.");
            }

            DialogResult = true;
            Close();
        }
        catch (RobloxAuthException)
        {
            status("That cookie is not valid (already logged out?).");
        }
        catch (Exception ex)
        {
            status("Failed: " + ex.Message);
        }
        finally
        {
            _adding = false;
        }
    }

    private static string ExtractToken(string input)
    {
        input = input.Trim();
        var m = Regex.Match(input, @"\.ROBLOSECURITY=([^;\s]+)");
        if (m.Success) return m.Groups[1].Value;
        // Otherwise assume the whole thing is the value.
        return input.Contains(' ') || input.Contains('\n') ? "" : input;
    }

    private void ResetWebView_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Web.CoreWebView2 is not null)
            {
                Web.CoreWebView2.CookieManager.DeleteAllCookies();
                Web.CoreWebView2.Navigate("https://www.roblox.com/login");
                LoginStatus.Text = "Cleared. Waiting for login…";
            }
        }
        catch { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
