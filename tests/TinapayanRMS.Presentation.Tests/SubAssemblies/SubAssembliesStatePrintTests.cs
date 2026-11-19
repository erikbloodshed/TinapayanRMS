using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// Print on the Sub-Assemblies screen: the sub-assembly as the form shows it, on its own, in the print preview, its lines
/// and the batch's total, and a draft while it has changes not saved.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class SubAssembliesStatePrintTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Print_shows_the_recipe_its_lines_and_the_batch_total_as_saved() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSavedSubAssemblyAsync();
        TablePrintout? shown = null;
        using var handler = state.ShowPrint.RegisterHandler(interaction =>
        {
            shown = interaction.Input;
            interaction.SetOutput(System.Reactive.Unit.Default);
        });

        await state.PrintCommand.ExecuteAsync();

        shown!.Title.Should().Be("Sweet Dough");
        shown.Lines[0].Should().Be("Dough · Sub-Assemblies");
        shown.SaveNote.Should().BeNull("it's as saved");
        var table = shown.Tables.Should().ContainSingle().Subject;
        table.Columns.Select(c => c.Header).Should().Equal("Ingredient", "Grams", "Absolute %", "Cost/g", "Cost");
        table.Rows.Select(r => (r[0], r[1], r[4])).Should().Equal(("Flour", "1,000.0", "50.00"), ("Sugar", "200.0", "18.00"));
        table.Total.Should().Equal("Total", "1,200.0", "", "", "68.00");
    });

    [Test]
    public Task A_change_not_saved_prints_as_a_draft() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSavedSubAssemblyAsync();
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.Batch.Lines[0].Grams = 500m;
        recorder.EndUndoStep();

        var printout = state.PrintoutNow(DateTime.Now);

        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        printout.Tables[0].Rows[0][1].Should().Be("500.0", "it prints what the form shows");
    });

    /// <summary>A saved standalone sub-assembly of 1,000 g of flour at 0.05 and 200 g of sugar at 0.09, open in the form.</summary>
    private async Task<SubAssemblyForm> OpenSavedSubAssemblyAsync()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var subAssemblyId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Sweet Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, false, null), new RecipeLine(sugarId, 200m, false, null)],
            Cancellation);

        var factory = CreateDbContextFactory();
        var state = new SubAssembliesState(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            new InMemoryViewStateStore(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger()).Form!;
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single(r => r.Id == subAssemblyId));
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");
        return state;
    }
}
