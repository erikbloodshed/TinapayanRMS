using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// The Print dialog (PrintDialog over PrintPreviews): what opens it shows its printout in it, and it closes on its ✕, Esc
/// or a move to another address, leaving the screen under it as it was.
/// </summary>
public class PrintDialogTests
{
    private static BunitContext NewContext()
    {
        var context = AppServices.NewContext();
        context.Services.AddSingleton<IFileDialogs>(new FakeFileDialogs());
        return context.RenderingAsTheWebView();
    }

    private static TablePrintout Table() => new(
        "Raw Materials Price Summary",
        [],
        [new PrintedTable(null, [new PrintedColumn("Name"), new PrintedColumn("Price", IsNumber: true)], [["Flour", "52.00"]])],
        new DateTime(2026, 11, 20, 9, 0, 0));

    [Test]
    public async Task A_table_opens_in_the_dialog_under_its_title()
    {
        await using var context = NewContext();
        var previews = context.Services.GetRequiredService<PrintPreviews>();
        var dialog = context.Render<PrintDialog>();

        await dialog.InvokeAsync(() => previews.ShowTables(Table()));

        await dialog.WaitForAssertionAsync(() =>
        {
            dialog.Find(".print-dialog .e-dlg-header").TextContent.Should().Be("Raw Materials Price Summary");
            dialog.Find(".print-dialog .print-document").TextContent.Should().Contain("Flour");
        });
    }

    [Test]
    public async Task A_move_to_another_address_closes_it()
    {
        await using var context = NewContext();
        var previews = context.Services.GetRequiredService<PrintPreviews>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var dialog = context.Render<PrintDialog>();
        await dialog.InvokeAsync(() => previews.ShowTables(Table()));

        await dialog.InvokeAsync(() => navigation.NavigateTo("reports"));

        await dialog.WaitForAssertionAsync(() => previews.Showing.Should().BeNull());
    }

    [Test]
    public async Task Closed_it_draws_no_printout()
    {
        await using var context = NewContext();
        var previews = context.Services.GetRequiredService<PrintPreviews>();
        var dialog = context.Render<PrintDialog>();
        await dialog.InvokeAsync(() => previews.ShowTables(Table()));
        await dialog.WaitForAssertionAsync(() => dialog.FindAll(".print-document").Should().NotBeEmpty());

        await dialog.InvokeAsync(previews.Close);

        await dialog.WaitForAssertionAsync(() => dialog.FindAll(".print-document").Should().BeEmpty());
    }
}
