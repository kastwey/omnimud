using System.Security.Cryptography;

namespace Omnimud.UI.Services;

/// <summary>
/// Loads (or creates on first run) a 32-byte master key for AesPasswordProtector, stored in the
/// data directory protected with Windows DPAPI (current user).
/// DPAPI ties the key to this Windows user: when the portable folder is moved to another
/// machine the key cannot be read, so a new one is created and saved passwords must be typed
/// again. Everything else travels.
/// </summary>
internal static class MasterKeyProvider
{
    public static byte[] GetOrCreate(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var blob = File.ReadAllBytes(path);
                return ProtectedData.Unprotect(blob, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            }
            catch (Exception ex) when (ex is CryptographicException or IOException)
            {
                // Unreadable key (another machine/user, or corrupt file): keep a copy and start over.
                try { File.Move(path, path + ".unreadable", overwrite: true); }
                catch (IOException) { }
            }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBlob = ProtectedData.Protect(key, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBlob);
        return key;
    }
}
