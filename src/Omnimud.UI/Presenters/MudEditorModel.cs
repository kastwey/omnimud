using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

public enum MudField
{
    Name,
    Host,
    Port,
    Encoding,
    SoundDirectory
}

/// <summary>State, validation and saving of the add/edit MUD dialog. No WinForms here.</summary>
public sealed class MudEditorModel
{
    public const int MaxNameLength = 255;

    private readonly IMudRepository _muds;
    private readonly IMessageRuleRepository _rules;
    private readonly MudEntity? _existing;
    private readonly Func<string, bool> _directoryExists;

    public MudEditorModel(IMudRepository muds, IMessageRuleRepository rules, MudEntity? existing = null,
        Func<string, bool>? directoryExists = null)
    {
        _muds = muds;
        _rules = rules;
        _existing = existing;
        _directoryExists = directoryExists ?? Directory.Exists;

        if (existing is null) return;
        Name = existing.Name;
        Host = existing.Host;
        Port = existing.Port;
        UseTls = existing.UseTls;
        ValidateCertificate = existing.ValidateCertificate;
        MessageRuleSetId = existing.MessageRuleSetId;
        SoundDirectory = existing.SoundDirectory ?? string.Empty;
        Encoding = string.IsNullOrWhiteSpace(existing.Encoding) ? MudEncodings.Default : existing.Encoding;
        SaveCommand = existing.SaveCommand ?? string.Empty;
        QuitCommand = existing.QuitCommand ?? string.Empty;
        LoginScript = existing.LoginScript ?? string.Empty;
    }

    public bool IsNew => _existing is null;
    public string Title => IsNew ? Strings.MudEdit_TitleAdd : string.Format(Strings.MudEdit_TitleEdit, _existing!.Name);

    /// <summary>Id of the MUD after a successful <see cref="SaveAsync"/>.</summary>
    public int? SavedId { get; private set; }

    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 23;
    public bool UseTls { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    /// <summary>The certificate check only makes sense (and is only editable) over TLS.</summary>
    public bool CanValidateCertificate => UseTls;
    public int? MessageRuleSetId { get; set; }
    /// <summary>Empty = the default sound folder of the MUD.</summary>
    public string SoundDirectory { get; set; } = string.Empty;
    public string Encoding { get; set; } = MudEncodings.Default;
    public string SaveCommand { get; set; } = string.Empty;
    public string QuitCommand { get; set; } = string.Empty;
    public string LoginScript { get; set; } = string.Empty;

    /// <summary>"None" first, then every rule set by name.</summary>
    public async Task<IReadOnlyList<Choice<int?>>> GetRuleSetChoicesAsync(CancellationToken ct = default)
    {
        var sets = await _rules.GetRuleSetsAsync(ct);
        var choices = new List<Choice<int?>> { new(null, Strings.MudEdit_RuleSetNone) };
        choices.AddRange(sets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).Select(s => new Choice<int?>(s.Id, s.Name)));
        return choices;
    }

    public FieldError<MudField>? Validate()
    {
        var name = Name.Trim();
        if (name.Length == 0) return new(MudField.Name, Strings.MudEdit_NameRequired);
        if (name.Length > MaxNameLength) return new(MudField.Name, string.Format(Strings.MudEdit_NameTooLong, MaxNameLength));
        if (Host.Trim().Length == 0) return new(MudField.Host, Strings.MudEdit_HostRequired);
        if (Host.Trim().Any(char.IsWhiteSpace)) return new(MudField.Host, Strings.MudEdit_HostInvalid);
        if (Port is < 1 or > 65535) return new(MudField.Port, Strings.MudEdit_PortInvalid);
        if (!MudEncodings.IsValid(Encoding)) return new(MudField.Encoding, string.Format(Strings.MudEdit_EncodingInvalid, Encoding.Trim()));
        if (SoundDirectory.Trim().Length > 0 && !_directoryExists(SoundDirectory.Trim()))
            return new(MudField.SoundDirectory, Strings.MudEdit_SoundDirectoryMissing);
        return null;
    }

    /// <summary>Validates and writes. Null = saved (see <see cref="SavedId"/>); otherwise what to tell the user and where to put the focus.</summary>
    public async Task<FieldError<MudField>?> SaveAsync(CancellationToken ct = default)
    {
        if (Validate() is { } error) return error;

        // The table only rejects exact duplicates, but every lookup by name (and the import) ignores case.
        if (await _muds.GetByNameAsync(Name.Trim(), ct) is { } sameName && sameName.Id != (_existing?.Id ?? 0))
            return Duplicate();

        var now = DateTime.UtcNow;
        var mud = _existing ?? new MudEntity { Name = string.Empty, Host = string.Empty, CreatedAt = now };
        var restore = Snapshot(mud);

        mud.Name = Name.Trim();
        mud.Host = Host.Trim();
        mud.Port = Port;
        mud.UseTls = UseTls;
        mud.ValidateCertificate = ValidateCertificate;
        mud.MessageRuleSetId = MessageRuleSetId;
        mud.SoundDirectory = NullIfBlank(SoundDirectory);
        mud.Encoding = Encoding.Trim();
        mud.SaveCommand = NullIfBlank(SaveCommand);
        mud.QuitCommand = NullIfBlank(QuitCommand);
        mud.LoginScript = string.IsNullOrWhiteSpace(LoginScript) ? null : LoginScript;
        mud.UpdatedAt = now;

        try
        {
            if (IsNew)
            {
                SavedId = await _muds.AddAsync(mud, ct);
            }
            else
            {
                await _muds.UpdateAsync(mud, ct);
                SavedId = mud.Id;
            }
            return null;
        }
        catch (DuplicateEntityException)
        {
            // The entity is shared with the caller: leave it as it was.
            restore(mud);
            return Duplicate();
        }
        catch
        {
            restore(mud);
            throw;
        }
    }

    private FieldError<MudField> Duplicate() => new(MudField.Name, string.Format(Strings.MudEdit_Duplicate, Name.Trim()));

    private static Action<MudEntity> Snapshot(MudEntity m)
    {
        var (name, host, port, tls, validate, ruleSet, sound, encoding, save, quit, login, updated) =
            (m.Name, m.Host, m.Port, m.UseTls, m.ValidateCertificate, m.MessageRuleSetId, m.SoundDirectory, m.Encoding,
                m.SaveCommand, m.QuitCommand, m.LoginScript, m.UpdatedAt);
        return target =>
        {
            target.Name = name; target.Host = host; target.Port = port; target.UseTls = tls;
            target.ValidateCertificate = validate; target.MessageRuleSetId = ruleSet; target.SoundDirectory = sound;
            target.Encoding = encoding; target.SaveCommand = save; target.QuitCommand = quit; target.LoginScript = login;
            target.UpdatedAt = updated;
        };
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
