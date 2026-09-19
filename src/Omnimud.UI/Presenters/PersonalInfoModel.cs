using System.Text.RegularExpressions;
using Omnimud.Data.Repositories;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>Name and e-mail address of the user. Both optional. Only ever used to sign a report the user sends by e-mail.</summary>
public sealed record PersonalInfo(string Name, string Email)
{
    public static PersonalInfo Empty { get; } = new(string.Empty, string.Empty);

    public bool IsEmpty => Name.Length == 0 && Email.Length == 0;

    /// <summary>"Name &lt;address&gt;", "Name" or "address"; empty when there is nothing.</summary>
    public string Signature =>
        Name.Length > 0 && Email.Length > 0 ? $"{Name} <{Email}>" : Name.Length > 0 ? Name : Email;
}

public interface IPersonalInfoStore
{
    Task<PersonalInfo> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(PersonalInfo info, CancellationToken ct = default);
}

/// <summary>Used when there is no database (tests, a window created without the container): lasts as long as the process.</summary>
public sealed class MemoryPersonalInfoStore : IPersonalInfoStore
{
    private PersonalInfo _info = PersonalInfo.Empty;

    public Task<PersonalInfo> LoadAsync(CancellationToken ct = default) => Task.FromResult(_info);

    public Task SaveAsync(PersonalInfo info, CancellationToken ct = default)
    {
        _info = info;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Keeps the personal information in the Options table under a scope of its own (101), like the other interface
/// state (<see cref="OptionQuickConnectStore"/> uses 100 and <see cref="OptionListSortStore"/> 102). Scopes 0-2
/// belong to <c>OptionsService</c> and are what <c>.omnimud</c> files export: by living outside them the personal
/// information is never exported, never inherited and never part of an options block.
/// </summary>
public sealed class OptionPersonalInfoStore(IOptionRepository options) : IPersonalInfoStore
{
    /// <summary>Personal information of the user of this installation (no scope id).</summary>
    public const int PersonalInfoScope = 101;
    public const string NameKey = "PersonalInfo.Name";
    public const string EmailKey = "PersonalInfo.Email";

    public async Task<PersonalInfo> LoadAsync(CancellationToken ct = default)
    {
        var name = await options.GetValueAsync(PersonalInfoScope, null, NameKey, ct).ConfigureAwait(false);
        var email = await options.GetValueAsync(PersonalInfoScope, null, EmailKey, ct).ConfigureAwait(false);
        return new PersonalInfo(name?.Trim() ?? string.Empty, email?.Trim() ?? string.Empty);
    }

    public async Task SaveAsync(PersonalInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        await SetOrDeleteAsync(NameKey, info.Name, ct).ConfigureAwait(false);
        await SetOrDeleteAsync(EmailKey, info.Email, ct).ConfigureAwait(false);
    }

    /// <summary>An emptied field leaves no row behind: deleting one's data really deletes it.</summary>
    private Task SetOrDeleteAsync(string key, string value, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(value)
            ? options.DeleteAsync(PersonalInfoScope, null, key, ct)
            : options.SetValueAsync(PersonalInfoScope, null, key, value.Trim(), ct);
}

/// <summary>Logic of the personal information dialog: load, validate, save. No WinForms.</summary>
public sealed partial class PersonalInfoModel(IPersonalInfoStore store)
{
    public const int MaxLength = 255;

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var info = await store.LoadAsync(ct);
        Name = info.Name;
        Email = info.Email;
    }

    /// <summary>Both fields are optional; what is written must make sense. Null = valid.</summary>
    public EditorIssue? Validate()
    {
        var name = Name.Trim();
        var email = Email.Trim();
        if (name.Length > MaxLength)
            return new EditorIssue(nameof(Name), string.Format(Strings.PersonalInfo_ErrNameTooLong, MaxLength));
        if (name.Any(char.IsControl))
            return new EditorIssue(nameof(Name), Strings.PersonalInfo_ErrNameInvalid);
        if (email.Length > MaxLength || (email.Length > 0 && !IsValidEmail(email)))
            return new EditorIssue(nameof(Email), Strings.PersonalInfo_ErrEmailInvalid);
        return null;
    }

    /// <summary>Saves when valid. Returns the problem otherwise (nothing is stored).</summary>
    public async Task<EditorIssue?> SaveAsync(CancellationToken ct = default)
    {
        if (Validate() is { } issue) return issue;
        await store.SaveAsync(new PersonalInfo(Name.Trim(), Email.Trim()), ct);
        return null;
    }

    /// <summary>Basic shape only (something@domain.tld, no spaces, one @): the address is never used to send anything from here.</summary>
    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= MaxLength && EmailShape().IsMatch(email.Trim());

    [GeneratedRegex(@"^[^\s@<>,;:""()\[\]\\]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex EmailShape();
}
