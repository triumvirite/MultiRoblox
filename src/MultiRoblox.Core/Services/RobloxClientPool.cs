using System.Collections.Concurrent;
using MultiRoblox.Core.Models;
using MultiRoblox.Core.Storage;

namespace MultiRoblox.Core.Services;

/// <summary>
/// One <see cref="RobloxClient"/> per account, reused across calls. Rotated cookies are written back
/// to the <see cref="AccountStore"/> automatically.
/// </summary>
public sealed class RobloxClientPool : IDisposable
{
    private readonly AccountStore _store;
    private readonly ConcurrentDictionary<Guid, RobloxClient> _clients = new();

    public RobloxClientPool(AccountStore store) => _store = store;

    public RobloxClient Get(Account account)
    {
        return _clients.GetOrAdd(account.Id, _ =>
        {
            var client = new RobloxClient(account.SecurityToken);
            client.CookieRotated += (_, newToken) =>
            {
                var current = _store.FindById(account.Id);
                if (current is null || current.SecurityToken == newToken) return;
                current.SecurityToken = newToken;
                _store.Update(current);
            };
            return client;
        });
    }

    /// <summary>Drop a cached client (e.g. after the stored cookie was replaced externally).</summary>
    public void Invalidate(Guid accountId)
    {
        if (_clients.TryRemove(accountId, out var c)) c.Dispose();
    }

    public void Dispose()
    {
        foreach (var c in _clients.Values) c.Dispose();
        _clients.Clear();
    }
}
