using AwesomeAssertions;
using TinapayanRMS.UI.ViewModels;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>VimCommandLine: what's typed after the colon, read into a command or a reason it can't be.</summary>
public class VimCommandLineTests
{
    [Theory]
    [InlineData("w", VimCommandKind.Save)]
    [InlineData("write", VimCommandKind.Save)]
    [InlineData(":w", VimCommandKind.Save)]
    [InlineData("  w  ", VimCommandKind.Save)]
    [InlineData("new", VimCommandKind.New)]
    [InlineData("del", VimCommandKind.Delete)]
    [InlineData("delete", VimCommandKind.Delete)]
    [InlineData("rev", VimCommandKind.Revert)]
    [InlineData("set", VimCommandKind.Settings)]
    [InlineData("h", VimCommandKind.Help)]
    [InlineData("HELP", VimCommandKind.Help)]
    public void A_command_is_read_by_its_name_or_the_start_of_it(string line, VimCommandKind kind)
    {
        var command = VimCommandLine.Parse(line, out var error);

        error.Should().BeEmpty();
        command.Should().Be(new VimCommand(kind));
    }

    [Theory]
    [InlineData("ra", AppScreen.RawMaterials)]
    [InlineData("raw", AppScreen.RawMaterials)]
    [InlineData("sub", AppScreen.SubAssemblies)]
    [InlineData("pro", AppScreen.Products)]
    [InlineData("products", AppScreen.Products)]
    [InlineData("rep", AppScreen.Reports)]
    [InlineData("si", AppScreen.PriceSimulator)]
    [InlineData("simulator", AppScreen.PriceSimulator)]
    [InlineData("pri", AppScreen.Print)]
    public void A_screen_is_named_by_its_command(string line, AppScreen screen)
    {
        VimCommandLine.Parse(line, out _).Should().Be(new VimCommand(VimCommandKind.Screen, Screen: screen));
    }

    [Fact]
    public void Edit_takes_the_rest_of_the_line_as_the_name_to_open()
    {
        VimCommandLine.Parse("e  cream loaf ", out var error).Should().Be(new VimCommand(VimCommandKind.Open, "cream loaf"));
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Type a command")]
    [InlineData(":", "Type a command")]
    [InlineData("xyz", "Not a command: xyz")]
    [InlineData("r", "Not a command: r")]
    [InlineData("pr", "Not a command: pr")]
    [InlineData("re", "Not a command: re")]
    [InlineData("writer", "Not a command: writer")]
    [InlineData("e", "Say which one: :e name")]
    [InlineData("w now", ":w takes nothing after it")]
    public void What_isnt_a_command_says_why(string line, string reason)
    {
        VimCommandLine.Parse(line, out var error).Should().BeNull();
        error.Should().StartWith(reason);
    }

    [Fact]
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
