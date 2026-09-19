using System.Text;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.Data.Exchange;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Presenters;

/// <summary>A validation problem: the message for the user and the field that must get the focus.</summary>
public sealed record OptionsValidationError(OptionsField Field, string Message);

/// <summary>What happened when the user pressed OK.</summary>
public sealed record OptionsAcceptResult(bool Saved, OptionsField? FocusField = null)
{
    public static OptionsAcceptResult Done { get; } = new(true);
    public static OptionsAcceptResult Stay(OptionsField field) => new(false, field);
}

/// <summary>The few things the options dialog needs from the file system, replaceable in tests.</summary>
public interface IDirectoryAccess
{
    bool Exists(string path);
    void Create(string path);
}

public sealed class SystemDirectoryAccess : IDirectoryAccess
{
    public bool Exists(string path) => Directory.Exists(path);
    public void Create(string path) => Directory.CreateDirectory(path);
}

/// <summary>
/// All the logic of the options dialog, without a window: which block a scope shows (its own or the
/// inherited one), validation, conversion, saving, going back to inherit, export and import.
/// The form only moves values between this class and its controls.
/// </summary>
public sealed class OptionsEditorModel
{
    private readonly IOptionsService _service;
    private readonly IOptionsFileStore _files;
    private readonly IDirectoryAccess _directories;
    private readonly IProxyCredentialStore? _credentials;
    private readonly int? _parentMudId;
    private OmnimudOptions _loaded = OmnimudOptions.Default;
    private OptionsFields? _ownDraft;

    /// <param name="scopeId">Id of the MUD or of the character; ignored for Global.</param>
    /// <param name="scopeName">Name of the MUD or of the character, for the title.</param>
    /// <param name="parentMudId">For the Character scope: the MUD of the character, needed to know what it inherits.
    /// Without it the character is shown as inheriting from the global options.</param>
    /// <param name="credentials">Protects a newly typed proxy password. Without it the dialog keeps or deletes the
    /// stored password but refuses a new one (it would have to store it in clear).</param>
    public OptionsEditorModel(IOptionsService service, OptionScope scope, int? scopeId, string? scopeName,
        int? parentMudId = null, IOptionsFileStore? files = null, IDirectoryAccess? directories = null,
        IProxyCredentialStore? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope != OptionScope.Global && scopeId is null)
            throw new ArgumentException($"The {scope} scope needs an id.", nameof(scopeId));

