using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// Undo and redo on the Sub-Assemblies screen, the same mechanism Products uses with its own snapshot, and with
/// it whether Save has anything to write, which is measured against the same snapshots. Steps are recorded by
/// bracketing a change with BeginUndoStep/EndUndoStep, which is what the fields and line tables do.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class SubAssembliesStateUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Undoing_a_grams_edit_puts_the_line_and_the_totals_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        var weightBefore = state.Batch.BatchWeight;
        var costBefore = state.Batch.BatchCost;

        Edit(state, () => state.Batch.Lines[0].Grams = 500m);
        state.Batch.BatchWeight.Should().NotBe(weightBefore);

        state.TryUndo().Should().BeTrue();

        state.Batch.Lines[0].Grams.Should().Be(1000m);
        state.Batch.BatchWeight.Should().Be(weightBefore, "the restore works the totals out again, once");
        state.Batch.BatchCost.Should().Be(costBefore);
        state.History.CanRedo.Should().BeTrue();

        state.TryRedo().Should().BeTrue();
        state.Batch.Lines[0].Grams.Should().Be(500m);
    });

    [Test]
    public Task Undoing_keeps_a_typed_percentage_that_is_waiting_for_a_base_to_come_back() => UiThread.RunAsync(async () =>
    {
        // The same trap as on Products: a percentage typed, then the base unticked, so nothing can be typed into
        // the percentage column and the figure is only being held. Restoring through the setters loses it.
        var (state, _) = await OpenSavedSubAssemblyAsync();
        state.Batch.Lines[0].IsFormulaBase = true;
        state.Batch.Lines[1].Percent = 65m;
        state.Batch.Lines[0].IsFormulaBase = false;
        state.Batch.Lines[1].CanTypeBakersPercent.Should().BeFalse();

        Edit(state, () => state.EditingName = "Renamed");
        state.TryUndo().Should().BeTrue();

        state.Batch.Lines[1].AuthoredBakersPercent.Should().Be(65m, "the percentage was waiting for a base, not thrown away");

        state.Batch.Lines[0].IsFormulaBase = true;
        state.Batch.Lines[0].Grams = 2000m;
        state.Batch.Lines[1].Grams.Should().Be(1300m, "65% of the 2,000 g base");
    });

    [Test]
    public Task Undoing_a_removed_line_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        state.Batch.SelectedLine = state.Batch.Lines[1];

        await state.RemoveLineCommand.ExecuteAsync();
        state.Batch.Lines.Should().ContainSingle();

        state.TryUndo().Should().BeTrue();
        state.Batch.Lines.Should().HaveCount(2);
        state.Batch.BatchWeight.Should().Be(1200m);
    });

    [Test]
    public Task Undo_leaves_what_Save_writes_alone() => UiThread.RunAsync(async () =>
    {
        var (state, subAssemblyId) = await OpenSavedSubAssemblyAsync();

        Edit(state, () => state.Batch.Lines[0].Grams = 500m);
        state.TryUndo().Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines
            .Where(l => l.SubAssemblyId == subAssemblyId)
            .OrderBy(l => l.Id)
            .ToListAsync(Cancellation);
        lines[0].Grams.Should().Be(1000m, "a change undone and a change never made come to the same thing");
        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalWeight.Should().Be(1200m);
    });

    [Test]
    public Task A_step_that_changes_nothing_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        var recorder = (IUndoRecorder)state;

        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        state.History.CanUndo.Should().BeFalse();

        Edit(state, () => state.Batch.Lines[0].Grams = 1000m);
        state.History.CanUndo.Should().BeFalse("it was retyped to the figure it already had");
    });

    [Test]
    public Task The_history_goes_when_the_form_stops_being_the_one_it_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();

        Edit(state, () => state.EditingName = "Renamed");
        state.History.CanUndo.Should().BeTrue();

        // A Save reloads what was saved into the form, which is where the history goes.
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        state.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        Edit(state, () => state.EditingName = "Renamed again");
        state.History.CanUndo.Should().BeTrue();

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await state.NewCommand.ExecuteAsync();
        state.History.CanUndo.Should().BeFalse("a new sub-assembly has no history of its own");
    });

    [Test]
    public Task Scale_makes_the_batch_weigh_what_is_typed_as_one_step_to_save_or_undo() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();

        // 1,000 g of flour and 200 g of sugar, 1,200 g, made 2,400 g: twice each.
        state.Batch.ScaleTo = 2400m;
        await state.Batch.ScaleCommand.ExecuteAsync();

        state.Batch.Lines.Select(l => l.Grams).Should().Equal(2000m, 400m);
        state.Batch.BatchWeight.Should().Be(2400m);
        state.History.IsDirty.Should().BeTrue("Save keeps it");

        state.TryUndo().Should().BeTrue();
        state.Batch.Lines.Select(l => l.Grams).Should().Equal(1000m, 200m);
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Scale_can_make_the_base_weigh_what_is_typed_and_says_when_nothing_is_ticked() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        state.Batch.ScaleOption = state.Batch.ScaleOptions.Single(o => o.Basis == BatchScaleBasis.Base);
        state.Batch.ScaleTo = 500m;

        await state.Batch.ScaleCommand.ExecuteAsync();
        state.ErrorMessage.Should().Contain("Nothing is ticked as the base");
        state.Batch.Lines.Select(l => l.Grams).Should().Equal(1000m, 200m);

        // The flour ticked as the base: 500 g of it, so the sugar is halved too, still 20% of the flour.
        Edit(state, () => state.Batch.Lines[0].IsFormulaBase = true);
        await state.Batch.ScaleCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Batch.Lines.Select(l => l.Grams).Should().Equal(500m, 100m);
        state.Batch.Lines[1].Percent.Should().Be(20m);
    });

    [Test]
    public Task Save_is_not_offered_until_the_form_is_changed() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();

        state.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        CanSave(state).Should().BeFalse();

        Edit(state, () => state.Batch.Lines[0].Grams = 500m);

        state.History.IsDirty.Should().BeTrue();
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Save_stops_being_offered_once_the_change_is_taken_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        Edit(state, () => state.EditingName = "Renamed");
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        CanSave(state).Should().BeFalse("the form shows what was saved again");
    });

    [Test]
    public Task Save_stops_being_offered_once_it_has_written() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();
        Edit(state, () => state.EditingName = "Renamed");

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        CanSave(state).Should().BeFalse("what was written is the new starting point");
    });

    [Test]
    public Task Save_is_not_offered_on_a_blank_new_form() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenSavedSubAssemblyAsync();

        await state.NewCommand.ExecuteAsync();
        CanSave(state).Should().BeFalse("an empty form has nothing to write");

        Edit(state, () => state.EditingName = "Sweet Dough II");
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task A_cell_still_open_keeps_Save_offered() => UiThread.RunAsync(async () =>
    {
        // What's typed into a cell doesn't reach the line until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (state, _) = await OpenSavedSubAssemblyAsync();
        var recorder = (IUndoRecorder)state;

        recorder.IsEditPending = true;
        CanSave(state).Should().BeTrue();

        recorder.IsEditPending = false;
        CanSave(state).Should().BeFalse("the cell closed with nothing typed");
    });

    [Test]
    public Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with() => UiThread.RunAsync(async () =>
    {
        // The screen's real opening order: the form is blank before the roles exist, and the load then gives it
        // the first one. That isn't the user's change, so it mustn't read as something to save.
        await AddRoleAsync("Dough", sortOrder: 1);

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.EditingRole.Should().NotBeNull("a new form takes the first role once they've loaded");
        CanSave(state).Should().BeFalse();
    });

    [Test]
    public Task Showing_the_tab_again_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (state, _) = await OpenSavedSubAssemblyAsync();

        await state.LoadCommand.ExecuteAsync();

        CanSave(state).Should().BeFalse("a refresh isn't an edit");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the page's button reads it.</summary>
    private static bool CanSave(SubAssembliesState state) => state.SaveCommand.CanExecute();

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(SubAssembliesState state, Action change)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A saved standalone sub-assembly of 1,000 g of flour and 200 g of sugar, open in the form.</summary>
    private async Task<(SubAssembliesState State, int SubAssemblyId)> OpenSavedSubAssemblyAsync()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var subAssemblyId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Sweet Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, false, null), new RecipeLine(sugarId, 200m, false, null)],
            Cancellation);

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedRow = state.List.Rows.Single(r => r.Id == subAssemblyId);
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");

        state.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return (state, subAssemblyId);
    }

    private SubAssembliesState CreateState()
    {
        var factory = CreateDbContextFactory();

        return new SubAssembliesState(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            new InMemoryViewStateStore(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger());
    }
}
