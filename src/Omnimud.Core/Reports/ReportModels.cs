using System.Runtime.InteropServices;

namespace Omnimud.Core.Reports;

public enum ReportKind
{
    Error,
    Suggestion
}

/// <summary>
/// What is known about the environment, for the author to reproduce a problem. Nothing here identifies the
/// user: no user name, no machine name, no paths, nothing about MUDs or characters.
/// </summary>
public sealed record DiagnosticInfo(
    string AppVersion,
    string WindowsVersion,
    string DotNetVersion,
    string Architecture,
    string UiLanguage,
    string ScreenReaderMode)
{
    public static DiagnosticInfo Collect(string appVersion, string uiLanguage, string screenReaderMode) => new(
        appVersion,
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        $"{RuntimeInformation.ProcessArchitecture} ({RuntimeInformation.OSArchitecture})",
        uiLanguage,
        screenReaderMode);
}

/// <summary>An exception as data: type, message and stack, with its inner exceptions.</summary>
public sealed record ExceptionInfo(string TypeName, string Message, string? StackTrace, ExceptionInfo? Inner = null)
{
    private const int MaxDepth = 5;

    public static ExceptionInfo From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return From(exception, 0);
    }

    private static ExceptionInfo From(Exception exception, int depth)
    {
        var inner = exception is AggregateException { InnerExceptions.Count: > 0 } aggregate ? aggregate.InnerExceptions[0] : exception.InnerException;
        return new ExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            inner is not null && depth < MaxDepth ? From(inner, depth + 1) : null);
    }
}

/// <summary>Everything a report is made from. Note what is NOT here: no session, no MUD, no character, no log.</summary>
public sealed record ReportRequest(ReportKind Kind, string? Description)
{
    /// <summary>Null = the user chose not to include diagnostic data.</summary>
    public DiagnosticInfo? Diagnostics { get; init; }
    public ExceptionInfo? Exception { get; init; }
}

/// <summary>The report as text: exactly what the user sees, may edit, and sends.</summary>
public sealed record Report(ReportKind Kind, string Title, string Body);
