using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Thin wrapper over the official Roblox web endpoints for one account. Owns a cookie container so we
/// can observe rotated <c>.ROBLOSECURITY</c> values. Not tied to any UI.
/// </summary>
public sealed class RobloxClient : IDisposable
{
    private const string RoblosecurityName = ".ROBLOSECURITY";
    private static readonly Uri RobloxRoot = new("https://www.roblox.com");

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies;
    private string? _csrf;

    /// <summary>Raised when Roblox hands back a new cookie value; caller should persist it.</summary>
    public event EventHandler<string>? CookieRotated;

    public RobloxClient(string securityToken)
    {
        _cookies = new CookieContainer();
        SetToken(securityToken);

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MultiRoblox/1.0");
    }

    public string CurrentToken =>
        _cookies.GetCookies(RobloxRoot)[RoblosecurityName]?.Value ?? "";

    private void SetToken(string token)
    {
        _cookies.Add(new Cookie(RoblosecurityName, token, "/", ".roblox.com") { HttpOnly = true, Secure = true });
    }

    // --- CSRF ---------------------------------------------------------------

    public async Task<string> GetCsrfTokenAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && _csrf is not null) return _csrf;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
        AddStdHeaders(req);
        using var res = await _http.SendAsync(req, ct);
        if (res.Headers.TryGetValues("x-csrf-token", out var vals))
            _csrf = vals.FirstOrDefault();

        CaptureRotatedCookie(res);
        if (_csrf is null)
            throw new RobloxApiException("Could not obtain an X-CSRF-TOKEN (cookie may be invalid).");
        return _csrf;
    }

    // --- Auth ticket (for launching) --------------------------------------

    public async Task<string> GetAuthTicketAsync(CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string csrf = await GetCsrfTokenAsync(force: attempt > 0, ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
            AddStdHeaders(req);
            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            req.Content = new StringContent("");

            using var res = await _http.SendAsync(req, ct);
            CaptureRotatedCookie(res);

            if (res.StatusCode == HttpStatusCode.Forbidden) { _csrf = null; continue; }
            res.EnsureSuccessStatusCode();

            if (res.Headers.TryGetValues("rbx-authentication-ticket", out var t))
            {
                string ticket = t.First();
                if (!string.IsNullOrWhiteSpace(ticket)) return ticket;
            }
            throw new RobloxApiException("Auth ticket response had no rbx-authentication-ticket header.");
        }
        throw new RobloxApiException("Failed to acquire an auth ticket after CSRF retry.");
    }

    // --- Validation ------------------------------------------------------

    public async Task<AuthenticatedUser> ValidateAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync("https://users.roblox.com/v1/users/authenticated", ct);
        // Only trust Set-Cookie from an authenticated response — a 401/403 can carry a guest cookie
        // that would otherwise clobber the stored session.
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new RobloxAuthException("Cookie is no longer valid.");
        CaptureRotatedCookie(res);
        res.EnsureSuccessStatusCode();
        var user = await res.Content.ReadFromJsonAsync<AuthenticatedUser>(cancellationToken: ct);
        return user ?? throw new RobloxApiException("Malformed authenticated-user response.");
    }

    /// <summary>Presence / "what game is this user in" for the given user ids.</summary>
    public async Task<JsonElement> GetPresencesAsync(IEnumerable<long> userIds, CancellationToken ct = default)
    {
        string csrf = await GetCsrfTokenAsync(ct: ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://presence.roblox.com/v1/presence/users");
        AddStdHeaders(req);
        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
        req.Content = JsonContent.Create(new { userIds });
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    /// <summary>Invalidate every other session for this account.</summary>
    public async Task LogoutOtherSessionsAsync(CancellationToken ct = default)
    {
        string csrf = await GetCsrfTokenAsync(ct: ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/logout-other-sessions");
        AddStdHeaders(req);
        req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
        req.Content = new StringContent("");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    // --- helpers -------------------------------------------------------

    private void AddStdHeaders(HttpRequestMessage req)
    {
        req.Headers.Referrer = new Uri("https://www.roblox.com/");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");
    }

    private void CaptureRotatedCookie(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("set-cookie", out var setCookies)) return;
        foreach (var raw in setCookies)
        {
            if (!raw.StartsWith(RoblosecurityName + "=", StringComparison.Ordinal)) continue;
            string value = raw[(RoblosecurityName.Length + 1)..].Split(';', 2)[0];
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
                continue;
            if (value == CurrentToken) continue;
            _cookies.Add(RobloxRoot, new Cookie(RoblosecurityName, value, "/", ".roblox.com"));
            CookieRotated?.Invoke(this, value);
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class AuthenticatedUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
}

public class RobloxApiException : Exception
{
    public RobloxApiException(string message) : base(message) { }
}

public sealed class RobloxAuthException : RobloxApiException
{
    public RobloxAuthException(string message) : base(message) { }
}
