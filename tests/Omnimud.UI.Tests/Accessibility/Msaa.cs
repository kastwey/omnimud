using System.Runtime.InteropServices;
using Accessibility;

namespace Omnimud.UI.Tests.Accessibility;

/// <summary>Reads a window through MSAA (IAccessible), which is how NVDA and JAWS look at WinForms edit boxes.</summary>
internal static class Msaa
{
    public const int RoleText = 42;       // ROLE_SYSTEM_TEXT: editable text
    public const int RoleDocument = 15;   // ROLE_SYSTEM_DOCUMENT
    public const int StateReadOnly = 0x40;

    private const uint ObjIdClient = 0xFFFFFFFC;
    private static Guid _iidAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    public sealed record Info(string? Name, int Role, int State, string? Value, string? Description);

    public static Info Read(IntPtr hwnd)
    {
        var hr = AccessibleObjectFromWindow(hwnd, ObjIdClient, ref _iidAccessible, out var obj);
        if (hr != 0 || obj is not IAccessible acc)
            throw new InvalidOperationException($"AccessibleObjectFromWindow failed: 0x{hr:X8}");

        const int self = 0;
        return new Info(
            Try(() => acc.get_accName(self)),
            Try(() => acc.get_accRole(self)) is int role ? role : -1,
            Try(() => acc.get_accState(self)) is int state ? state : 0,
            Try(() => acc.get_accValue(self)),
            Try(() => acc.get_accDescription(self)));
    }

    private static T? Try<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is COMException or NotImplementedException or ArgumentException) { return default; }
    }
}
