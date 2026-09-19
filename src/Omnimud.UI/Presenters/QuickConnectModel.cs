using System.Globalization;
using Omnimud.Core.Session;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

public enum QuickConnectField
{
    Host,
    Port,
    Encoding
}

/// <summary>The last quick connection, as the user typed it.</summary>
public sealed record QuickConnectSettings(string Host, int Port, bool UseTls, bool ValidateCertificate, string Encoding);

/// <summary>Remembers the last quick connection between runs.</summary>
public interface IQuickConnectStore
{
    /// <summary>Null the first time.</summary>
    Task<QuickConnectSettings?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(QuickConnectSettings settings, CancellationToken ct = default);
}

/// <summary>Used when no database store is given: remembered as long as the process lives.</summary>
public sealed class MemoryQuickConnectStore : IQuickConnectStore
{
    private QuickConnectSettings? _last;

    public Task<QuickConnectSettings?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_last);

    public Task SaveAsync(QuickConnectSettings settings, CancellationToken ct = default)
    {
        _last = settings;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stores it in the Options table (everything lives in the database, nothing in the Registry) as
/// "Ui.QuickConnect.*" keys. It uses a scope number of its own for global interface state: scopes
/// 0-2 belong to <c>OptionsService</c>, which rewrites the whole scope when saving and treats
/// "has any row" as "has its own options", so interface state must not be mixed with them.
/// </summary>
public sealed class OptionQuickConnectStore(IOptionRepository options) : IQuickConnectStore
{
    /// <summary>Global interface state (no scope id).</summary>
    public const int UiStateScope = 100;
    public const string Prefix = "Ui.QuickConnect.";

    public async Task<QuickConnectSettings?> LoadAsync(CancellationToken ct = default)
    {
        var host = await options.GetValueAsync(UiStateScope, null, Prefix + "Host", ct);
        if (string.IsNullOrWhiteSpace(host)) return null;

        var port = int.TryParse(await options.GetValueAsync(UiStateScope, null, Prefix + "Port", ct), NumberStyles.None,
            CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 65535 ? p : 0;
        var tls = await options.GetValueAsync(UiStateScope, null, Prefix + "UseTls", ct) == "1";
        var validate = await options.GetValueAsync(UiStateScope, null, Prefix + "ValidateCertificate", ct) != "0";
        var encoding = await options.GetValueAsync(UiStateScope, null, Prefix + "Encoding", ct);
        return new QuickConnectSettings(host, port, tls, validate, MudEncodings.IsValid(encoding) ? encoding! : MudEncodings.Default);
    }

    public async Task SaveAsync(QuickConnectSettings settings, CancellationToken ct = default)
    {
        await options.SetValueAsync(UiStateScope, null, Prefix + "Host", settings.Host, ct);
        await options.SetValueAsync(UiStateScope, null, Prefix + "Port", settings.Port.ToString(CultureInfo.InvariantCulture), ct);
        await options.SetValueAsync(UiStateScope, null, Prefix + "UseTls", settings.UseTls ? "1" : "0", ct);
        await options.SetValueAsync(UiStateScope, null, Prefix + "ValidateCertificate", settings.ValidateCertificate ? "1" : "0", ct);
        await options.SetValueAsync(UiStateScope, null, Prefix + "Encoding", settings.Encoding, ct);
    }
}

/// <summary>
/// Connection without a saved MUD. The first time host and port are empty; afterwards the dialog
/// comes filled with the last connection used. Validation and the resulting profile live here.
/// </summary>
public sealed class QuickConnectModel
{
    private readonly IQuickConnectStore _store;

    public QuickConnectModel(IQuickConnectStore? store = null) => _store = store ?? new MemoryQuickConnectStore();

    public string Host { get; set; } = string.Empty;
    /// <summary>As typed: empty the first time, and validated as text so "abc" or "" are reported, not silently fixed.</summary>
    public string Port { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    public bool CanValidateCertificate => UseTls;
    public string Encoding { get; set; } = MudEncodings.Default;

    /// <summary>Fills the model with the last connection used, if there is one.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (await _store.LoadAsync(ct) is not { } last) return;
        Host = last.Host;
        Port = last.Port > 0 ? last.Port.ToString(CultureInfo.InvariantCulture) : string.Empty;
        UseTls = last.UseTls;
        ValidateCertificate = last.ValidateCertificate;
        Encoding = last.Encoding;
    }

    private int? PortNumber =>
        int.TryParse(Port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535 ? port : null;

    public FieldError<QuickConnectField>? Validate()
    {
        var host = Host.Trim();
        if (host.Length == 0) return new(QuickConnectField.Host, Strings.QuickConnect_HostRequired);
        if (host.Any(char.IsWhiteSpace)) return new(QuickConnectField.Host, Strings.QuickConnect_HostInvalid);
        if (Port.Trim().Length == 0) return new(QuickConnectField.Port, Strings.QuickConnect_PortRequired);
        if (PortNumber is null) return new(QuickConnectField.Port, Strings.QuickConnect_PortInvalid);
        if (!MudEncodings.IsValid(Encoding))
            return new(QuickConnectField.Encoding, string.Format(Strings.QuickConnect_EncodingInvalid, Encoding.Trim()));
        return null;
    }

    /// <summary>Validates, remembers the connection for next time and returns the profile. Null profile = see the error.</summary>
    public async Task<(SessionProfile? Profile, FieldError<QuickConnectField>? Error)> AcceptAsync(CancellationToken ct = default)
    {
        if (Validate() is { } error) return (null, error);
        var profile = ToProfile();
        await _store.SaveAsync(new QuickConnectSettings(profile.Host, profile.Port, UseTls, ValidateCertificate, profile.Encoding), ct);
        return (profile, null);
    }

    /// <summary>Only meaningful for a valid model.</summary>
    public SessionProfile ToProfile()
    {
        var port = PortNumber ?? throw new InvalidOperationException("The model is not valid.");
        return new SessionProfile
        {
            Title = $"{Host.Trim()}:{port}",
            Host = Host.Trim(),
            Port = port,
            UseTls = UseTls,
            ValidateCertificate = ValidateCertificate,
            Encoding = Encoding.Trim(),
        };
    }
}
