using Microsoft.Extensions.Logging;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Storage;

namespace MultiRoblox.Core.Services;

public enum AccountHealth { Unknown, Valid, NeedsAttention }

/// <summary>
/// Keeps every stored session warm. Roblox's <c>.ROBLOSECURITY</c> cookie has no fixed lifetime but
/// it <b>rotates</b>: authenticated responses periodically carry a fresh value and retire the old one.
/// By pinging a lightweight authenticated endpoint on a timer we (a) capture each rotation via
/// <see cref="RobloxClient.CookieRotated"/> and persist it, and (b) surface a health flag so the UI
/// can show which accounts actually need a re-login (logout-all, password change, ban, security hold).
/// </summary>
public sealed class CookieKeepAlive : IDisposable
{
    private readonly AccountStore _store;
    private readonly RobloxClientPool _pool;
    private readonly SettingsStore _settings;
    private readonly ILogger<CookieKeepAlive>? _log;
    private readonly Dictionary<Guid, AccountHealth> _health = new();
    private Timer? _timer;

    public CookieKeepAlive(AccountStore store, RobloxClientPool pool, SettingsStore settings,
        ILogger<CookieKeepAlive>? log = null)
    {
        _store = store;
        _pool = pool;
        _settings = settings;
        _log = log;
    }

    public event EventHandler<(Guid AccountId, AccountHealth Health)>? HealthChanged;

    public AccountHealth GetHealth(Guid id) => _health.TryGetValue(id, out var h) ? h : AccountHealth.Unknown;

    public void Start()
    {
        Stop();
        _ = RefreshAllAsync(); // once at startup
        int minutes = Math.Max(5, _settings.Current.CookieRefreshMinutes);
        if (_settings.Current.CookieRefreshMinutes <= 0) return;
        _timer = new Timer(_ => _ = RefreshAllAsync(), null,
            TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public async Task<AccountHealth> RefreshAsync(Account account, CancellationToken ct = default)
    {
        var health = AccountHealth.Valid;
        try
        {
            var user = await _pool.Get(account).ValidateAsync(ct);
            if (user.Id != 0 && (account.UserId != user.Id || account.Username != user.Name))
            {
                account.UserId = user.Id;
                account.Username = user.Name;
                account.DisplayName = user.DisplayName;
                _store.Update(account);
            }
        }
        catch (RobloxAuthException)
        {
            health = AccountHealth.NeedsAttention;
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "keep-alive transient failure for {User}", account.Username);
            return GetHealth(account.Id); // network blip — keep prior state
        }

        if (GetHealth(account.Id) != health)
        {
            _health[account.Id] = health;
            HealthChanged?.Invoke(this, (account.Id, health));
        }
        return health;
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        foreach (var acc in _store.Accounts)
        {
            if (ct.IsCancellationRequested) return;
            await RefreshAsync(acc, ct);
        }
    }

    public void Dispose() => Stop();
}
