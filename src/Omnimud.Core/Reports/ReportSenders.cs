using Omnimud.Core.Resources;

namespace Omnimud.Core.Reports;

/// <summary>Opens an address with the program Windows has for it (browser, mail client). Tests use a fake: nothing is opened.</summary>
public interface IExternalLauncher
{
    /// <summary>False when the address was refused or nothing could open it.</summary>
    bool Open(Uri address);
}

public enum ReportChannel
{
    GitHubIssue,
    Email
}

/// <summary>A report turned into the address that will be opened.</summary>
/// <param name="Truncated">The report did not fit in the link: the address carries a cut version with a notice.
/// The caller must put the full text on the clipboard and tell the user BEFORE opening it.</param>
public sealed record ComposedReport(Uri Target, bool Truncated);

/// <summary>
/// "Sends" a report in the only way Omnimud ever does: by opening something already filled in, for the user to
/// review and send (or not). No implementation talks to any server itself.
/// </summary>
public interface IReportSender
{
    ReportChannel Channel { get; }

    /// <summary>Pure: builds the address, opens nothing.</summary>
    /// <param name="signature">Optional signature the user agreed to add (name and e-mail address); only the e-mail channel uses it.</param>
    ComposedReport Compose(Report report, string? signature = null);

    /// <summary>Starts the browser or the mail client. False when nothing could be opened. Sending is always the user's own act there.</summary>
    bool Open(ComposedReport composed);
}

public static class ReportDestinations
{
    /// <summary>New issue form of the project.</summary>
    public static Uri NewIssue { get; } = new("https://github.com/kastwey/omnimud/issues/new");

    /// <summary>Where reports by e-mail go: the author's address. Change it here if it moves.</summary>
    public const string AuthorEmail = "juanjo@jmontiel.es";
}

/// <summary>Shared by the senders: fit a title and a body into an address of limited length.</summary>
internal static class ReportLink
{
    /// <summary>
    /// Builds <c>prefix + titleParameter=title&amp;body=body</c> with everything percent-encoded. When it does not fit
    /// in <paramref name="maxLength"/> the END of the body is cut (that is where the stack trace is) and a notice
    /// says the full report is on the clipboard.
    /// </summary>
    public static Uri Build(string prefix, string titleParameter, string title, string body, int maxLength, out bool truncated)
    {
        truncated = false;
        var head = $"{prefix}{titleParameter}={Uri.EscapeDataString(title)}&body=";
        var full = head + Uri.EscapeDataString(body);
        if (full.Length <= maxLength) return new Uri(full);

        truncated = true;
        var notice = "\r\n\r\n" + Strings.Report_Truncated;
        var encodedNotice = Uri.EscapeDataString(notice);
        var budget = maxLength - head.Length - encodedNotice.Length;

        // The encoded length grows with the text, so the longest prefix that fits is found by bisection.
        int low = 0, high = body.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Uri.EscapeDataString(body[..ReportBuilder.SafeCut(body, middle)]).Length <= budget) low = middle;
            else high = middle - 1;
        }
        var kept = body[..ReportBuilder.SafeCut(body, low)].TrimEnd();
        return new Uri(head + Uri.EscapeDataString(kept) + encodedNotice);
    }
}

/// <summary>Opens the "new issue" page of the project in the browser, filled in. The user needs a GitHub account and presses Submit there.</summary>
public sealed class GitHubIssueReportSender(IExternalLauncher launcher) : IReportSender
{
    /// <summary>GitHub refuses addresses a little above 8000 characters; this leaves a margin.</summary>
    public const int MaxUrlLength = 7500;

    public ReportChannel Channel => ReportChannel.GitHubIssue;

    public static Uri BuildUri(Report report, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ReportLink.Build(ReportDestinations.NewIssue.AbsoluteUri + "?", "title", report.Title, report.Body, MaxUrlLength, out truncated);
    }

    /// <summary>A public issue never carries the signature: name and e-mail address are for the e-mail channel only.</summary>
    public ComposedReport Compose(Report report, string? signature = null)
    {
        var uri = BuildUri(report, out var truncated);
        return new ComposedReport(uri, truncated);
    }

    public bool Open(ComposedReport composed) =>
        composed.Target.AbsoluteUri.StartsWith(ReportDestinations.NewIssue.AbsoluteUri + "?", StringComparison.Ordinal) && launcher.Open(composed.Target);
}

/// <summary>Opens the user's mail program with a message to the author already written. The user presses Send there.</summary>
public sealed class EmailReportSender(IExternalLauncher launcher, string recipient = ReportDestinations.AuthorEmail) : IReportSender
{
    /// <summary>mailto: links reach the mail program through the Windows shell, and several mail programs still
    /// refuse links longer than about 2000 characters; a longer report travels on the clipboard.</summary>
    public const int MaxUrlLength = 2000;

    public ReportChannel Channel => ReportChannel.Email;

    public static Uri BuildUri(Report report, string recipient, string? signature, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        recipient = recipient.Trim();
        // The recipient is a constant of the program, never user data; anything that is not a plain address
        // (and could smuggle "?cc=..." headers into the link) is refused outright.
        var at = recipient.IndexOf('@');
        if (at <= 0 || at != recipient.LastIndexOf('@') || at == recipient.Length - 1
            || recipient.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '@' or '.' or '-' or '_' or '+')))
            throw new ArgumentException("The recipient must be a plain e-mail address.", nameof(recipient));
        var body = report.Body.ReplaceLineEndings("\r\n");
        if (!string.IsNullOrWhiteSpace(signature))
            body = $"{body}\r\n\r\n-- \r\n{signature.Trim()}";
        var to = Uri.EscapeDataString(recipient.Trim()).Replace("%40", "@", StringComparison.Ordinal);
        return ReportLink.Build($"mailto:{to}?", "subject", report.Title, body, MaxUrlLength, out truncated);
    }

    public ComposedReport Compose(Report report, string? signature = null)
    {
        var uri = BuildUri(report, recipient, signature, out var truncated);
        return new ComposedReport(uri, truncated);
    }

    public bool Open(ComposedReport composed) =>
        composed.Target.Scheme == Uri.UriSchemeMailto && launcher.Open(composed.Target);
}
