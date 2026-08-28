using System.Security.Cryptography;
using System.Text;
using MultiRoblox.Core.Services;
using Xunit;

namespace MultiRoblox.Tests;

public class RamImporterTests
{
    // Same fixed entropy RAM uses for its default DPAPI store.
    private static readonly byte[] Entropy =
    {
        0x52,0x4f,0x42,0x4c,0x4f,0x58,0x20,0x41,0x43,0x43,0x4f,0x55,0x4e,0x54,0x20,0x4d,
        0x41,0x4e,0x41,0x47,0x45,0x52,0x20,0x7c,0x20,0x3a,0x29,0x20,0x7c,0x20,0x42,0x52,
        0x4f,0x55,0x47,0x48,0x54,0x20,0x54,0x4f,0x20,0x59,0x4f,0x55,0x20,0x42,0x55,0x59,
        0x20,0x69,0x63,0x33,0x77,0x30,0x6c,0x66,
    };

    private const string SampleJson = """
    [
      { "Valid": true, "SecurityToken": "_|WARNING:token-one|_abc", "Username": "alpha", "UserID": 111,
        "Group": "Mains", "Alias": "a1", "Description": "primary", "BrowserTrackerID": "123456789012",
        "Fields": { "note": "vip" } },
      { "SecurityToken": "_|WARNING:token-two|_def", "Username": "bravo", "UserID": "222",
        "Group": "Default", "Alias": "", "Description": "" },
      { "SecurityToken": "", "Username": "empty-skipme", "UserID": 333 }
    ]
    """;

    [Fact]
    public void Reads_plain_json()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, SampleJson);
        try
        {
            var (status, accounts) = RamImporter.Read(f);
            Assert.Equal(RamImporter.Result.Ok, status);
            Assert.Equal(2, accounts.Count); // the empty-token row is dropped
            var a = accounts[0];
            Assert.Equal("alpha", a.Username);
            Assert.Equal(111, a.UserID);
            Assert.Equal("Mains", a.Group);
            Assert.Equal("a1", a.Alias);
            Assert.Equal("123456789012", a.BrowserTrackerID);
            Assert.Equal("vip", a.Fields!["note"]);
            Assert.Equal(222, accounts[1].UserID); // parsed from a JSON string
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Reads_dpapi_protected_json()
    {
        byte[] blob;
        try { blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(SampleJson), Entropy, DataProtectionScope.CurrentUser); }
        catch (PlatformNotSupportedException) { return; } // non-Windows CI

        var f = Path.GetTempFileName();
        File.WriteAllBytes(f, blob);
        try
        {
            var (status, accounts) = RamImporter.Read(f);
            Assert.Equal(RamImporter.Result.Ok, status);
            Assert.Equal(2, accounts.Count);
            Assert.Equal("alpha", accounts[0].Username);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Flags_a_sodium_password_protected_file()
    {
        var header = Encoding.ASCII.GetBytes("Roblox Account Manager created by ic3w0lf22 @ github.com .......");
        var f = Path.GetTempFileName();
        File.WriteAllBytes(f, header.Concat(new byte[64]).ToArray());
        try
        {
            var (status, _) = RamImporter.Read(f);
            Assert.Equal(RamImporter.Result.PasswordProtected, status);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var (status, _) = RamImporter.Read(Path.Combine(Path.GetTempPath(), "definitely-not-there-" + Guid.NewGuid() + ".json"));
        Assert.Equal(RamImporter.Result.FileMissing, status);
    }
}
