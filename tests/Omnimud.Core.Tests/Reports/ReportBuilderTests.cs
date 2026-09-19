using FluentAssertions;
using Omnimud.Core.Reports;

namespace Omnimud.Core.Tests.Reports;

public sealed class ReportBuilderTests
{
    private const string Profile = @"C:\Users\maria.lopez";

    private static readonly DiagnosticInfo Diagnostics =
        new("2.0.0", "Microsoft Windows 10.0.26200", ".NET 10.0.0", "X64 (X64)", "es", "Nvda");

    private static ReportSanitizer Sanitizer(params string[] privateTerms) =>
        new("maria.lopez", Profile, "PC-DE-MARIA", privateTerms);

    private static ReportBuilder Builder(params string[] privateTerms) => new(Sanitizer(privateTerms));

    private static ExceptionInfo Thrown(string message, Exception? inner = null)
    {
        try
        {
            throw new InvalidOperationException(message, inner);
        }
        catch (Exception ex)
        {
            return ExceptionInfo.From(ex);
        }
    }

    // ── Content ────────────────────────────────────────────────────────────

    [Fact]
    public void Report_CarriesDescriptionAndEveryDiagnosticValue()
    {
        var report = Builder().Build(new ReportRequest(ReportKind.Error, "The window froze\r\nwhen I pressed F5.") { Diagnostics = Diagnostics });

        report.Kind.Should().Be(ReportKind.Error);
        report.Title.Should().Contain("The window froze").And.NotContain("F5", "only the first line goes to the title");
        report.Body.Should().Contain("The window froze").And.Contain("when I pressed F5.");
        foreach (var value in new[] { "2.0.0", "Microsoft Windows 10.0.26200", ".NET 10.0.0", "X64 (X64)", "es", "Nvda" })
            report.Body.Should().Contain(value);
    }

    [Fact]
    public void WithoutDiagnostics_NoDiagnosticValueAppears()
    {
        var report = Builder().Build(new ReportRequest(ReportKind.Suggestion, "More sounds, please."));

        report.Kind.Should().Be(ReportKind.Suggestion);
        report.Body.Should().Contain("More sounds, please.");
        report.Body.Should().NotContain("Windows").And.NotContain(".NET").And.NotContain("X64").And.NotContain("Nvda");
    }

    [Fact]
    public void ErrorAndSuggestion_HaveDifferentTitles()
    {
        var error = Builder().Build(new ReportRequest(ReportKind.Error, "Same text"));
        var suggestion = Builder().Build(new ReportRequest(ReportKind.Suggestion, "Same text"));

        error.Title.Should().NotBe(suggestion.Title);
        error.Title.Should().Contain("Same text");
        suggestion.Title.Should().Contain("Same text");
    }

    [Fact]
    public void Exception_AppearsWithTypeMessageStackAndInnerException()
    {
        var exception = Thrown("Outer failure", new FormatException("Inner failure"));

        var report = Builder().Build(new ReportRequest(ReportKind.Error, null) { Exception = exception });

        report.Body.Should().Contain("System.InvalidOperationException").And.Contain("Outer failure");
        report.Body.Should().Contain("System.FormatException").And.Contain("Inner failure");
        report.Body.Should().Contain(nameof(Thrown), "the stack trace is included");
        report.Title.Should().Contain("InvalidOperationException").And.Contain("Outer failure");
    }

