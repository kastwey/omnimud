using System.Web;
using FluentAssertions;
using Omnimud.Core.Reports;

namespace Omnimud.Core.Tests.Reports;

/// <summary>Nothing is opened here: the launcher is a recorder.</summary>
public sealed class ReportSendersTests
{
    private sealed class RecordingLauncher(bool succeeds = true) : IExternalLauncher
    {
        public List<Uri> Opened { get; } = [];

        public bool Open(Uri address)
        {
            Opened.Add(address);
            return succeeds;
        }
    }

    private static readonly Report Small = new(ReportKind.Error, "[Error] Se cuelga al pulsar F5 & más",
        "Descripción:\r\nAl pulsar F5 se cuelga.\r\n¿Un 100% de las veces? Sí: a=b&c=d #hash + más\r\n\r\nTipo: X");

    private static Report Large(int length) => new(ReportKind.Error, "[Error] Big",
        "Description:\nIt broke.\n\nStack trace:\n" + string.Join("\n", Enumerable.Range(0, length / 40 + 1).Select(i => $"   at Namespace.Type{i:D5}.Method() línea ñ")));

    private static (string Title, string Body) Decode(Uri uri, string titleParameter)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return (query[titleParameter]!, query["body"]!);
    }

    // ── GitHub ─────────────────────────────────────────────────────────────

    [Fact]
    public void GitHub_OpensTheNewIssuePageOfTheProject_OverHttps()
    {
        var uri = GitHubIssueReportSender.BuildUri(Small, out var truncated);

        truncated.Should().BeFalse();
        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("github.com");
        uri.AbsolutePath.Should().Be("/kastwey/omnimud/issues/new");
    }

    [Fact]
    public void GitHub_TitleAndBody_SurviveTheEncodingExactly()
    {
        var uri = GitHubIssueReportSender.BuildUri(Small, out _);

        var (title, body) = Decode(uri, "title");
        title.Should().Be(Small.Title);
        body.Should().Be(Small.Body);
        // Only two parameters: '&', '=', '#' and '+' of the text did not leak into the query.
        HttpUtility.ParseQueryString(uri.Query).AllKeys.Should().BeEquivalentTo("title", "body");
        uri.Fragment.Should().BeEmpty();
        uri.Query.Should().NotContain(" ").And.NotContain("\n");
    }

    [Fact]
    public void GitHub_LongReport_IsCutToTheLimit_WithANotice_AndSaysSo()
    {
        var report = Large(30_000);

        var uri = GitHubIssueReportSender.BuildUri(report, out var truncated);

        truncated.Should().BeTrue();
        uri.AbsoluteUri.Length.Should().BeLessThanOrEqualTo(GitHubIssueReportSender.MaxUrlLength);
        uri.OriginalString.Length.Should().BeLessThanOrEqualTo(GitHubIssueReportSender.MaxUrlLength);
        var (title, body) = Decode(uri, "title");
        title.Should().Be(report.Title);
        body.Should().StartWith("Description:\nIt broke.", "the description is at the beginning and survives; the stack is what gets cut");
        body.Should().Contain("Type00000").And.NotContain("Type00700");
        body.Should().EndWith("]", "the notice about the clipboard closes the text");
        body.Length.Should().BeGreaterThan(1500, "as much as fits is kept");
    }

    [Fact]
    public void GitHub_ReportJustUnderTheLimit_IsNotCut()
    {
        var body = new string('a', 7000);
        var uri = GitHubIssueReportSender.BuildUri(new Report(ReportKind.Suggestion, "T", body), out var truncated);

        truncated.Should().BeFalse();
        Decode(uri, "title").Body.Should().Be(body);
    }

    [Fact]
    public void GitHub_CuttingNeverBreaksAnEmoji()
    {
        var report = new Report(ReportKind.Error, "T", string.Concat(Enumerable.Repeat("😀", 3000)));

        var act = () => GitHubIssueReportSender.BuildUri(report, out _);

        var uri = act.Should().NotThrow().Subject;
        Decode(uri, "title").Body.Should().NotContain("\uFFFD");
    }

    [Fact]
    public void GitHub_NeverCarriesTheSignature()
    {
        var launcher = new RecordingLauncher();
        var sender = new GitHubIssueReportSender(launcher);

        var composed = sender.Compose(Small, "María López <maria@example.org>");
        sender.Open(composed).Should().BeTrue();

        sender.Channel.Should().Be(ReportChannel.GitHubIssue);
        launcher.Opened.Should().ContainSingle().Which.Should().Be(composed.Target);
        Uri.UnescapeDataString(composed.Target.AbsoluteUri).Should().NotContain("maria@example.org").And.NotContain("López");
    }

    [Fact]
    public void GitHub_RefusesToOpenAnythingElse()
    {
        var launcher = new RecordingLauncher();
        var sender = new GitHubIssueReportSender(launcher);

        sender.Open(new ComposedReport(new Uri("https://evil.example/kastwey/omnimud/issues/new?title=x"), false)).Should().BeFalse();
        sender.Open(new ComposedReport(new Uri("file:///C:/Windows/System32/calc.exe"), false)).Should().BeFalse();

        launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public void WhenNothingCanBeOpened_OpenSaysSo()
    {
        var sender = new GitHubIssueReportSender(new RecordingLauncher(succeeds: false));
        sender.Open(sender.Compose(Small)).Should().BeFalse();
    }

    // ── E-mail ─────────────────────────────────────────────────────────────

    [Fact]
    public void Email_IsAMailtoToTheAuthor_WithSubjectAndBody()
    {
        var uri = EmailReportSender.BuildUri(Small, ReportDestinations.AuthorEmail, null, out var truncated);

        truncated.Should().BeFalse();
        uri.Scheme.Should().Be("mailto");
        uri.AbsoluteUri.Should().StartWith($"mailto:{ReportDestinations.AuthorEmail}?subject=");
        var (subject, body) = Decode(new Uri("http://x/" + uri.AbsoluteUri[uri.AbsoluteUri.IndexOf('?')..]), "subject");
        subject.Should().Be(Small.Title);
        body.Should().Be(Small.Body);
    }

    [Fact]
    public void Email_LineBreaksAreCrLf_AndSpacesArePercentEncoded()
    {
        var uri = EmailReportSender.BuildUri(new Report(ReportKind.Error, "A b", "one\ntwo\r\nthree"), "author@example.org", null, out _);

        uri.AbsoluteUri.Should().Contain("subject=A%20b").And.Contain("one%0D%0Atwo%0D%0Athree");
        uri.AbsoluteUri.Should().NotContain("+");
    }

    [Fact]
    public void Email_WithoutSignature_HasNoAddressOfTheUser_AndOnlyTheRecipient()
    {
        var uri = EmailReportSender.BuildUri(Small, "author@example.org", "   ", out _);

        uri.AbsoluteUri.Count(c => c == '@').Should().Be(1);
        uri.AbsoluteUri.Should().NotContain("cc=").And.NotContain("bcc=").And.NotContain("from=");
    }

    [Fact]
    public void Email_Signature_GoesAtTheEndOfTheBody_OnlyWhenGiven()
    {
        var uri = EmailReportSender.BuildUri(Small, "author@example.org", "María López <maria@example.org>", out _);

        var decoded = Uri.UnescapeDataString(uri.AbsoluteUri);
        decoded.Should().EndWith("-- \r\nMaría López <maria@example.org>");
        uri.AbsoluteUri[..uri.AbsoluteUri.IndexOf('?')].Should().Be("mailto:author@example.org", "the address of the user is never a recipient");
    }

    [Fact]
    public void Email_LongReport_IsCutToItsOwnLimit()
    {
        var uri = EmailReportSender.BuildUri(Large(10_000), "author@example.org", null, out var truncated);

        truncated.Should().BeTrue();
        uri.AbsoluteUri.Length.Should().BeLessThanOrEqualTo(EmailReportSender.MaxUrlLength);
        Uri.UnescapeDataString(uri.AbsoluteUri).Should().Contain("It broke.");
    }

    [Theory]
    [InlineData("author@example.org?cc=evil@example.org&x=")]
    [InlineData("author@example.org,evil@example.org")]
    [InlineData("author@example.org&bcc=evil")]
    [InlineData("a b@example.org")]
    [InlineData("author@")]
    [InlineData("@example.org")]
    [InlineData("no-at-sign")]
    [InlineData("author@example.org\r\nCc: evil@example.org")]
    public void Email_StrangeRecipient_IsRefused_SoItCannotInjectHeaders(string recipient)
    {
        var act = () => EmailReportSender.BuildUri(Small, recipient, null, out _);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Email_TheConfiguredRecipient_IsAccepted()
    {
        var act = () => EmailReportSender.BuildUri(Small, ReportDestinations.AuthorEmail, null, out _);
        act.Should().NotThrow();
    }

    [Fact]
    public void Email_SenderOpensOnlyMailto()
    {
        var launcher = new RecordingLauncher();
        var sender = new EmailReportSender(launcher);

        sender.Channel.Should().Be(ReportChannel.Email);
        sender.Open(new ComposedReport(new Uri("https://evil.example/"), false)).Should().BeFalse();
        sender.Open(sender.Compose(Small)).Should().BeTrue();

        launcher.Opened.Should().ContainSingle().Which.Scheme.Should().Be("mailto");
    }

    [Fact]
    public void ComposeIsPure_NothingIsOpenedUntilOpen()
    {
        var launcher = new RecordingLauncher();
        new GitHubIssueReportSender(launcher).Compose(Small);
        new EmailReportSender(launcher).Compose(Small, "x");

        launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public void Destinations_AreTheProjectOnGitHub_AndAPlausibleAddress()
    {
        ReportDestinations.NewIssue.AbsoluteUri.Should().Be("https://github.com/kastwey/omnimud/issues/new");
        ReportDestinations.AuthorEmail.Should().MatchRegex(@"^[^@\s]+@[^@\s]+\.[a-z]{2,}$");
    }
}
