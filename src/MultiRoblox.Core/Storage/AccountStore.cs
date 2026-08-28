using System.Text.Json;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Storage;

/// <summary>
/// In-memory list of accounts backed by an encrypted file. Not thread-safe for writers; call from the
/// UI thread or serialize access. Reads return copies of the list.
/// </summary>
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _path;
    private readonly SecretProtector _protector;
    private readonly List<Account> _accounts = new();

    public AccountStore(string path, SecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Account> Accounts => _accounts.OrderBy(a => a.Order).ToList();

    public Account? FindByName(string usernameOrDisplay) =>
        _accounts.FirstOrDefault(a =>
            a.Username.Equals(usernameOrDisplay, StringComparison.OrdinalIgnoreCase) ||
            a.DisplayName.Equals(usernameOrDisplay, StringComparison.OrdinalIgnoreCase));

    public Account? FindById(Guid id) => _accounts.FirstOrDefault(a => a.Id == id);

    public void Load()
    {
        _accounts.Clear();
        if (!File.Exists(_path)) return;

        byte[] blob = File.ReadAllBytes(_path);
        if (blob.Length == 0) return;

        byte[] json = _protector.Unprotect(blob);
        var loaded = JsonSerializer.Deserialize<List<Account>>(json, JsonOpts) ?? new();
        _accounts.AddRange(loaded);
        Normalize();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(_accounts, JsonOpts);
        byte[] blob = _protector.Protect(json);

        string tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, blob);
        File.Move(tmp, _path, overwrite: true);
    }

    public Account Add(Account account)
    {
        if (account.BrowserTrackerId == 0)
            account.BrowserTrackerId = Random.Shared.NextInt64(100_000_000_000, 175_000_000_000);
        account.Order = _accounts.Count == 0 ? 0 : _accounts.Max(a => a.Order) + 1;
        _accounts.Add(account);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return account;
    }

    public void Remove(Guid id)
    {
        _accounts.RemoveAll(a => a.Id == id);
        Normalize();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Account account)
    {
        int i = _accounts.FindIndex(a => a.Id == account.Id);
        if (i < 0) return;
        _accounts[i] = account;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Persist a new ordering. <paramref name="orderedIds"/> is the full list top-to-bottom.</summary>
    public void Reorder(IReadOnlyList<Guid> orderedIds)
    {
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var acc = FindById(orderedIds[i]);
            if (acc is not null) acc.Order = i;
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Normalize()
    {
        int i = 0;
        foreach (var a in _accounts.OrderBy(a => a.Order))
            a.Order = i++;
    }
}
