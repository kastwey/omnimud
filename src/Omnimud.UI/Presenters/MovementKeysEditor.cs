using Omnimud.Core.Session;

namespace Omnimud.UI.Presenters;

/// <summary>
/// The rules of the "movement keys" dialog, without any window.
///
/// What a key sends is: the owner's row for that key (an empty command = switched off), else the
/// default for the language. The owner is the character if it has ANY row of its own, else its
/// MUD (the set is inherited as a whole, never mixed: see SessionStore.GetMovementsAsync).
///
/// Baseline = what the owner gets when it has no rows of its own: the MUD's keys completed with
/// the defaults for a character, the plain defaults for a MUD. "Restore defaults" goes back to it.
///
/// Saving (decision):
///  - every box equal to the baseline → NOTHING is stored (rows of the owner are deleted), so a
///    character keeps following its MUD and a MUD keeps following the language defaults;
///  - otherwise the COMPLETE set is stored: every command as shown, plus an empty command for
///    each emptied key that has a default (otherwise the default would come back). What the user
///    saw is what the keys do from then on, whatever happens later to the MUD's keys or to the
///    language of the interface.
/// </summary>
public sealed class MovementKeysEditor
{
    private readonly IReadOnlyDictionary<int, string> _own;
    private readonly IReadOnlyDictionary<int, string> _inherited;
    private readonly IReadOnlyDictionary<int, string> _defaults;

    /// <param name="own">Rows of the owner being edited (character or MUD). May be empty.</param>
    /// <param name="inherited">Rows of the MUD when the owner is a character; empty when editing a MUD.</param>
    /// <param name="defaults">Default commands for the language (<see cref="MovementKeys.DefaultCommands"/>).</param>
    public MovementKeysEditor(
        IReadOnlyDictionary<int, string> own,
        IReadOnlyDictionary<int, string> inherited,
        IReadOnlyDictionary<int, string> defaults)
    {
        _own = own;
        _inherited = inherited;
        _defaults = defaults;
    }

    /// <summary>Command of the key when the owner has no rows of its own; empty if none.</summary>
    public string Baseline(int key) => Resolve(_inherited, key);

    /// <summary>Command the key sends right now (what the box shows when the dialog opens); empty if none.</summary>
    public string Effective(int key) => _own.Count > 0 ? Resolve(_own, key) : Baseline(key);

    /// <summary>What has to be stored for the owner, given the text of every box (key → text).
    /// Empty = delete the owner's rows. See the class remarks for the rule.</summary>
    public IReadOnlyDictionary<int, string> ToSave(IReadOnlyDictionary<int, string> texts)
    {
        string TextOf(int key) => texts.TryGetValue(key, out var text) ? text.Trim() : string.Empty;

        if (MovementKeys.AllCodes.All(key => TextOf(key) == Baseline(key)))
            return new Dictionary<int, string>();

        var result = new Dictionary<int, string>();
        foreach (var key in MovementKeys.AllCodes)
        {
            var text = TextOf(key);
            if (text.Length > 0 || _defaults.ContainsKey(key)) result[key] = text;
        }
        return result;
    }

    private string Resolve(IReadOnlyDictionary<int, string> configured, int key)
    {
        var command = configured.TryGetValue(key, out var own) ? own
            : _defaults.TryGetValue(key, out var byDefault) ? byDefault
            : string.Empty;
        return command.Trim();
    }
}
