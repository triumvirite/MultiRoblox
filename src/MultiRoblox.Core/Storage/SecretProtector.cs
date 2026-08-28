using System.Security.Cryptography;
using System.Text;

namespace MultiRoblox.Core.Storage;

/// <summary>
/// Encrypts the account blob. Layer 1 is always Windows DPAPI (CurrentUser) — "your machine + your
/// login is the key". Layer 2 is an optional user passphrase (PBKDF2-SHA256 → AES-256-GCM).
/// </summary>
public sealed class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MultiRoblox.v1.accounts");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 200_000;

    private readonly string? _passphrase;

    public SecretProtector(string? passphrase = null) =>
        _passphrase = string.IsNullOrEmpty(passphrase) ? null : passphrase;

    public byte[] Protect(byte[] plaintext)
    {
        byte[] dpapi = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        if (_passphrase is null)
            return Prepend(0x01, dpapi);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(_passphrase, salt);
        byte[] cipher = new byte[dpapi.Length];
        byte[] tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, dpapi, cipher, tag);
        CryptographicOperations.ZeroMemory(key);

        using var ms = new MemoryStream();
        ms.WriteByte(0x02);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);
        return ms.ToArray();
    }

    public byte[] Unprotect(byte[] blob)
    {
        if (blob.Length < 1) throw new InvalidDataException("Empty account blob.");
        byte version = blob[0];
        ReadOnlySpan<byte> body = blob.AsSpan(1);

        if (version == 0x01)
            return ProtectedData.Unprotect(body.ToArray(), Entropy, DataProtectionScope.CurrentUser);

        if (version == 0x02)
        {
            if (_passphrase is null)
                throw new UnauthorizedAccessException("Account file is passphrase-protected but no passphrase was supplied.");
            var salt = body[..SaltSize].ToArray();
            var nonce = body.Slice(SaltSize, NonceSize).ToArray();
            var tag = body.Slice(SaltSize + NonceSize, TagSize).ToArray();
            var cipher = body[(SaltSize + NonceSize + TagSize)..].ToArray();
            byte[] key = DeriveKey(_passphrase, salt);
            byte[] dpapi = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, dpapi);
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Wrong passphrase or corrupted account file.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
            return ProtectedData.Unprotect(dpapi, Entropy, DataProtectionScope.CurrentUser);
        }

        throw new InvalidDataException($"Unknown account file version 0x{version:X2}.");
    }

    private static byte[] Prepend(byte version, byte[] data)
    {
        var result = new byte[data.Length + 1];
        result[0] = version;
        Buffer.BlockCopy(data, 0, result, 1, data.Length);
        return result;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
}
