using Omnimud.Core.Security;
using Omnimud.Data;
using Omnimud.Data.Entities;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

public enum CharacterField
{
    Name,
    Password,
    Mud
}

/// <summary>
/// State, validation and saving of the add/edit character dialog. The stored password is never
/// decrypted nor shown: the user can keep it, replace it or forget it.
/// </summary>
public sealed class CharacterEditorModel
{
    public const int MaxNameLength = 255;

    private readonly ICharacterRepository _characters;
    private readonly IMudRepository _muds;
    private readonly IPasswordProtector _protector;
    private readonly CharacterEntity? _existing;

    /// <param name="existing">Null to create.</param>
    /// <param name="mudId">MUD the new character belongs to; null when created without context (the user picks it).</param>
    public CharacterEditorModel(ICharacterRepository characters, IMudRepository muds, IPasswordProtector protector,
        CharacterEntity? existing = null, int? mudId = null)
    {
        _characters = characters;
        _muds = muds;
        _protector = protector;
        _existing = existing;

        if (existing is null)
        {
            MudId = mudId;
            RememberPassword = true;
            return;
        }

        Name = existing.Name;
        MudId = existing.MudId;
        HasStoredPassword = existing.EncryptedPassword is { Length: > 0 };
        RememberPassword = HasStoredPassword;
    }

    public bool IsNew => _existing is null;
    public string Title => IsNew ? Strings.CharEdit_TitleAdd : string.Format(Strings.CharEdit_TitleEdit, _existing!.Name);
    public int? SavedId { get; private set; }

    public string Name { get; set; } = string.Empty;
    /// <summary>Unchecking it forgets the stored password.</summary>
    public bool RememberPassword { get; set; }
    /// <summary>What the user typed now. Empty while editing = keep the stored one.</summary>
    public string Password { get; set; } = string.Empty;
    public bool HasStoredPassword { get; }
    public bool CanEditPassword => RememberPassword;
    public int? MudId { get; set; }
    /// <summary>A character cannot be moved to another MUD once created.</summary>
    public bool CanChooseMud => IsNew;

    public string PasswordHint => !RememberPassword ? Strings.CharEdit_HintNotRemembered
        : HasStoredPassword ? Strings.CharEdit_HintStored
        : Strings.CharEdit_HintNew;

    public async Task<IReadOnlyList<Choice<int>>> GetMudChoicesAsync(CancellationToken ct = default)
    {
        var muds = await _muds.GetAllAsync(ct);
        return muds.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Select(m => new Choice<int>(m.Id, m.Name)).ToList();
    }

    public FieldError<CharacterField>? Validate()
    {
        var name = Name.Trim();
        if (name.Length == 0) return new(CharacterField.Name, Strings.CharEdit_NameRequired);
        if (name.Length > MaxNameLength) return new(CharacterField.Name, string.Format(Strings.CharEdit_NameTooLong, MaxNameLength));
        if (RememberPassword && Password.Length == 0 && !HasStoredPassword)
            return new(CharacterField.Password, Strings.CharEdit_PasswordRequired);
        if (MudId is null) return new(CharacterField.Mud, Strings.CharEdit_MudRequired);
        return null;
    }

    public async Task<FieldError<CharacterField>?> SaveAsync(CancellationToken ct = default)
    {
        if (Validate() is { } error) return error;

        // The table only rejects exact duplicates, but the import looks characters up ignoring case.
        var siblings = await _characters.GetByMudAsync(MudId!.Value, ct);
        if (siblings is not null && siblings.Any(c => c.Id != (_existing?.Id ?? 0) && string.Equals(c.Name, Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return new(CharacterField.Name, string.Format(Strings.CharEdit_Duplicate, Name.Trim()));

        var now = DateTime.UtcNow;
        var password = !RememberPassword ? null
            : Password.Length > 0 ? _protector.Protect(Password)
            : _existing?.EncryptedPassword;

        try
        {
            if (_existing is null)
            {
                SavedId = await _characters.AddAsync(new CharacterEntity
                {
                    MudId = MudId!.Value,
                    Name = Name.Trim(),
                    EncryptedPassword = password,
                    CreatedAt = now,
                    UpdatedAt = now,
                }, ct);
                return null;
            }

            var (oldName, oldPassword, oldUpdated) = (_existing.Name, _existing.EncryptedPassword, _existing.UpdatedAt);
            _existing.Name = Name.Trim();
            _existing.EncryptedPassword = password;
            _existing.UpdatedAt = now;
            try
            {
                await _characters.UpdateAsync(_existing, ct);
            }
            catch
            {
                // The entity is shared with the caller: leave it as it was.
                (_existing.Name, _existing.EncryptedPassword, _existing.UpdatedAt) = (oldName, oldPassword, oldUpdated);
                throw;
            }
            SavedId = _existing.Id;
            return null;
        }
        catch (DuplicateEntityException)
        {
            return new(CharacterField.Name, string.Format(Strings.CharEdit_Duplicate, Name.Trim()));
        }
    }
}
