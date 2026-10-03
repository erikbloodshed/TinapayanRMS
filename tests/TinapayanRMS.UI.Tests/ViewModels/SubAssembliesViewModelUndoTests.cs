using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// Undo and redo on the Sub-Assemblies screen, the same mechanism Products uses with its own snapshot, and with
/// it whether Save has anything to write, which is measured against the same snapshots. Steps are recorded by
/// bracketing a change with BeginUndoStep/EndUndoStep, which is what the Controls/ behaviours do.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SubAssembliesViewModelUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Undoing_a_grams_edit_puts_the_line_and_the_totals_back()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        var weightBefore = viewModel.BatchWeight;
        var costBefore = viewModel.BatchCost;

        Edit(viewModel, () => viewModel.Lines[0].Grams = 500m);
        viewModel.BatchWeight.Should().NotBe(weightBefore);

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Lines[0].Grams.Should().Be(1000m);
        viewModel.BatchWeight.Should().Be(weightBefore, "the restore works the totals out again, once");
        viewModel.BatchCost.Should().Be(costBefore);
        viewModel.History.CanRedo.Should().BeTrue();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Lines[0].Grams.Should().Be(500m);
    }

    [Fact]
    public async Task Undoing_keeps_a_typed_percentage_that_is_waiting_for_a_base_to_come_back()
    {
        // The same trap as on Products: a percentage typed, then the base unticked, so nothing can be typed into
        // the percentage column and the figure is only being held. Restoring through the setters loses it.
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        viewModel.Lines[0].IsFormulaBase = true;
        viewModel.Lines[1].Percent = 65m;
        viewModel.Lines[0].IsFormulaBase = false;
        viewModel.Lines[1].CanTypeBakersPercent.Should().BeFalse();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.TryUndo().Should().BeTrue();

        viewModel.Lines[1].AuthoredBakersPercent.Should().Be(65m, "the percentage was waiting for a base, not thrown away");

        viewModel.Lines[0].IsFormulaBase = true;
        viewModel.Lines[0].Grams = 2000m;
        viewModel.Lines[1].Grams.Should().Be(1300m, "65% of the 2,000 g base");
    }

    [Fact]
    public async Task Undoing_a_removed_line_puts_it_back()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        viewModel.SelectedLine = viewModel.Lines[1];

        await viewModel.RemoveLineCommand.Execute();
        viewModel.Lines.Should().ContainSingle();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.Lines.Should().HaveCount(2);
        viewModel.BatchWeight.Should().Be(1200m);
    }

    [Fact]
    public async Task Undo_leaves_what_Save_writes_alone()
    {
        var (viewModel, subAssemblyId) = await OpenSavedSubAssemblyAsync();

        Edit(viewModel, () => viewModel.Lines[0].Grams = 500m);
        viewModel.TryUndo().Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .OrderBy(l => l.Id)
            .ToListAsync(Cancellation);
        lines[0].Grams.Should().Be(1000m, "a change undone and a change never made come to the same thing");
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == subAssemblyId, Cancellation)).TotalWeight.Should().Be(1200m);
    }

    [Fact]
    public async Task A_step_that_changes_nothing_is_not_recorded()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        viewModel.History.CanUndo.Should().BeFalse();

        Edit(viewModel, () => viewModel.Lines[0].Grams = 1000m);
        viewModel.History.CanUndo.Should().BeFalse("it was retyped to the figure it already had");
    }

    [Fact]
    public async Task The_history_goes_when_the_form_stops_being_the_one_it_recorded()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        // A Save reloads what was saved into the form, which is where the history goes.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        Edit(viewModel, () => viewModel.EditingName = "Renamed again");
        viewModel.History.CanUndo.Should().BeTrue();

        await viewModel.NewCommand.Execute();
        viewModel.History.CanUndo.Should().BeFalse("a new sub-assembly has no history of its own");
    }

    [Fact]
    public async Task Save_is_not_offered_until_the_form_is_changed()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();

        viewModel.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        (await CanSave(viewModel)).Should().BeFalse();

        Edit(viewModel, () => viewModel.Lines[0].Grams = 500m);

        viewModel.History.IsDirty.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();
    }

    [Fact]
    public async Task Save_stops_being_offered_once_the_change_is_taken_back()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        (await CanSave(viewModel)).Should().BeFalse("the form shows what was saved again");
    }

    [Fact]
    public async Task Save_stops_being_offered_once_it_has_written()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        Edit(viewModel, () => viewModel.EditingName = "Renamed");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        (await CanSave(viewModel)).Should().BeFalse("what was written is the new starting point");
    }

    [Fact]
    public async Task Save_is_not_offered_on_a_blank_new_form()
    {
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();

        await viewModel.NewCommand.Execute();
        (await CanSave(viewModel)).Should().BeFalse("an empty form has nothing to write");

        Edit(viewModel, () => viewModel.EditingName = "Sweet Dough II");
        (await CanSave(viewModel)).Should().BeTrue();
    }

    [Fact]
    public async Task A_cell_still_open_keeps_Save_offered()
    {
        // What's typed into a cell doesn't reach the line until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.IsEditPending = true;
        (await CanSave(viewModel)).Should().BeTrue();

        recorder.IsEditPending = false;
        (await CanSave(viewModel)).Should().BeFalse("the cell closed with nothing typed");
    }

    [Fact]
    public async Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with()
    {
        // The screen's real opening order: the form is blank before the roles exist, and the load then gives it
        // the first one. That isn't the user's change, so it mustn't read as something to save.
        await AddRoleAsync("Dough", sortOrder: 1);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.EditingRole.Should().NotBeNull("a new form takes the first role once they've loaded");
        (await CanSave(viewModel)).Should().BeFalse();
    }

    [Fact]
    public async Task Showing_the_tab_again_is_not_something_to_save()
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (viewModel, _) = await OpenSavedSubAssemblyAsync();

        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("a refresh isn't an edit");
    }

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the View binds it.</summary>
    private static async Task<bool> CanSave(SubAssembliesViewModel viewModel) =>
        await viewModel.SaveCommand.CanExecute.FirstAsync();

    /// <summary>One finished change, as the Controls/ behaviours bracket a gesture in the app.</summary>
    private static void Edit(SubAssembliesViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A saved standalone sub-assembly of 1,000 g of flour and 200 g of sugar, open in the form.</summary>
    private async Task<(SubAssembliesViewModel ViewModel, int SubAssemblyId)> OpenSavedSubAssemblyAsync()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var subAssemblyId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Sweet Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, false, null), new RecipeLine(sugarId, 200m, false, null)],
            Cancellation);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Id == subAssemblyId);
        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (viewModel, subAssemblyId);
    }

    private SubAssembliesViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new SubAssembliesViewModel(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new SubAssemblyValidator(),
            new LoggerConfiguration().CreateLogger());
    }

    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The sub-assembly never finished loading: {describe()}.");
    }
}
