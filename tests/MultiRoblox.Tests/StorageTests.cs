using MultiRoblox.Core.Models;
using MultiRoblox.Core.Storage;
using Xunit;

namespace MultiRoblox.Tests;

public class StorageTests
{
    [Fact]
    public void AccountStore_roundtrips_encrypted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mr-test-{Guid.NewGuid():N}.dat");
        try
        {
            var protector = new SecretProtector();
            var store = new AccountStore(path, protector);
            store.Load();
            store.Add(new Account { Username = "alice", SecurityToken = "SECRET_TOKEN", Group = "Mains" });

            byte[] raw = File.ReadAllBytes(path);
            Assert.DoesNotContain("SECRET_TOKEN", System.Text.Encoding.UTF8.GetString(raw));

            var reopened = new AccountStore(path, new SecretProtector());
            reopened.Load();
            var acc = Assert.Single(reopened.Accounts);
            Assert.Equal("alice", acc.Username);
            Assert.Equal("SECRET_TOKEN", acc.SecurityToken);
            Assert.NotEqual(0, acc.BrowserTrackerId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Passphrase_layer_requires_correct_passphrase()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("hello world");
        byte[] blob = new SecretProtector("hunter2").Protect(plain);

        Assert.Throws<UnauthorizedAccessException>(() => new SecretProtector("wrong").Unprotect(blob));
        Assert.Equal(plain, new SecretProtector("hunter2").Unprotect(blob));
    }

    [Fact]
    public void ExtractExe_handles_quoted_command()
    {
        Assert.Equal(@"C:\a b\RobloxPlayerBeta.exe",
            MultiRoblox.Core.Services.RobloxPlayerLocator.ExtractExe("\"C:\\a b\\RobloxPlayerBeta.exe\" \"%1\""));
    }
}
