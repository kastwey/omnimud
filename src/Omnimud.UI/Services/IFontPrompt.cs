namespace Omnimud.UI.Services;

/// <summary>A font as the options store it.</summary>
public sealed record FontChoice(string Family, float Size);

/// <summary>The system font dialog behind an interface (companion of <see cref="IUserPrompts"/>).</summary>
public interface IFontPrompt
{
    /// <summary>Null when cancelled.</summary>
    FontChoice? PickFont(FontChoice current);
}

/// <summary>The real font dialog: no colour, no effects, no script, as in the original client.</summary>
public sealed class WinFormsFontPrompt : IFontPrompt
{
    private readonly Func<IWin32Window?> _owner;

    public WinFormsFontPrompt() : this(() => Form.ActiveForm) { }

    public WinFormsFontPrompt(IWin32Window owner) : this(() => owner) { }

    public WinFormsFontPrompt(Func<IWin32Window?> owner) => _owner = owner;

    public FontChoice? PickFont(FontChoice current)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var dialog = new FontDialog
        {
            ShowColor = false,
            ShowEffects = false,
            AllowScriptChange = false,
            AllowVerticalFonts = false,
            FontMustExist = true,
            MinSize = 4,
            MaxSize = 200
        };

        Font? initial = null;
        try
        {
            initial = new Font(current.Family, Math.Clamp(current.Size, 4f, 200f));
            dialog.Font = initial;
        }
        catch (ArgumentException)
        {
            // Unknown family: the dialog opens with its default font.
        }

        try
        {
            var owner = _owner() is Control { IsDisposed: true } ? null : _owner();
            return dialog.ShowDialog(owner) == DialogResult.OK
                ? new FontChoice(dialog.Font.FontFamily.Name, dialog.Font.SizeInPoints)
                : null;
        }
        finally
        {
            initial?.Dispose();
        }
    }
}
