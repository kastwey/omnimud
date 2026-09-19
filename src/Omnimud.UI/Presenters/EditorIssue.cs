namespace Omnimud.UI.Presenters;

/// <summary>
/// A validation error of an editor dialog: which field is wrong (so the form can focus it),
/// the localized message, and for scripts the 1-based line where the cursor must go.
/// </summary>
public sealed record EditorIssue(string Field, string Message, int? Line = null);
