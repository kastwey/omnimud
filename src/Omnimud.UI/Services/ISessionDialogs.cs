using Omnimud.Core.Session;

namespace Omnimud.UI.Services;

/// <summary>
/// The management dialogs the game window can open. Behind an interface so the window does not
/// know about repositories and can be tested with a fake.
/// Every method returns true when something may have changed and the session should reload.
/// </summary>
public interface ISessionDialogs
{
    bool ShowAliases(IWin32Window owner, SessionProfile profile);
    bool ShowTriggers(IWin32Window owner, SessionProfile profile);
    bool ShowPaths(IWin32Window owner, SessionProfile profile);
    /// <summary>Add-path dialog prefilled with a recorded path.</summary>
    bool ShowNewPath(IWin32Window owner, SessionProfile profile, string recordedPath);
    /// <summary>Options at character level when the session has a character, else at MUD level, else global.</summary>
    bool ShowOptions(IWin32Window owner, SessionProfile profile);
    bool ShowMovementKeys(IWin32Window owner, SessionProfile profile);
    bool ShowDirections(IWin32Window owner, SessionProfile profile);
}
