namespace Omnimud.UI.Services;

/// <summary>
/// System dialogs (message boxes, file and folder pickers) behind an interface, so presenters
/// can be tested without a modal window nobody answers. No WinForms type appears here.
/// </summary>
public interface IUserPrompts
{
    /// <summary>Yes/No question. True = yes.</summary>
    bool Confirm(string message, string? title = null);
    void Info(string message, string? title = null);
    void Warn(string message, string? title = null);
    void Error(string message, string? title = null);

    /// <summary>Null when cancelled. <paramref name="filter"/> uses the OpenFileDialog syntax ("Text|*.txt|All|*.*").</summary>
    string? PickOpenFile(string title, string filter);
    /// <summary>Null when cancelled.</summary>
    string? PickSaveFile(string title, string filter, string? defaultFileName = null);
    /// <summary>Null when cancelled.</summary>
    string? PickFolder(string title, string? initialPath = null);
}

/// <summary>The real dialogs. Owned by the given window, or by the active form when none is given.</summary>
public sealed class WinFormsUserPrompts : IUserPrompts
{
    private readonly Func<IWin32Window?> _owner;

    public WinFormsUserPrompts() : this(() => Form.ActiveForm) { }

    public WinFormsUserPrompts(IWin32Window owner) : this(() => owner) { }

    public WinFormsUserPrompts(Func<IWin32Window?> owner) => _owner = owner;

    private IWin32Window? Owner => _owner() is Control { IsDisposed: true } ? null : _owner();

    public bool Confirm(string message, string? title = null) =>
        MessageBox.Show(Owner, message, title ?? Resources.Strings.Common_Confirm, MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public void Info(string message, string? title = null) => Show(message, title, MessageBoxIcon.Information);
    public void Warn(string message, string? title = null) => Show(message, title, MessageBoxIcon.Warning);
    public void Error(string message, string? title = null) => Show(message, title, MessageBoxIcon.Error);

    private void Show(string message, string? title, MessageBoxIcon icon) =>
        MessageBox.Show(Owner, message, title ?? Resources.Strings.App_Title, MessageBoxButtons.OK, icon);

    public string? PickOpenFile(string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string? defaultFileName = null)
    {
        using var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName ?? string.Empty, OverwritePrompt = true };
        return dialog.ShowDialog(Owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialPath = null)
    {
        using var dialog = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dialog.SelectedPath = initialPath;
        return dialog.ShowDialog(Owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
