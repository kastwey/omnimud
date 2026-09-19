namespace Omnimud.Core.Security;

public interface IPasswordProtector
{
    /// <summary>Encrypts a plaintext password. Returns salt + ciphertext blob.</summary>
    byte[] Protect(string plainPassword);

    /// <summary>Decrypts a protected password blob.</summary>
    string Unprotect(byte[] protectedData);
}
