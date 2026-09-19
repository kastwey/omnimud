using FluentAssertions;
using Omnimud.Core.Aliases;

namespace Omnimud.Core.Tests.Aliases;

public class AliasResolverTests
{
    private readonly AliasResolver _sut = new();

    [Fact]
    public void Resolve_NoAliases_ReturnsOriginal()
    {
        _sut.Resolve("kill monster").Should().Be("kill monster");
    }

    [Fact]
    public void Resolve_MatchingAlias_SubstitutesAction()
    {
        _sut.Load([new AliasDefinition("k", "kill")]);

        _sut.Resolve("k monster").Should().Be("kill monster");
    }

    [Fact]
    public void Resolve_NoArguments_ReturnsActionOnly()
    {
        _sut.Load([new AliasDefinition("q", "quit")]);

        _sut.Resolve("q").Should().Be("quit");
    }

    [Fact]
    public void Resolve_CaseInsensitive_Matches()
    {
        _sut.Load([new AliasDefinition("K", "kill")]);

        _sut.Resolve("k dragon").Should().Be("kill dragon");
    }

    [Fact]
    public void Resolve_DisabledAlias_DoesNotMatch()
    {
        _sut.Load([new AliasDefinition("k", "kill", Enabled: false)]);

        _sut.Resolve("k monster").Should().Be("k monster");
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        _sut.Resolve("").Should().Be("");
        _sut.Resolve("   ").Should().Be("   ");
    }

    [Fact]
    public void Add_NewAlias_IsResolvable()
    {
        _sut.Add(new AliasDefinition("x", "exit"));

        _sut.Resolve("x").Should().Be("exit");
    }

    [Fact]
    public void Remove_ExistingAlias_NoLongerResolves()
    {
        _sut.Load([new AliasDefinition("k", "kill")]);
        _sut.Remove("k");

        _sut.Resolve("k monster").Should().Be("k monster");
    }

    [Fact]
    public void Get_ExistingAlias_ReturnsIt()
    {
        _sut.Load([new AliasDefinition("k", "kill")]);

        _sut.Get("k").Should().NotBeNull();
        _sut.Get("k")!.Action.Should().Be("kill");
    }

    [Fact]
    public void Get_NonExisting_ReturnsNull()
    {
        _sut.Get("x").Should().BeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllLoaded()
    {
        _sut.Load([
            new AliasDefinition("k", "kill"),
            new AliasDefinition("q", "quit")
        ]);

        _sut.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Load_ReplacesExisting()
    {
        _sut.Load([new AliasDefinition("k", "kill")]);
        _sut.Load([new AliasDefinition("q", "quit")]);

        _sut.GetAll().Should().HaveCount(1);
        _sut.Get("k").Should().BeNull();
    }
}
