using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Reads an account list exported by ic3w0lf22's Roblox Account Manager (its <c>AccountData.json</c>,
/// found next to the RAM executable).
///
/// RAM stores that file one of three ways:
///  • plain UTF-8 JSON (only if the user opted out of encryption),
///  • DPAPI-protected with a fixed entropy (the default), or
///  • libsodium password-encrypted (starts with RAM's ASCII header) — not supported here; the user
///    must remove the password in RAM first.
/// </summary>
public static class RamImporter
{
    public enum Result { Ok, FileMissing, PasswordProtected, Unreadable, Empty }

    /// <summary>A single account as RAM serialises it (public fields + a few properties, Newtonsoft, PascalCase).</summary>
    public sealed class RamAccount
    {
        public string SecurityToken { get; set; } = "";
        public string Username { get; set; } = "";
        public long UserID { get; set; }
        public string Alias { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "";
        public string? BrowserTrackerID { get; set; }
        public Dictionary<string, string>? Fields { get; set; }
    }

    // "ROBLOX ACCOUNT MANAGER | :) | BROUGHT TO YOU BUY ic3w0lf"
    private static readonly byte[] Entropy =
    {
        0x52,0x4f,0x42,0x4c,0x4f,0x58,0x20,0x41,0x43,0x43,0x4f,0x55,0x4e,0x54,0x20,0x4d,
        0x41,0x4e,0x41,0x47,0x45,0x52,0x20,0x7c,0x20,0x3a,0x29,0x20,0x7c,0x20,0x42,0x52,
        0x4f,0x55,0x47,0x48,0x54,0x20,0x54,0x4f,0x20,0x59,0x4f,0x55,0x20,0x42,0x55,0x59,
        0x20,0x69,0x63,0x33,0x77,0x30,0x6c,0x66,
    };

    // "Roblox Account Manager created by ic3w0lf22 @ github.com ......."
    private static readonly byte[] SodiumHeader =
        Encoding.ASCII.GetBytes("Roblox Account Manager created by ic3w0lf22 @ github.com .......");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static (Result Status, IReadOnlyList<RamAccount> Accounts) Read(string path)
    {
        if (!File.Exists(path)) return (Result.FileMissing, Array.Empty<RamAccount>());

        byte[] raw;
        try { raw = File.ReadAllBytes(path); }
        catch { return (Result.Unreadable, Array.Empty<RamAccount>()); }

        if (raw.Length >= SodiumHeader.Length &&
            raw.AsSpan(0, SodiumHeader.Length).SequenceEqual(SodiumHeader))
            return (Result.PasswordProtected, Array.Empty<RamAccount>());

        string? json = TryPlain(raw) ?? TryDpapi(raw);
        if (json is null) return (Result.Unreadable, Array.Empty<RamAccount>());

        List<RamAccount>? list;
        try { list = JsonSerializer.Deserialize<List<RamAccount>>(json, JsonOpts); }
        catch { return (Result.Unreadable, Array.Empty<RamAccount>()); }

        var accounts = (list ?? new())
            .Where(a => !string.IsNullOrWhiteSpace(a.SecurityToken))
            .ToList();

        return accounts.Count == 0
            ? (Result.Empty, Array.Empty<RamAccount>())
            : (Result.Ok, accounts);
    }

    private static string? TryPlain(byte[] raw)
    {
        try
        {
            string s = Encoding.UTF8.GetString(raw).TrimStart('﻿', ' ', '\r', '\n', '\t');
            return s.StartsWith('[') || s.StartsWith('{') ? s : null;
        }
        catch { return null; }
    }

    private static string? TryDpapi(byte[] raw)
    {
        foreach (var scope in new[] { DataProtectionScope.LocalMachine, DataProtectionScope.CurrentUser })
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, Entropy, scope)); }
            catch (CryptographicException) { }
            catch (PlatformNotSupportedException) { break; }
        }
        return null;
    }
}
