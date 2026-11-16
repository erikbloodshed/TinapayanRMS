using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Reports;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Tests.Vim;

/// <summary>VimKeysState.Run: what's typed after the colon, run on the screen on show.</summary>
public class VimKeysStateTests
{
    [Test]
    [Arguments("raw", "raw-materials")]
    [Arguments("sub", "sub-assemblies")]
    [Arguments("pro", "products")]
    [Arguments("rep", "reports")]
    [Arguments("si", "price-simulator")]
    [Arguments("set", "settings")]
    public async Task A_screens_command_gives_its_address(string line, string url)
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();

        keys.Run(line).Should().Be(new VimCommandResult(Url: url));
    }

    [Test]
    public async Task Help_asks_for_the_cheat_sheet()
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();

        keys.Run(":help").Should().Be(new VimCommandResult(Help: true));
    }

    [Test]
    public async Task What_cant_be_read_is_said()
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();

        keys.Run("bogus").Message.Should().Be("Not a command: bogus. :help lists them all.");
    }

    [Test]
    [Arguments("w", "There's nothing to save on this screen.")]
    [Arguments("new", "There's nothing to start here.")]
    [Arguments("delete", "There's nothing to delete on this screen.")]
    [Arguments("e pandesal", "There's no list to open from on this screen.")]
    [Arguments("revert", "Revert isn't on this screen.")]
    public async Task A_record_command_needs_a_screen_of_records(string line, string message)
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();

        // Reports has no records to save or open: it's on show, and the commands say so.
        keys.Show(context.Services.GetRequiredService<ReportsState>());

        keys.Run(line).Message.Should().Be(message);
    }

    [Test]
    public async Task A_record_command_runs_on_the_screen_on_show()
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();
        var products = context.Services.GetRequiredService<ProductsState>();
        keys.Show(products);

        // Nothing is open and nothing has been typed: Save and Delete have nothing to act on, and no name matches.
        keys.Run("w").Message.Should().Be("There's nothing to save.");
        keys.Run("delete").Message.Should().Be("There's nothing to delete.");
        keys.Run("e pandesal").Message.Should().Be("Nothing on this screen is named “pandesal”.");
    }

    [Test]
    public async Task A_screen_that_has_gone_is_no_longer_the_one_on_show()
    {
        await using var context = AppServices.NewContext();
        var keys = context.Services.GetRequiredService<VimKeysState>();
        var products = context.Services.GetRequiredService<ProductsState>();
        var reports = context.Services.GetRequiredService<ReportsState>();

        // Going from Products to Reports: the new screen shows before the old one is taken down.
        keys.Show(products);
        keys.Show(reports);
        keys.Hide(products);

        keys.Screen.Should().BeSameAs(reports);

        keys.Hide(reports);
        keys.Screen.Should().BeNull();
    }
}
