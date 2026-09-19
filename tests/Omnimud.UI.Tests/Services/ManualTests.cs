using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Session;
using Omnimud.Core.Sound;
using Omnimud.UI.Forms;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;
using Omnimud.UI.Services.Accessibility;
using Omnimud.UI.Tests.Presenters;

namespace Omnimud.UI.Tests.Services;

/// <summary>
/// The user manual (docs/manual/manual.es.html and manual.en.html): it exists in both languages, it is accessible
/// in the basics, and its table of shortcuts says exactly what the menus of the game window do, so the manual
/// cannot drift away from the program without a test failing.
/// </summary>
public sealed class ManualTests
{
    public static TheoryData<string> Languages() => new() { "es", "en" };

    private static string PathOf(string language) =>
        Path.Combine(HelpAndAppInfoTests.RepositoryRoot(), "docs", "manual", HelpService.ManualFileName(language));

    /// <summary>The manual is written as well-formed XML (XHTML syntax), so a plain XML parser is enough.</summary>
    private static XDocument Load(string language)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(PathOf(language), settings);
        return XDocument.Load(reader, LoadOptions.SetLineInfo);
    }

    private static IEnumerable<XElement> All(XDocument document, string name) =>
        document.Descendants().Where(e => e.Name.LocalName == name);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_Exists_AndIsWellFormed(string language)
    {
        File.Exists(PathOf(language)).Should().BeTrue();
        var act = () => Load(language);
        act.Should().NotThrow();
        File.ReadAllText(PathOf(language)).Should().StartWith("<!DOCTYPE html>");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_DeclaresItsLanguage_AndHasTitleAndCharset(string language)
    {
        var document = Load(language);

        document.Root!.Name.LocalName.Should().Be("html");
        document.Root.Attribute("lang")!.Value.Should().Be(language);
        All(document, "title").Should().ContainSingle().Which.Value.Should().NotBeNullOrWhiteSpace();
        All(document, "meta").Should().Contain(m => (string?)m.Attribute("charset") == "utf-8");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_HasOneH1_AndHeadingsNeverSkipALevel(string language)
    {
        var document = Load(language);
        All(document, "h1").Should().ContainSingle();

        var previous = 0;
        foreach (var heading in document.Descendants().Where(e => e.Name.LocalName.Length == 2 && e.Name.LocalName[0] == 'h' && char.IsDigit(e.Name.LocalName[1])))
        {
            var level = heading.Name.LocalName[1] - '0';
            level.Should().BeLessThanOrEqualTo(previous + 1, $"'{heading.Value}' (line {((IXmlLineInfo)heading).LineNumber}) skips a heading level");
            heading.Value.Should().NotBeNullOrWhiteSpace();
            previous = level;
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_IdsAreUnique_AndEveryInternalLinkResolves(string language)
    {
        var document = Load(language);
        var ids = document.Descendants().Select(e => (string?)e.Attribute("id")).Where(id => id is not null).ToList();
        ids.Should().OnlyHaveUniqueItems();

        var links = All(document, "a").Select(a => (string?)a.Attribute("href")).ToList();
        links.Should().NotContainNulls("an anchor without href is not a link");
        var internalLinks = links.Where(h => h!.StartsWith('#')).Select(h => h![1..]).ToList();
        internalLinks.Should().NotBeEmpty();
        internalLinks.Except(ids).Should().BeEmpty("every #link must point to an existing id");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_HasAnIndex_ThatReachesEverySection(string language)
    {
        var document = Load(language);
        var nav = All(document, "nav").Should().ContainSingle().Subject;
        ((string?)nav.Attribute("aria-label")).Should().NotBeNullOrWhiteSpace();

        var indexed = nav.Descendants().Where(e => e.Name.LocalName == "a").Select(a => ((string)a.Attribute("href")!)[1..]).ToHashSet();
        var sections = All(document, "section").Select(s => (string?)s.Attribute("id")).ToList();
        sections.Should().NotContainNulls().And.HaveCountGreaterThan(20);
        sections.Except(indexed).Should().BeEmpty("every chapter is in the index");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_EveryTable_HasCaption_ColumnHeaders_AndRowHeaders(string language)
    {
        var document = Load(language);
        var tables = All(document, "table").ToList();
        tables.Should().NotBeEmpty();

        foreach (var table in tables)
        {
            var where = $"table at line {((IXmlLineInfo)table).LineNumber}";
            table.Elements().Where(e => e.Name.LocalName == "caption").Should().ContainSingle(where).Which.Value.Should().NotBeNullOrWhiteSpace();
            var columnHeaders = table.Descendants().Where(e => e.Name.LocalName == "th" && (string?)e.Attribute("scope") == "col").ToList();
            columnHeaders.Should().NotBeEmpty(where);
            foreach (var row in table.Descendants().Where(e => e.Name.LocalName == "tbody").SelectMany(b => b.Elements()))
            {
                var first = row.Elements().First();
                first.Name.LocalName.Should().Be("th", where);
                ((string?)first.Attribute("scope")).Should().Be("row", where);
                row.Elements().Count().Should().Be(columnHeaders.Count, where);
            }
        }
        All(document, "th").Should().OnlyContain(th => th.Attribute("scope") != null);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_HasNoScripts_NoImages_AndNoExternalResources(string language)
    {
        var document = Load(language);

        foreach (var forbidden in new[] { "script", "img", "iframe", "object", "embed", "video", "audio", "link" })
            All(document, forbidden).Should().BeEmpty($"<{forbidden}> is not allowed in the manual");
        document.Descendants().SelectMany(e => e.Attributes()).Should().NotContain(a => a.Name.LocalName.StartsWith("on"), "no inline event handlers");
        All(document, "style").Should().ContainSingle().Which.Value.Should().NotContain("url(").And.NotContain("@import");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Manual_ExternalLinks_AreHttps_AndTheLuaReferenceIsTheFileShippedNextToIt(string language)
    {
        var document = Load(language);
        var external = All(document, "a").Select(a => (string)a.Attribute("href")!).Where(h => !h.StartsWith('#')).Distinct().ToList();

        external.Should().Contain(HelpService.LuaReferenceFile);
        foreach (var href in external.Where(h => h != HelpService.LuaReferenceFile))
            (href.StartsWith("https://", StringComparison.Ordinal) || href.StartsWith("mailto:", StringComparison.Ordinal)).Should().BeTrue($"'{href}' must be https");
    }

    [Fact]
    public void BothManuals_HaveTheSameStructure()
    {
        var spanish = Load("es");
        var english = Load("en");

        static List<string> Ids(XDocument d) => d.Descendants().Select(e => (string?)e.Attribute("id")).Where(i => i is not null).Select(i => i!).ToList();
        Ids(english).Should().Equal(Ids(spanish), "the same ids in the same order: a link between languages always works");
        All(english, "table").Count().Should().Be(All(spanish, "table").Count());
        All(english, "tr").Count().Should().Be(All(spanish, "tr").Count());
    }

    // ── The shortcuts of the manual are the shortcuts of the program ───────

    private static FrmGame CreateGameWindow()
    {
        var session = Substitute.For<IMudSession>();
        session.Profile.Returns(new SessionProfile { Title = "Reinos", Host = "mud.example.org", Port = 23, MudId = 1, MudName = "Reinos" });
        session.Options.Returns(OmnimudOptions.Default with { ConfirmBeforeExit = false });
        session.State.Returns(SessionState.Connected);
        session.Messages.Returns(new List<SessionMessage>());
        session.History.Returns(new List<string>());
        session.ActionMenu.Returns(Omnimud.Core.Actions.ActionMenu.Empty);
        return new FrmGame(session, Substitute.For<ISessionSound>(), Substitute.For<ISessionDialogs>(), TimeProvider.System,
            Substitute.For<IAnnouncer>(), new FakeAppDialogs());
    }

    private static IEnumerable<ToolStripMenuItem> AllMenuItems(ToolStripItemCollection items) =>
        items.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(AllMenuItems(i.DropDownItems)));

    /// <summary>Canonical spelling used by the data-keys attribute: Ctrl, Shift, Alt, then the name of the key in <see cref="Keys"/>.</summary>
    internal static string Canonical(Keys shortcut)
    {
        var parts = new List<string>();
        if (shortcut.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (shortcut.HasFlag(Keys.Shift)) parts.Add("Shift");
        if (shortcut.HasFlag(Keys.Alt)) parts.Add("Alt");
        parts.Add((shortcut & Keys.KeyCode).ToString());
        return string.Join('+', parts);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ShortcutTable_DocumentsExactlyTheMenuShortcutsOfTheGameWindow(string language) => Sta.Run(() =>
    {
        Strings.Culture = CultureInfo.GetCultureInfo(language);
        using var form = CreateGameWindow();
        var real = AllMenuItems(form.MainMenuStrip!.Items).Where(i => i.ShortcutKeys != Keys.None).Select(i => Canonical(i.ShortcutKeys)).ToList();
        real.Should().HaveCountGreaterThan(15).And.OnlyHaveUniqueItems();

        var table = Load(language).Descendants().Single(e => e.Name.LocalName == "table" && (string?)e.Attribute("id") == "game-shortcuts");
        var rows = table.Descendants().Where(e => e.Name.LocalName == "tbody").SelectMany(b => b.Elements()).ToList();
        var documented = rows.Select(r => (string?)r.Attribute("data-keys")).Where(k => k is not null).Select(k => k!).ToList();

        documented.Should().OnlyHaveUniqueItems();
        real.Except(documented).Should().BeEmpty("every menu shortcut of FrmGame must be in the manual (row with data-keys)");
        documented.Except(real).Should().BeEmpty("the manual documents a menu shortcut that FrmGame does not have");
        rows.Should().OnlyContain(r => r.Elements().First().Descendants().Any(e => e.Name.LocalName == "kbd"), "every row names its keys inside <kbd>");
        rows.Count.Should().BeGreaterThan(documented.Count, "keys handled outside the menus (Ctrl+1, Ctrl+F...) are in the same table");
    });

    [Theory]
    [MemberData(nameof(Languages))]
    public void ShortcutTable_FunctionKeys_ShowTheSameKeyTheyDeclare(string language)
    {
        // The F-keys are the ones users learn by heart: the visible text of <kbd> must say the same key as data-keys.
        var table = Load(language).Descendants().Single(e => (string?)e.Attribute("id") == "game-shortcuts");
        var rows = table.Descendants().Where(e => e.Attribute("data-keys") is { } k && k.Value.StartsWith('F') && k.Value.Length <= 3).ToList();

        rows.Should().HaveCount(9);
        foreach (var row in rows)
            row.Descendants().First(e => e.Name.LocalName == "kbd").Value.Should().Be(row.Attribute("data-keys")!.Value);
    }

    [Theory]
    [InlineData(Keys.F3, "F3")]
    [InlineData(Keys.Control | Keys.K, "Ctrl+K")]
    [InlineData(Keys.Control | Keys.Oemplus, "Ctrl+Oemplus")]
    [InlineData(Keys.Control | Keys.Shift | Keys.N, "Ctrl+Shift+N")]
    [InlineData(Keys.Alt | Keys.S, "Alt+S")]
    public void CanonicalSpelling(Keys keys, string expected) => Canonical(keys).Should().Be(expected);
}
