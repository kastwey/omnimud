using System.Text;
using Omnimud.Core.Resources;

namespace Omnimud.Core.Reports;

/// <summary>
/// Composes the text of an error report or a suggestion. Pure. It only knows what it is given
/// (<see cref="ReportRequest"/>), and everything that did not come from the user's own keyboard — diagnostic
/// data and exception details — goes through <see cref="ReportSanitizer"/>.
/// </summary>
public sealed class ReportBuilder
{
    public const int DefaultMaxStackLength = 6000;
    private const int MaxTitleLength = 80;
    private const int MaxMessageLength = 1000;

    private readonly ReportSanitizer _sanitizer;
    private readonly int _maxStackLength;

    public ReportBuilder(ReportSanitizer sanitizer, int maxStackLength = DefaultMaxStackLength)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentOutOfRangeException.ThrowIfNegative(maxStackLength);
        _sanitizer = sanitizer;
        _maxStackLength = maxStackLength;
    }

    public Report Build(ReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var description = (request.Description ?? string.Empty).ReplaceLineEndings(Environment.NewLine).Trim();

        var body = new StringBuilder();
        body.AppendLine(Strings.Report_SectionDescription);
        body.AppendLine(description.Length == 0 ? Strings.Report_NoDescription : description);

        if (request.Diagnostics is { } d)
        {
            body.AppendLine();
            body.AppendLine(Strings.Report_SectionDiagnostics);
            foreach (var (label, value) in new[]
                     {
                         (Strings.Report_DiagVersion, d.AppVersion), (Strings.Report_DiagWindows, d.WindowsVersion),
                         (Strings.Report_DiagDotNet, d.DotNetVersion), (Strings.Report_DiagArchitecture, d.Architecture),
                         (Strings.Report_DiagLanguage, d.UiLanguage), (Strings.Report_DiagScreenReader, d.ScreenReaderMode)
                     })
                body.Append("- ").AppendLine(string.Format(label, SingleLine(_sanitizer.Sanitize(value))));
        }

        if (request.Exception is { } exception)
        {
            body.AppendLine();
            body.AppendLine(Strings.Report_SectionException);
            AppendException(body, exception);
        }

        return new Report(request.Kind, Title(request, description), body.ToString().TrimEnd());
    }

    private void AppendException(StringBuilder body, ExceptionInfo exception)
    {
        var stackBudget = _maxStackLength;
        for (var current = exception; current is not null; current = current.Inner)
        {
            if (!ReferenceEquals(current, exception))
                body.AppendLine(Strings.Report_InnerException);
            body.AppendLine(string.Format(Strings.Report_ExceptionType, SingleLine(_sanitizer.Sanitize(current.TypeName))));
            body.AppendLine(string.Format(Strings.Report_ExceptionMessage, Cut(SingleLine(_sanitizer.Sanitize(current.Message)), MaxMessageLength)));

            var stack = _sanitizer.Sanitize(current.StackTrace).ReplaceLineEndings(Environment.NewLine).Trim();
            if (stack.Length == 0) continue;
            body.AppendLine(Strings.Report_StackTrace);
            body.AppendLine("```");
            if (stack.Length > stackBudget)
            {
                body.AppendLine(stack[..SafeCut(stack, stackBudget)]);
                body.AppendLine(string.Format(Strings.Report_StackCut, stack.Length - stackBudget));
                stackBudget = 0;
            }
            else
            {
                body.AppendLine(stack);
                stackBudget -= stack.Length;
            }
            body.AppendLine("```");
        }
    }

    private string Title(ReportRequest request, string description)
    {
        var format = request.Kind == ReportKind.Error ? Strings.Report_TitleError : Strings.Report_TitleSuggestion;
        var firstLine = description.Split('\n', 2)[0].Trim();
        if (firstLine.Length == 0 && request.Exception is { } exception)
            firstLine = SingleLine(_sanitizer.Sanitize($"{ShortTypeName(exception.TypeName)}: {exception.Message}"));
        if (firstLine.Length == 0)
            firstLine = request.Kind == ReportKind.Error ? Strings.Report_UntitledError : Strings.Report_UntitledSuggestion;
        return string.Format(format, Cut(firstLine, MaxTitleLength));
    }

    private static string ShortTypeName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    private static string SingleLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..SafeCut(text, max - 1)] + "…";

    /// <summary>Never cuts between the two halves of a surrogate pair.</summary>
    internal static int SafeCut(string text, int length)
    {
        length = Math.Clamp(length, 0, text.Length);
        return length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
    }
}
