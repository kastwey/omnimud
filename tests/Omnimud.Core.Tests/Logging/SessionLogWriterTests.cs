using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Omnimud.Core.Logging;
using Omnimud.Core.Options;

namespace Omnimud.Core.Tests.Logging;

public sealed class SessionLogWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "omnimud-log-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 4, 9, 5, 7, TimeSpan.Zero));

    public SessionLogWriterTests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private SessionLogWriter Create() => new(_time, _root);

    [Fact]
    public void Open_PerDay_UsesSortableDateNameInsideMudAndCharacterFolders()
    {
        using var sut = Create();

        sut.Open(LogMode.PerDay, null, "Reinos", "Zork").Should().BeTrue();

        sut.CurrentPath.Should().Be(Path.Combine(_root, "Reinos", "Zork", "2026-03-04.log"));
        File.Exists(sut.CurrentPath).Should().BeTrue();
    }

    [Fact]
    public void Open_PerSession_UsesDateAndTimeName()
    {
        using var sut = Create();

        sut.Open(LogMode.PerSession, null, "Reinos", "Zork");

        Path.GetFileName(sut.CurrentPath).Should().Be("2026-03-04 09-05-07.log");
    }

    [Fact]
    public void Open_None_WritesNothing()
    {
        using var sut = Create();

        sut.Open(LogMode.None, null, "Reinos", "Zork").Should().BeTrue();
        sut.WriteLine("texto");

        sut.IsOpen.Should().BeFalse();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public void Open_WithoutCharacter_UsesOnlyTheMudFolder()
    {
        using var sut = Create();

        sut.Open(LogMode.PerDay, null, "Reinos", null);

        sut.CurrentPath.Should().Be(Path.Combine(_root, "Reinos", "2026-03-04.log"));
    }

    [Fact]
    public void Open_CustomDirectory_ReplacesTheDefaultBase()
    {
        var custom = Path.Combine(_root, "mis logs");
        using var sut = Create();

        sut.Open(LogMode.PerDay, custom, "Reinos", "Zork");

        sut.CurrentPath.Should().StartWith(Path.Combine(custom, "Reinos", "Zork"));
    }

    [Fact]
    public void Open_NamesWithInvalidCharacters_AreSanitized()
    {
        using var sut = Create();

        sut.Open(LogMode.PerDay, null, "Mud: el <mejor>?", "Zo/rk").Should().BeTrue();

        sut.CurrentPath.Should().Be(Path.Combine(_root, "Mud_ el _mejor__", "Zo_rk", "2026-03-04.log"));
    }

    [Fact]
    public void WriteLine_AppendsUtf8WithoutBom_AndKeepsExistingContent()
    {
        var path = Path.Combine(_root, "Reinos", "Zork", "2026-03-04.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "sesión anterior" + Environment.NewLine);

        using (var sut = Create())
        {
            sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
            sut.WriteLine("Un ñu dice: ¡hola!");
            sut.Close(writeFooter: false);
        }

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        File.ReadAllLines(path).Should().Equal("sesión anterior", "Un ñu dice: ¡hola!");
    }

    [Fact]
    public void WriteLine_MultilineText_UsesPlatformLineBreaks()
    {
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        var path = sut.CurrentPath!;

        sut.WriteLine("uno\ndos");
        sut.Close(writeFooter: false);

        File.ReadAllText(path).Should().Be("uno" + Environment.NewLine + "dos" + Environment.NewLine);
    }

    [Fact]
    public void Close_WritesLocalizedFooterWithDateAndTime()
    {
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        var path = sut.CurrentPath!;
        _time.Advance(TimeSpan.FromMinutes(10));

        sut.Close();

        File.ReadAllLines(path).Last().Should().Be("Partida finalizada el 04/03/2026 a las 09:15:07.");
        sut.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Close_InEnglish_UsesEnglishFooter()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        var path = sut.CurrentPath!;

        sut.Close();

        File.ReadAllLines(path).Last().Should().Be("Session ended on 2026-03-04 at 09:05:07.");
    }

    [Fact]
    public void Close_Twice_WritesOneFooter()
    {
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        var path = sut.CurrentPath!;

        sut.Close();
        sut.Close();

        File.ReadAllLines(path).Should().ContainSingle();
    }

    [Fact]
    public void WriteLine_PerDayPastMidnight_RollsOverToTheNewDay()
    {
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        var first = sut.CurrentPath!;
        sut.WriteLine("antes");

        _time.Advance(TimeSpan.FromHours(15));
        sut.WriteLine("después");
        var second = sut.CurrentPath!;
        sut.Close(writeFooter: false);

        Path.GetFileName(second).Should().Be("2026-03-05.log");
        File.ReadAllLines(first).Should().Equal("antes");
        File.ReadAllLines(second).Should().Equal("después");
    }

    [Fact]
    public void WriteLine_PerSessionPastMidnight_KeepsTheSameFile()
    {
        using var sut = Create();
        sut.Open(LogMode.PerSession, null, "Reinos", "Zork");
        var path = sut.CurrentPath;

        _time.Advance(TimeSpan.FromHours(15));
        sut.WriteLine("después");

        sut.CurrentPath.Should().Be(path);
    }

    [Fact]
    public void Open_UnusableDirectory_ReturnsFalseAndReportsOnce()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "fichero");
        File.WriteAllText(blocker, "no soy una carpeta");
        var errors = new List<string>();
        using var sut = Create();
        sut.Failed += errors.Add;

        var opened = sut.Open(LogMode.PerDay, blocker, "Reinos", "Zork");
        sut.WriteLine("no debe fallar");

        opened.Should().BeFalse();
        errors.Should().ContainSingle();
        sut.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void FileCanBeReadWhileOpen()
    {
        using var sut = Create();
        sut.Open(LogMode.PerDay, null, "Reinos", "Zork");
        sut.WriteLine("línea");

        using var reader = new StreamReader(new FileStream(sut.CurrentPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        reader.ReadLine().Should().Be("línea");
    }
}