        _service = service;
        Scope = scope;
        ScopeId = scope == OptionScope.Global ? null : scopeId;
        ScopeName = scopeName ?? string.Empty;
        _parentMudId = scope == OptionScope.Character ? parentMudId : null;
        _files = files ?? new ExchangeOptionsFileStore();
        _directories = directories ?? new SystemDirectoryAccess();
        _credentials = credentials;
        Fields = OptionsFields.From(OmnimudOptions.Default);
        InheritedFields = Fields;
    }

    public OptionScope Scope { get; }
    public int? ScopeId { get; }
    public string ScopeName { get; }
    public bool IsLoaded { get; private set; }

    /// <summary>Global has nothing above it: no "use the options of..." box.</summary>
    public bool CanInherit => Scope != OptionScope.Global;

    /// <summary>The language belongs to the application, not to a MUD or a character: only editable in Global.</summary>
    public bool CanEditLanguage => Scope == OptionScope.Global;

    /// <summary>True = the scope has no block of its own and follows the level above.</summary>
    public bool UseInherited { get; private set; }

    /// <summary>What the dialog must show right now.</summary>
    public OptionsFields Fields { get; private set; }

    /// <summary>The block of the level above (what the scope gets while it inherits).</summary>
    public OptionsFields InheritedFields { get; private set; }

    public string Title => Scope switch
    {
        OptionScope.Mud => string.Format(Strings.Options_TitleMud, ScopeName),
        OptionScope.Character => string.Format(Strings.Options_TitleCharacter, ScopeName),
        _ => Strings.Options_TitleGlobal
    };

    /// <summary>Text of the "use the options of the level above" box; empty for Global.</summary>
    public string InheritText => Scope switch
    {
        OptionScope.Mud => Strings.Options_InheritGlobal,
        OptionScope.Character => Strings.Options_InheritMud,
        _ => string.Empty
    };

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var inherited = Scope switch
        {
            OptionScope.Character => await _service.ResolveAsync(_parentMudId, null, ct),
            OptionScope.Mud => await _service.ResolveAsync(null, null, ct),
            _ => OmnimudOptions.Default
        };
        InheritedFields = OptionsFields.From(inherited);

        var hasOwn = !CanInherit || await _service.HasOwnOptionsAsync(Scope, ScopeId, ct);
        if (hasOwn)
        {
            _loaded = Scope switch
            {
                OptionScope.Character => await _service.ResolveAsync(_parentMudId, ScopeId, ct),
                OptionScope.Mud => await _service.ResolveAsync(ScopeId, null, ct),
                _ => await _service.ResolveAsync(null, null, ct)
            };
        }
        else
        {
            _loaded = inherited;
        }

        UseInherited = !hasOwn;
        _ownDraft = null;
        Fields = OptionsFields.From(_loaded);
        IsLoaded = true;
    }

    /// <summary>
    /// The user toggled the inherit box. Returns what to show: the inherited block (read-only) when it is
    /// checked; when it is unchecked, what the user had before checking it, or a copy of the inherited block
    /// to start from, as the original client did.
    /// </summary>
    public OptionsFields SetUseInherited(bool inherit, OptionsFields current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!CanInherit) inherit = false;
        if (inherit == UseInherited) return Fields = current;

        if (inherit)
        {
            _ownDraft = current;
            Fields = InheritedFields with { };
        }
        else
        {
            Fields = _ownDraft ?? InheritedFields with { };
            _ownDraft = null;
        }
        UseInherited = inherit;
        return Fields;
    }

    /// <summary>First problem found, in the order of the tabs; null when everything is right.</summary>
    public OptionsValidationError? Validate(OptionsFields f)
    {
        ArgumentNullException.ThrowIfNull(f);

        if (f.HistorySize is < 1 or > 10_000) return Error(OptionsField.HistorySize, Strings.Options_ErrHistorySize);
        if (f.MaxLines is < 100 or > 1_000_000) return Error(OptionsField.MaxLines, Strings.Options_ErrMaxLines);
        if (f.PromptFlushMilliseconds is < 0 or > 60_000) return Error(OptionsField.PromptFlushMilliseconds, Strings.Options_ErrPromptFlush);

        if (f.LogDirectoryEnabled && !string.IsNullOrWhiteSpace(f.LogDirectory) && !IsValidPath(f.LogDirectory.Trim()))
            return Error(OptionsField.LogDirectory, Strings.Options_ErrLogDirectory);

        if (f.Volume is < 0 or > 100) return Error(OptionsField.Volume, Strings.Options_ErrVolume);

        if (f.ProxyType == ProxyMode.Manual)
        {
            if (string.IsNullOrWhiteSpace(f.ProxyHost)) return Error(OptionsField.ProxyHost, Strings.Options_ErrProxyHost);
            if (f.ProxyPort is < 1 or > 65_535) return Error(OptionsField.ProxyPort, Strings.Options_ErrProxyPort);
        }
        else if (f.ProxyPort is < 0 or > 65_535)
        {
            return Error(OptionsField.ProxyPort, Strings.Options_ErrProxyPort);
        }

        if (f.ProxyCredentialsEnabled)
        {
            var user = (f.ProxyUsername ?? string.Empty).Trim();
            var password = f.NewProxyPassword ?? string.Empty;
            if (user.Length == 0 && password.Length > 0)
                return Error(OptionsField.ProxyUsername, Strings.Options_ErrProxyUserMissing);
            // SOCKS5 carries each of them behind a single length byte; HTTP Basic joins them with a colon.
            if (Encoding.UTF8.GetByteCount(user) > 255)
                return Error(OptionsField.ProxyUsername, Strings.Options_ErrProxyUserTooLong);
            var mayBeHttp = f.ProxyType == ProxyMode.Automatic || f.ProxyProtocol == ProxyProtocol.HttpConnect;
            if (mayBeHttp && user.Contains(':'))
                return Error(OptionsField.ProxyUsername, Strings.Options_ErrProxyUserColon);
            if (Encoding.UTF8.GetByteCount(password) > 255)
                return Error(OptionsField.ProxyPasswordProtected, Strings.Options_ErrProxyPasswordTooLong);
            if (password.Length > 0 && _credentials is null)
                return Error(OptionsField.ProxyPasswordProtected, Strings.Options_ErrProxyPasswordNoStore);
        }

        if (string.IsNullOrWhiteSpace(f.FontFamily)) return Error(OptionsField.FontFamily, Strings.Options_ErrFontFamily);
        if (float.IsNaN(f.FontSize) || f.FontSize < 4f || f.FontSize > 200f) return Error(OptionsField.FontSize, Strings.Options_ErrFontSize);

        if (f.UseConcatChar)
        {
            if (f.ConcatChar is not { Length: 1 }) return Error(OptionsField.ConcatChar, Strings.Options_ErrConcatMissing);
            if (!IsUsableCharacter(f.ConcatChar[0])) return Error(OptionsField.ConcatChar, Strings.Options_ErrConcatInvalid);
        }
        if (f.UseRepeatChar)
        {
            if (f.RepeatChar is not { Length: 1 }) return Error(OptionsField.RepeatChar, Strings.Options_ErrRepeatMissing);
            if (!IsUsableCharacter(f.RepeatChar[0])) return Error(OptionsField.RepeatChar, Strings.Options_ErrRepeatInvalid);
        }
        if (f.UseConcatChar && f.UseRepeatChar && f.ConcatChar == f.RepeatChar)
            return Error(OptionsField.RepeatChar, Strings.Options_ErrSameChars);

        return null;
    }

    /// <summary>
    /// OK was pressed. Inheriting: the scope's own block is removed. Otherwise: validate (message + field to
    /// focus), offer to create a missing log folder, store the complete block and, if the language changed,
    /// tell the user it needs a restart.
    /// </summary>
    public async Task<OptionsAcceptResult> AcceptAsync(OptionsFields current, IUserPrompts prompts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(prompts);

        if (CanInherit && UseInherited)
        {
            await _service.ResetToInheritedAsync(Scope, ScopeId, ct);
            return OptionsAcceptResult.Done;
        }

        if (Validate(current) is { } error)
        {
            prompts.Warn(error.Message, Title);
            return OptionsAcceptResult.Stay(error.Field);
        }

        if (!EnsureLogDirectory(current, prompts))
            return OptionsAcceptResult.Stay(OptionsField.LogDirectory);

        var options = current.ToOptions(_loaded);
        // A new password is protected here and nowhere else: the clear text goes no further than this line.
        if (current.ProxyCredentialsEnabled && !string.IsNullOrEmpty(current.NewProxyPassword) && _credentials is not null)
            options = _credentials.WithPassword(options, current.NewProxyPassword);
        var languageChanged = CanEditLanguage && !string.Equals(options.Language, _loaded.Language, StringComparison.OrdinalIgnoreCase);

        await _service.SaveAsync(Scope, ScopeId, options, ct);
        _loaded = options;
        Fields = OptionsFields.From(options);

        if (languageChanged)
            prompts.Info(Strings.Options_LanguageRestart, Title);
        return OptionsAcceptResult.Done;
    }

    /// <summary>Exports what is on screen (validated first). Returns the field to focus when it cannot, else null.</summary>
    public async Task<OptionsField?> ExportAsync(OptionsFields current, IUserPrompts prompts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(prompts);

        var inheriting = CanInherit && UseInherited;
        if (!inheriting && Validate(current) is { } error)
        {
            prompts.Warn(error.Message, Title);
            return error.Field;
        }

        var path = prompts.PickSaveFile(Strings.Options_ExportTitle, Strings.Options_FileFilter,
            Strings.Options_ExportFileName + IExchangeService.FileExtension);
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            // The proxy password never leaves this installation, not even protected.
            var exported = (inheriting ? InheritedFields : current).ToOptions(_loaded) with { ProxyPasswordProtected = null };
            await _files.SaveAsync(path, exported, ct);
            prompts.Info(Strings.Options_Exported, Title);
        }
        catch (OptionsFileException ex)
        {
            prompts.Error(string.Format(Strings.Options_ErrExport, ex.Message), Title);
        }
        return null;
    }

    /// <summary>
    /// Loads a file INTO the dialog: nothing is stored until OK. The scope stops inheriting (an import is an edit).
    /// Returns the fields to show, or null when nothing was imported. The language of the file is ignored
    /// where the language cannot be edited.
    /// </summary>
    public async Task<OptionsFields?> ImportAsync(OptionsFields current, IUserPrompts prompts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(prompts);

        var path = prompts.PickOpenFile(Strings.Options_ImportTitle, Strings.Options_FileFilter);
        if (string.IsNullOrWhiteSpace(path)) return null;

        OmnimudOptions imported;
        try
        {
            imported = await _files.LoadAsync(path, ct);
        }
        catch (OptionsFileException ex)
        {
            prompts.Error(string.Format(Strings.Options_ErrImport, ex.Message), Title);
            return null;
        }

        var fields = OptionsFields.From(imported);
        if (!CanEditLanguage) fields.Language = current.Language;
        // A file brings no password: what the dialog had (stored, typed or marked for deletion) stays.
        fields.ProxyPasswordProtected = current.ProxyPasswordProtected;
        fields.NewProxyPassword = current.NewProxyPassword;
        fields.ClearProxyPassword = current.ClearProxyPassword;

        UseInherited = false;
        _ownDraft = null;
        Fields = fields;
        prompts.Info(Strings.Options_Imported, Title);
        return fields;
    }

    private bool EnsureLogDirectory(OptionsFields f, IUserPrompts prompts)
    {
        if (!f.LogDirectoryEnabled || string.IsNullOrWhiteSpace(f.LogDirectory)) return true;
        var path = f.LogDirectory.Trim();
        if (_directories.Exists(path)) return true;
        if (!prompts.Confirm(Strings.Options_ConfirmCreateDirectory, Title)) return false;

        try
        {
            _directories.Create(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            prompts.Error(string.Format(Strings.Options_ErrCreateDirectory, ex.Message), Title);
            return false;
        }
    }

    private static OptionsValidationError Error(OptionsField field, string message) => new(field, message);

    /// <summary>One visible symbol or letter: digits would be ambiguous with repeat counts, blanks cannot be seen.</summary>
    private static bool IsUsableCharacter(char c) =>
        !char.IsDigit(c) && !char.IsWhiteSpace(c) && !char.IsControl(c) && !char.IsSurrogate(c);

    private static bool IsValidPath(string path)
    {
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        try
        {
            _ = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
