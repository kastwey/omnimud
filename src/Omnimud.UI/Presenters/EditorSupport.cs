using System.Text;

namespace Omnimud.UI.Presenters;

/// <summary>A validation or save problem: the message to show and the field that must get the focus.</summary>
public sealed record FieldError<TField>(TField Field, string Message) where TField : struct, Enum;

/// <summary>One entry of a combo: the value that is stored and the text that is shown.</summary>
public sealed record Choice<TValue>(TValue Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>Encodings offered for a MUD, and the check for the ones typed by hand.</summary>
public static class MudEncodings
{
    static MudEncodings()
    {
        // windows-1252, iso-8859-x... are not available in modern .NET without this. Idempotent.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public const string Default = "utf-8";

    public static IReadOnlyList<string> Common { get; } = ["utf-8", "iso-8859-1", "windows-1252", "ascii"];

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            Encoding.GetEncoding(name.Trim());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
