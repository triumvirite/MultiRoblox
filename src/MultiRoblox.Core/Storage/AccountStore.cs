using System.Text.Json;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Storage;

/// <summary>
/// In-memory list of accounts backed by an encrypted file. Not thread-safe for writers; call from the
/// UI thread or serialize access. Reads return copies of the list. File access is serialised across
/// every MultiRoblox process on the machine via a named mutex.
/// </summary>
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _path;
    private readonly SecretProtector _protector;
    private readonly List<Account> _accounts = new();

    private static readonly Mutex FileLock = new(false, @"Local\MultiRoblox.accounts.file");

    private static IDisposable AcquireFileLock()
    {
        try { FileLock.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { }
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        public void Dispose() { try { FileLock.ReleaseMutex(); } catch { } }
    }

    public AccountStore(string path, SecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public event EventHandler? Changed;

    /// <summary>Optional diagnostic sink (wired to the app logger).</summary>
    public Action<string>? Log { get; init; }

    public IReadOnlyList<Account> Accounts => _accounts.OrderBy(a => a.Order).ToList();

    public Account? FindByName(string usernameOrDisplay) =>
        _accounts.FirstOrDefault(a =>
            a.Username.Equals(usernameOrDisplay, StringComparison.OrdinalIgnoreCase) ||
            a.DisplayName.Equals(usernameOrDisplay, StringComparison.OrdinalIgnoreCase));

    public Account? FindById(Guid id) => _accounts.FirstOrDefault(a => a.Id == id);

    public void Load()
    {
        _accounts.Clear();
        using var _lock = AcquireFileLock();
        if (!File.Exists(_path)) return;

        byte[] blob = ReadAllBytesShared(_path);
        if (blob.Length == 0) return;

        byte[] json = _protector.Unprotect(blob);
        var loaded = JsonSerializer.Deserialize<List<Account>>(json, JsonOpts) ?? new();
        foreach (var a in loaded)
        {
            // migrate the old single Group field into the Categories list
            if (a.Categories.Count == 0 && !string.IsNullOrWhiteSpace(a.Group))
                a.Categories.Add(a.Group.Trim());
            a.Group = "";
        }
        _accounts.AddRange(loaded);
        Normalize();
        Log?.Invoke($"loaded {_accounts.Count} account(s) from {_path}");
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        // Another instance mid-Save can hold the file briefly; retry before giving up.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Save()
    {
        if (_accounts.Count == 0)
        {
            // Never let a transient empty in-memory list wipe the stored file. A genuine
            // "removed my last account" is rare and still goes through Remove().
            Log?.Invoke("save skipped: account list is empty");
            return;
        }

        using var _lock = AcquireFileLock();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(_accounts, JsonOpts);
        byte[] blob = _protector.Protect(json);

        string tmp = _path + "." + Environment.ProcessId + ".tmp";
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

    /// <summary>Bulk add (import): one Save + one Changed for the whole batch. Returns the count added.</summary>
    public int AddMany(IEnumerable<Account> accounts)
    {
        int next = _accounts.Count == 0 ? 0 : _accounts.Max(a => a.Order) + 1;
        int added = 0;
        foreach (var account in accounts)
        {
            if (account.BrowserTrackerId == 0)
                account.BrowserTrackerId = Random.Shared.NextInt64(100_000_000_000, 175_000_000_000);
            account.Order = next++;
            _accounts.Add(account);
            added++;
        }
        if (added == 0) return 0;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public void Remove(Guid id)
    {
        _accounts.RemoveAll(a => a.Id == id);
        Normalize();
        SaveAllowingEmpty();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Like <see cref="Save"/> but permits writing an empty list (explicit last-account removal).</summary>
    private void SaveAllowingEmpty()
    {
        if (_accounts.Count > 0) { Save(); return; }
        using var _lock = AcquireFileLock();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] blob = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(_accounts, JsonOpts));
        string tmp = _path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(tmp, blob);
        File.Move(tmp, _path, overwrite: true);
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
