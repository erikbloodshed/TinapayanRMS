using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Tests.Vim;

/// <summary>VimCommandLine: what's typed after the colon, read into a command or a reason it can't be.</summary>
public class VimCommandLineTests
{
    [Test]
    [Arguments("w", VimCommandKind.Save)]
    [Arguments("write", VimCommandKind.Save)]
    [Arguments(":w", VimCommandKind.Save)]
    [Arguments("  w  ", VimCommandKind.Save)]
    [Arguments("new", VimCommandKind.New)]
    [Arguments("del", VimCommandKind.Delete)]
    [Arguments("delete", VimCommandKind.Delete)]
    [Arguments("rev", VimCommandKind.Revert)]
    [Arguments("set", VimCommandKind.Settings)]
    [Arguments("h", VimCommandKind.Help)]
    [Arguments("HELP", VimCommandKind.Help)]
    public void A_command_is_read_by_its_name_or_the_start_of_it(string line, VimCommandKind kind)
    {
        var command = VimCommandLine.Parse(line, out var error);

        error.Should().BeEmpty();
        command.Should().Be(new VimCommand(kind));
    }

    [Test]
    [Arguments("ra", AppScreen.RawMaterials)]
    [Arguments("raw", AppScreen.RawMaterials)]
    [Arguments("sub", AppScreen.SubAssemblies)]
    [Arguments("pro", AppScreen.Products)]
    [Arguments("products", AppScreen.Products)]
    [Arguments("rep", AppScreen.Reports)]
    [Arguments("si", AppScreen.PriceSimulator)]
    [Arguments("simulator", AppScreen.PriceSimulator)]
    [Arguments("pri", AppScreen.Print)]
    public void A_screen_is_named_by_its_command(string line, AppScreen screen)
    {
        VimCommandLine.Parse(line, out _).Should().Be(new VimCommand(VimCommandKind.Screen, Screen: screen));
    }

    [Test]
    public void Edit_takes_the_rest_of_the_line_as_the_name_to_open()
    {
        VimCommandLine.Parse("e  cream loaf ", out var error).Should().Be(new VimCommand(VimCommandKind.Open, "cream loaf"));
        error.Should().BeEmpty();
    }

    [Test]
    [Arguments("", "Type a command")]
    [Arguments(":", "Type a command")]
    [Arguments("xyz", "Not a command: xyz")]
    [Arguments("r", "Not a command: r")]
    [Arguments("pr", "Not a command: pr")]
    [Arguments("re", "Not a command: re")]
    [Arguments("writer", "Not a command: writer")]
    [Arguments("e", "Say which one: :e name")]
    [Arguments("w now", ":w takes nothing after it")]
    public void What_isnt_a_command_says_why(string line, string reason)
    {
        VimCommandLine.Parse(line, out var error).Should().BeNull();
        error.Should().StartWith(reason);
    }

    [Test]
    public void Every_way_of_shortening_a_command_names_that_command_alone()
    {
        foreach (var command in VimCommandLine.Commands)
        {
            for (var length = command.ShortestLength; length <= command.Name.Length; length++)
            {
                var typed = command.Name[..length];
                VimCommandLine.Commands.Where(other => other.IsNamedBy(typed)).Should().ContainSingle($":{typed} is {command.Name}'s").Which.Should().Be(command);
            }
        }
    }
}
