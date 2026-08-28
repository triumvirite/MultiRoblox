using System.Net.Http.Json;
using System.Text.Json;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Cookie-only account actions: read profile/economy info, edit description, block/unblock users,
/// join/leave groups, sign out other sessions. No password-required operations.
/// </summary>
public sealed class AccountUtilities
{
    private readonly RobloxClient _client;
    private readonly HttpClient _http;

    public AccountUtilities(RobloxClient client)
    {
        _client = client;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MultiRoblox/1.0");
    }

    public sealed record Overview(
        long UserId, string Username, string DisplayName, string Description,
        long Robux, bool Premium, string? Birthdate, bool EmailVerified);

    public async Task<Overview> GetOverviewAsync(long userId, CancellationToken ct = default)
    {
        var auth = await _client.ValidateAsync(ct);

        string desc = "";
        try { desc = (await GetJsonAsync("https://users.roblox.com/v1/description", ct)).GetProperty("description").GetString() ?? ""; }
        catch { }

        long robux = 0;
        try { robux = (await GetJsonAsync("https://economy.roblox.com/v1/user/currency", ct)).GetProperty("robux").GetInt64(); }
        catch { }

        bool premium = false;
        try
        {
            var el = await GetJsonAsync($"https://premiumfeatures.roblox.com/v1/users/{userId}/validate-membership", ct);
            premium = el.ValueKind == JsonValueKind.True;
        }
        catch { }

        string? birthdate = null;
        try
        {
            var b = await GetJsonAsync("https://users.roblox.com/v1/birthdate", ct);
            birthdate = $"{b.GetProperty("birthYear").GetInt32():0000}-{b.GetProperty("birthMonth").GetInt32():00}-{b.GetProperty("birthDay").GetInt32():00}";
        }
        catch { }

        bool emailVerified = false;
        try
        {
            var em = await GetJsonAsync("https://accountsettings.roblox.com/v1/email", ct);
            emailVerified = em.TryGetProperty("verified", out var v) && v.GetBoolean();
        }
        catch { }

        return new Overview(auth.Id, auth.Name, auth.DisplayName, desc, robux, premium, birthdate, emailVerified);
    }

    public async Task SetDescriptionAsync(string description, CancellationToken ct = default) =>
        await PostAsync("https://users.roblox.com/v1/description", new { description }, ct);

    public async Task BlockUserAsync(long userId, CancellationToken ct = default) =>
        await PostAsync($"https://accountsettings.roblox.com/v1/users/{userId}/block", new { }, ct);

    public async Task UnblockUserAsync(long userId, CancellationToken ct = default) =>
        await PostAsync($"https://accountsettings.roblox.com/v1/users/{userId}/unblock", new { }, ct);

    public async Task JoinGroupAsync(long groupId, CancellationToken ct = default) =>
        await PostAsync($"https://groups.roblox.com/v1/groups/{groupId}/users", new { }, ct);

    public async Task LeaveGroupAsync(long groupId, long userId, CancellationToken ct = default)
    {
        string csrf = await _client.GetCsrfTokenAsync(ct: ct);
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"https://groups.roblox.com/v1/groups/{groupId}/users/{userId}");
        AddAuth(req, csrf);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    public Task LogoutOtherSessionsAsync(CancellationToken ct = default) => _client.LogoutOtherSessionsAsync(ct);

    // --- helpers -----------------------------------------------------

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuth(req, null);
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private async Task PostAsync(string url, object body, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string csrf = await _client.GetCsrfTokenAsync(force: attempt > 0, ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            AddAuth(req, csrf);
            using var res = await _http.SendAsync(req, ct);
            if ((int)res.StatusCode == 403 && attempt == 0) continue;
            res.EnsureSuccessStatusCode();
            return;
        }
    }

    private void AddAuth(HttpRequestMessage req, string? csrf)
    {
        req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={_client.CurrentToken}");
        req.Headers.Referrer = new Uri("https://www.roblox.com/");
        if (csrf is not null) req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
    }
}
