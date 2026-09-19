using FluentAssertions;
using Omnimud.Core.Actions;

namespace Omnimud.Core.Tests.Actions;

public sealed class ActionMenuNodeTests
{
    [Fact]
    public void Action_HasCommandAndNoChildren()
    {
        var node = ActionMenuNode.Action("Coger", "coger espada")!;

        node.Label.Should().Be("Coger");
        node.Command.Should().Be("coger espada");
        node.Children.Should().BeEmpty();
        node.IsAction.Should().BeTrue();
        node.IsSubmenu.Should().BeFalse();
    }

    [Fact]
    public void Submenu_HasChildrenAndNoCommand()
    {
        var node = ActionMenuNode.Submenu("espada", [ActionMenuNode.Action("Coger", "coger espada"), null])!;

        node.Command.Should().BeNull();
        node.Children.Should().ContainSingle("null children are left out");
        node.IsSubmenu.Should().BeTrue();
        node.IsAction.Should().BeFalse();
    }

    [Fact]
    public void Information_IsNeitherActionNorSubmenu()
    {
        var node = ActionMenuNode.Information("piedra")!;

        node.IsAction.Should().BeFalse();
        node.IsSubmenu.Should().BeFalse();
        node.Command.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "cmd")]
    [InlineData("", "cmd")]
    [InlineData(" \n ", "cmd")]
    [InlineData("Coger", null)]
    [InlineData("Coger", "")]
    [InlineData("Coger", " \r\n ")]
    public void Action_WithoutLabelOrCommand_DoesNotExist(string? label, string? command)
        => ActionMenuNode.Action(label, command).Should().BeNull();

    [Fact]
    public void Submenu_WithoutLabelOrChildren_DoesNotExist()
    {
        var child = ActionMenuNode.Action("Coger", "coger");

        ActionMenuNode.Submenu("", [child]).Should().BeNull();
        ActionMenuNode.Submenu(null, [child]).Should().BeNull();
        ActionMenuNode.Submenu("espada", []).Should().BeNull();
        ActionMenuNode.Submenu("espada", [null]).Should().BeNull();
        ActionMenuNode.Information("  ").Should().BeNull();
    }

    [Fact]
    public void ANodeCannotBeBuilt_WithDirtyText()
    {
        // The factories are the only way in: every node that exists is clean.
        var node = ActionMenuNode.Action("Co\nger\u001B[0m", "coger espada\nabandonar")!;

        node.Label.Should().Be("Co ger");
        node.Command.Should().Be("coger espada abandonar");
        typeof(ActionMenuNode).GetConstructors().Should().BeEmpty("there is no public constructor");
    }
}