    [Fact]
    public void EmptyDescription_IsSaidSo_AndTheTitleIsNeverEmpty()
    {
        foreach (var kind in Enum.GetValues<ReportKind>())
        {
            var report = Builder().Build(new ReportRequest(kind, "   "));
            report.Title.Trim().Length.Should().BeGreaterThan(5);
            report.Body.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void LongFirstLine_IsCutInTheTitle_ButKeptWholeInTheBody()
    {
        var line = string.Join(' ', Enumerable.Repeat("word", 100));
        var report = Builder().Build(new ReportRequest(ReportKind.Error, line));

        report.Title.Length.Should().BeLessThan(110);
        report.Title.Should().EndWith("…");
        report.Body.Should().Contain(line);
    }

    [Fact]
    public void AggregateException_ShowsItsFirstInnerException()
    {
        var info = ExceptionInfo.From(new AggregateException(new TimeoutException("too slow")));
        info.Inner!.TypeName.Should().Be("System.TimeoutException");
        info.Inner.Message.Should().Be("too slow");
    }

    [Fact]
    public void EndlessChainOfInnerExceptions_IsCut()
    {
        Exception exception = new InvalidOperationException("0");
        for (var i = 1; i < 50; i++) exception = new InvalidOperationException(i.ToString(), exception);

        var depth = 0;
        for (var info = ExceptionInfo.From(exception); info is not null; info = info.Inner) depth++;

        depth.Should().BeLessThanOrEqualTo(6);
    }

    // ── Sanitizing ─────────────────────────────────────────────────────────

    [Fact]
    public void PathsInsideTheProfile_LoseTheUserName()
    {
        var exception = new ExceptionInfo("System.IO.FileNotFoundException",
            @"Could not find file 'C:\Users\maria.lopez\Juegos\Omnimud\data\omnimud.db'.",
            @"   at Omnimud.Data.Foo() in C:\Users\maria.lopez\src\omnimud\Foo.cs:line 12" + "\n" +
            @"   at Omnimud.Data.Bar() in c:/users/MARIA.LOPEZ/src/omnimud/Bar.cs:line 40");

        var report = Builder().Build(new ReportRequest(ReportKind.Error, "It failed") { Exception = exception, Diagnostics = Diagnostics });

        report.Body.Should().NotContainEquivalentOf("maria.lopez");
        report.Body.Should().Contain(@"%USERPROFILE%\Juegos\Omnimud\data\omnimud.db");
        report.Body.Should().Contain(@"%USERPROFILE%\src\omnimud\Foo.cs:line 12");
        report.Body.Should().Contain("%USERPROFILE%/src/omnimud/Bar.cs:line 40");
        report.Title.Should().NotContainEquivalentOf("maria.lopez");
    }

    [Fact]
    public void ProfilesOfOtherAccounts_AndOtherDrives_AreHiddenToo()
    {
        var sanitizer = Sanitizer();
        sanitizer.Sanitize(@"D:\Users\build agent\work\x.cs").Should().Be(@"%USERPROFILE%\work\x.cs");
        sanitizer.Sanitize(@"C:\Usuarios\pepe\x.cs").Should().Be(@"%USERPROFILE%\x.cs");
        sanitizer.Sanitize(@"C:\Documents and Settings\pepe\x.cs").Should().Be(@"%USERPROFILE%\x.cs");
    }

    [Fact]
    public void UserNameAndMachineName_AsWords_AreReplaced()
    {
        var text = Sanitizer().Sanitize(@"Access denied for PC-DE-MARIA\maria.lopez (maria.lopez@PC-DE-MARIA)");

        text.Should().NotContainEquivalentOf("maria.lopez").And.NotContainEquivalentOf("PC-DE-MARIA");
        text.Should().Contain(ReportSanitizer.UserPlaceholder).And.Contain(ReportSanitizer.MachinePlaceholder);
    }

    [Fact]
    public void UserName_InsideALongerWord_IsLeftAlone()
    {
        var sanitizer = new ReportSanitizer("ana", @"C:\Users\ana");
        sanitizer.Sanitize("The banana is analog; ana is not.").Should().Be($"The banana is analog; {ReportSanitizer.UserPlaceholder} is not.");
    }

    [Fact]
    public void ProfileInTheRootOfADrive_DoesNotEatEveryPath()
    {
        var sanitizer = new ReportSanitizer("x", @"C:\");
        sanitizer.Sanitize(@"C:\Program Files\Omnimud\Omnimud.exe").Should().Be(@"C:\Program Files\Omnimud\Omnimud.exe");
    }

    [Fact]
    public void NullEmptyAndUnknownValues_AreHarmless()
    {
        var sanitizer = new ReportSanitizer(null, null, null, [null!, "", " ", "a"]);
        sanitizer.Sanitize(null).Should().BeEmpty();
        sanitizer.Sanitize("").Should().BeEmpty();
        sanitizer.Sanitize("a b c").Should().Be("a b c", "one-letter terms would shred the text");
    }

    [Fact]
    public void ForThisMachine_HidesTheRealProfileAndUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = ReportSanitizer.ForThisMachine().Sanitize($@"in {profile}\src\File.cs:line 3");

        text.Should().Be(@"in %USERPROFILE%\src\File.cs:line 3");
    }

    /// <summary>
    /// The builder has no access to the session, so MUD data can only arrive inside an exception message
    /// (a connection error names the host). The terms the application knows are taken out.
    /// </summary>
    [Fact]
    public void MudHostAndCharacterName_NeverAppear_EvenWhenTheExceptionMentionsThem()
    {
        var exception = new ExceptionInfo("System.Net.Sockets.SocketException",
            "No such host is known (mud.reinosdeleyenda.es:23) while logging in Aldara",
            "   at Omnimud.Core.Connection.TelnetConnection.ConnectAsync(String host) host=MUD.ReinosDeLeyenda.es character=aldara");

        var report = Builder("mud.reinosdeleyenda.es", "Aldara", "Reinos de Leyenda")
            .Build(new ReportRequest(ReportKind.Error, null) { Exception = exception, Diagnostics = Diagnostics });

        var everything = report.Title + "\n" + report.Body;
        everything.Should().NotContainEquivalentOf("reinosdeleyenda").And.NotContainEquivalentOf("aldara");
        everything.Should().Contain(ReportSanitizer.RedactedPlaceholder);
        everything.Should().Contain("SocketException").And.Contain("TelnetConnection.ConnectAsync");
    }

    [Fact]
    public void TheRequest_HasNoPlaceForSessionData()
    {
        // A guard for the future: if someone adds a session, a log or a password to the request, this fails and makes them think.
        typeof(ReportRequest).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("Kind", "Description", "Diagnostics", "Exception");
        typeof(DiagnosticInfo).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("AppVersion", "WindowsVersion", "DotNetVersion", "Architecture", "UiLanguage", "ScreenReaderMode");
    }

    [Fact]
    public void CollectedDiagnostics_DoNotNameTheUserOrTheMachine()
    {
        var info = DiagnosticInfo.Collect("2.0.0", "es", "Automatic");
        var text = info.ToString();

        text.Should().Contain("2.0.0").And.Contain("Windows");
        if (Environment.UserName.Length >= 3) text.Should().NotContainEquivalentOf(Environment.UserName);
        if (Environment.MachineName.Length >= 3) text.Should().NotContainEquivalentOf(Environment.MachineName);
    }

    // ── Truncating ─────────────────────────────────────────────────────────

    [Fact]
    public void HugeStack_IsCutWithANotice()
    {
        var stack = string.Join("\n", Enumerable.Range(0, 2000).Select(i => $"   at Some.Namespace.Type{i}.Method()"));
        var exception = new ExceptionInfo("System.StackOverflowException", "deep", stack);

        var report = new ReportBuilder(Sanitizer(), maxStackLength: 500).Build(new ReportRequest(ReportKind.Error, "x") { Exception = exception });

        report.Body.Length.Should().BeLessThan(1200);
        report.Body.Should().Contain("Type0.Method").And.NotContain("Type1999.Method");
        report.Body.Should().Contain((stack.ReplaceLineEndings(Environment.NewLine).Trim().Length - 500).ToString(), "the notice says how much was left out");
    }

    [Fact]
    public void StackBudget_IsSharedByInnerExceptions()
    {
        var stack = new string('s', 400);
        var exception = new ExceptionInfo("A", "a", stack, new ExceptionInfo("B", "b", stack, new ExceptionInfo("C", "c", stack)));

        var report = new ReportBuilder(Sanitizer(), maxStackLength: 500).Build(new ReportRequest(ReportKind.Error, "x") { Exception = exception });

        report.Body.Count(c => c == 's').Should().BeLessThanOrEqualTo(500 + "x".Length + 50);
    }

    [Fact]
    public void HugeMessage_IsCut()
    {
        var exception = new ExceptionInfo("A", new string('m', 50_000), null);
        var report = Builder().Build(new ReportRequest(ReportKind.Error, "x") { Exception = exception });
        report.Body.Length.Should().BeLessThan(2000);
    }

    [Fact]
    public void Cutting_NeverSplitsASurrogatePair()
    {
        var line = new string('a', 78) + "😀😀😀";
        var report = Builder().Build(new ReportRequest(ReportKind.Suggestion, line));

        foreach (var (c, i) in report.Title.Select((c, i) => (c, i)))
        {
            if (char.IsHighSurrogate(c)) char.IsLowSurrogate(report.Title[i + 1]).Should().BeTrue();
        }
        var act = () => Uri.EscapeDataString(report.Title);
        act.Should().NotThrow();
    }
}
