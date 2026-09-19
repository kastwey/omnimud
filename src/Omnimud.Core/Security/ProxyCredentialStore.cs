using System.Security.Cryptography;
using Omnimud.Core.Options;

namespace Omnimud.Core.Security;

/// <summary>
/// The only way in and out of <see cref="OmnimudOptions.ProxyPasswordProtected"/>: encrypts when the user
/// types a password and decrypts when a connection needs it. The clear password is never stored.
/// </summary>
public interface IProxyCredentialStore
{
    /// <summary>A copy of <paramref name="options"/> with that password protected; null or empty removes it.</summary>
    OmnimudOptions WithPassword(OmnimudOptions options, string? plainPassword);

    /// <summary>
    /// The clear password, or null when there is none or it cannot be decrypted (data folder moved to another
    /// computer or Windows user: the master key does not travel). Never throws for that.
    /// </summary>
    string? GetPassword(OmnimudOptions options);

    /// <summary>True when the options carry a protected password (whether or not it can still be decrypted).</summary>
    bool HasPassword(OmnimudOptions options);
}

public sealed class ProxyCredentialStore : IProxyCredentialStore
{
    private readonly IPasswordProtector _protector;

    public ProxyCredentialStore(IPasswordProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        _protector = protector;
    }

    public OmnimudOptions WithPassword(OmnimudOptions options, string? plainPassword)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with
        {
            ProxyPasswordProtected = string.IsNullOrEmpty(plainPassword)
                ? null
                : Convert.ToBase64String(_protector.Protect(plainPassword))
        };
    }

    public string? GetPassword(OmnimudOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ProxyPasswordProtected))
            return null;

        try
        {
            var clear = _protector.Unprotect(Convert.FromBase64String(options.ProxyPasswordProtected.Trim()));
            return string.IsNullOrEmpty(clear) ? null : clear;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException
                                       or InvalidOperationException or NotSupportedException)
        {
            // Another master key, a truncated value, a hand-edited row: the same as having no password.
            // Nothing about the value goes anywhere: not even the exception is reported.
            return null;
        }
    }

    public bool HasPassword(OmnimudOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !string.IsNullOrWhiteSpace(options.ProxyPasswordProtected);
    }
}

/// <summary>For sessions and dialogs created without a protector (tests, tools): nothing is kept, nothing is read.</summary>
public sealed class NullProxyCredentialStore : IProxyCredentialStore
{
    public static NullProxyCredentialStore Instance { get; } = new();

    public OmnimudOptions WithPassword(OmnimudOptions options, string? plainPassword)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with { ProxyPasswordProtected = null };
    }

    public string? GetPassword(OmnimudOptions options) => null;

    public bool HasPassword(OmnimudOptions options) => false;
}
