using System.Security.Cryptography;
using System.Text;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Security;

/// <summary>
/// Encrypts/decrypts passwords using AES-256-GCM with a key protected via DPAPI.
/// </summary>
public sealed class AesPasswordProtector : IPasswordProtector
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _masterKey;

    /// <summary>
    /// Creates a protector with the given master key.
    /// In production, this key should come from DPAPI or a secure key store.
    /// </summary>
    public AesPasswordProtector(byte[] masterKey)
    {
        if (masterKey.Length != KeySize)
            throw new ArgumentException(string.Format(Strings.Error_MasterKeySize, KeySize), nameof(masterKey));

        _masterKey = masterKey;
    }

    public byte[] Protect(string plainPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainPassword);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(salt);

        var plainBytes = Encoding.UTF8.GetBytes(plainPassword);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        // Zero out sensitive data
        CryptographicOperations.ZeroMemory(plainBytes);
        CryptographicOperations.ZeroMemory(key);

        // Format: salt (16) + nonce (12) + tag (16) + ciphertext (variable)
        var result = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
        salt.CopyTo(result, 0);
        nonce.CopyTo(result, SaltSize);
        tag.CopyTo(result, SaltSize + NonceSize);
        ciphertext.CopyTo(result, SaltSize + NonceSize + TagSize);

        return result;
    }

    public string Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < SaltSize + NonceSize + TagSize + 1)
            throw new ArgumentException(Strings.Error_ProtectedDataTooShort, nameof(protectedData));

        var salt = protectedData.AsSpan(0, SaltSize);
        var nonce = protectedData.AsSpan(SaltSize, NonceSize);
        var tag = protectedData.AsSpan(SaltSize + NonceSize, TagSize);
        var ciphertext = protectedData.AsSpan(SaltSize + NonceSize + TagSize);

        var key = DeriveKey(salt);
        var plainBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plainBytes);

        CryptographicOperations.ZeroMemory(key);

        var result = Encoding.UTF8.GetString(plainBytes);
        CryptographicOperations.ZeroMemory(plainBytes);

        return result;
    }

    private byte[] DeriveKey(ReadOnlySpan<byte> salt)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _masterKey,
            KeySize,
            salt: salt.ToArray(),
            info: "Omnimud.PasswordProtection.v1"u8.ToArray());
    }
}
