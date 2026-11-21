using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// Undo and redo on the Products screen. One step is one finished change, and a step is recorded by bracketing
/// the change with BeginUndoStep/EndUndoStep — which is exactly what the fields and line tables (TextField,
/// NumberField, PickField, LineGrid) do around a cell edit, an added or deleted row, and a field typed and left, so
/// driving the screen that way here exercises the real path without a page.
///
/// What these are mostly guarding is that a restore puts back state the public setters can't express, and that
/// it leaves alone the baselines Save compares against: a change undone and a change never made have to come to
/// the same thing, in the form and in the database.
///
/// The last few cover whether Save has anything to write, which is measured against the same snapshots.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsStateUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Undoing_a_grams_edit_puts_the_line_and_the_costs_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        var line = state.UsageForm.FormLines.Single();
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,000 g");

        Edit(state, () => line.Grams = 500m);

        state.History.CanUndo.Should().BeTrue();
        state.UsageForm.UsageFormTotalWeightText.Should().Be("500 g");

        state.TryUndo().Should().BeTrue();

        state.UsageForm.FormLines.Single().Grams.Should().Be(1000m);
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,000 g", "the restore works the form's figures out again, once");
        state.History.CanUndo.Should().BeFalse();
        state.History.CanRedo.Should().BeTrue();

        state.TryRedo().Should().BeTrue();
        state.UsageForm.FormLines.Single().Grams.Should().Be(500m);
    });

    [Test]
    public Task Undoing_keeps_a_typed_percentage_that_is_waiting_for_a_base_to_come_back() => UiThread.RunAsync(async () =>
    {
        // The state a restore through the public setters loses silently: a percentage typed, then the base
        // unticked, so nothing can be typed into the percentage column and the figure is only being held.
        var (state, ingredients) = await OpenTwoLineProductAsync();
        var flour = state.UsageForm.FormLines[0];
        var sugar = state.UsageForm.FormLines[1];

        sugar.Percent = 65m;
        sugar.AuthoredBakersPercent.Should().Be(65m);
        flour.IsFormulaBase = false;
        sugar.CanTypeBakersPercent.Should().BeFalse("with no base there's nothing to read a percentage against");

        // An unrelated change, then take it back: the held percentage has to survive the round trip.
        Edit(state, () => state.EditingName = "Renamed");
        state.TryUndo().Should().BeTrue();

        var restored = state.UsageForm.FormLines[1];
        restored.AuthoredBakersPercent.Should().Be(65m, "the percentage was waiting for a base, not thrown away");

        // And it still works out the grams once a base comes back, which is the only way the loss would show.
        state.UsageForm.FormLines[0].IsFormulaBase = true;
        state.UsageForm.FormLines[0].Grams = 2000m;
        state.UsageForm.FormLines[1].Grams.Should().Be(1300m, "65% of the 2,000 g base");
    });

    [Test]
    public Task Undoing_a_grams_edit_leaves_another_lines_typed_percentage_alone() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenTwoLineProductAsync();
        state.UsageForm.FormLines[1].Percent = 50m;

        Edit(state, () => state.UsageForm.FormLines[0].Grams = 1200m);
        state.TryUndo().Should().BeTrue();

        state.UsageForm.FormLines[0].Grams.Should().Be(1000m);
        state.UsageForm.FormLines[1].AuthoredBakersPercent.Should().Be(50m);
        state.UsageForm.FormLines[1].IsPercentTyped.Should().BeTrue("the figure it keeps is still the typed percentage");
    });

    [Test]
    public Task Undoing_puts_back_a_line_that_was_only_half_filled_in() => UiThread.RunAsync(async () =>
    {
        var (state, seeded) = await OpenOneSubAssemblyProductAsync();

        // An ingredient picked with no grams yet: a legitimate row, and one no constructor can rebuild.
        Edit(state, () => state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = seeded.Ingredients.SugarId }));
        Edit(state, () => state.EditingName = "Renamed");

        state.TryUndo().Should().BeTrue();

        var added = state.UsageForm.FormLines.Should().HaveCount(2).And.Subject.Last();
        added.IngredientId.Should().Be(seeded.Ingredients.SugarId);
        added.Grams.Should().BeNull("it was half filled in, and that's how it comes back");
    });

    [Test]
    public Task Undoing_a_removed_line_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenTwoLineProductAsync();
        state.UsageForm.SelectedFormLine = state.UsageForm.FormLines[1];

        await state.UsageForm.RemoveLineCommand.ExecuteAsync();
        state.UsageForm.FormLines.Should().ContainSingle();

        state.TryUndo().Should().BeTrue();
        state.UsageForm.FormLines.Should().HaveCount(2);
    });

    [Test]
    public Task Undo_leaves_what_Save_compares_against_alone() => UiThread.RunAsync(async () =>
    {
        // The direct test that the baselines weren't restored: a change undone must leave the database exactly
        // as a change never made would. Restoring _savedRecipe would make Save think the recipe had changed.
        var (state, seeded) = await OpenOneSubAssemblyProductAsync();
        var subAssemblyId = await SubAssemblyIdAsync(seeded.ProductId, seeded.DoughRoleId);

        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 500m);
        state.TryUndo().Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == subAssemblyId).ToListAsync(Cancellation);
        lines.Should().ContainSingle().Which.Grams.Should().Be(1000m);
    });

    [Test]
    public Task Undoing_a_quantity_required_change_puts_the_figure_and_the_costing_back() => UiThread.RunAsync(async () =>
    {
        // Quantity Required multiplies every cost on the screen, so an undo has to put both back: the typed
        // figure and the summary worked out from it.
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        var costPerPiece = state.Costing.Costs.Material;
        costPerPiece.Should().BeGreaterThan(0m);

        Edit(state, () => state.EditingQuantityRequired = 8);
        state.Costing.Costs.Material.Should().Be(costPerPiece * 8m);

        state.TryUndo().Should().BeTrue();

        state.EditingQuantityRequired.Should().Be(1);
        state.Costing.Costs.Material.Should().Be(costPerPiece);
    });

    [Test]
    public Task Undoing_a_section_applied_by_leaving_its_tab_does_not_apply_it_again() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenTwoSectionProductAsync();
        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        state.Recipe.PickRecipeTab(dough);
        state.UsageForm.TypeWeightPerPiece(75m);

        // Leaving the tab applies the form, which is one step.
        state.Recipe.PickRecipeTab(filling);
        state.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(75m);

        state.TryUndo().Should().BeTrue();

        state.ErrorMessage.Should().BeNull("restoring must not run the apply again");
        state.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(50m);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(filling, "the undo takes back the change, not where you navigated to");
    });

    [Test]
    public Task Moving_between_the_recipe_tabs_is_not_something_to_undo() => UiThread.RunAsync(async () =>
    {
        // Leaving a tab applies its form, which closes it, and a form closed isn't an edit: only the section it
        // applied is, and nothing was typed.
        var (state, _) = await OpenTwoSectionProductAsync();
        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        state.Recipe.PickRecipeTab(filling);
        state.Recipe.PickRecipeTab(dough);
        state.Recipe.PickRecipeTab(filling);

        state.History.CanUndo.Should().BeFalse("clicking through the sections changes nothing");
    });

    [Test]
    public Task A_step_left_open_across_a_change_of_tab_records_nothing() => UiThread.RunAsync(async () =>
    {
        // A field can hear it lost the cursor after the tab that took it is picked: its step is still open while the
        // form changes over to the other tab's section, and closes after it.
        var (state, _) = await OpenTwoSectionProductAsync(withPackaging: true);
        var recorder = (IUndoRecorder)state;

        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);
        recorder.BeginUndoStep();
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough"));
        recorder.EndUndoStep();

        state.History.CanUndo.Should().BeFalse("the form opening on the tab picked isn't an edit");

        // And a field still holding the cursor after the change records what's typed into it next.
        recorder.BeginUndoStep();
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling"));
        state.UsageForm.TypeWeightPerPiece(12m);
        recorder.EndUndoStep();

        state.History.CanUndo.Should().BeTrue();
        state.TryUndo().Should().BeTrue();
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(10m);
        state.History.CanUndo.Should().BeFalse("the change of tab was never a step of its own");
    });

    [Test]
    public Task A_tab_change_after_an_undo_still_applies_the_form_it_leaves() => UiThread.RunAsync(async () =>
    {
        // Guards the bookkeeping a restore does by hand: RecipeTabShown is skipped while the tab is put back,
        // and it's the only other thing that records which tab's form is open.
        var (state, _) = await OpenTwoSectionProductAsync();
        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        state.Recipe.PickRecipeTab(dough);
        state.UsageForm.TypeWeightPerPiece(75m);
        state.Recipe.PickRecipeTab(filling);
        state.TryUndo().Should().BeTrue();

        // Back to Dough's tab by hand, then type and leave: the apply must still happen, which it wouldn't if
        // the restore had left behind a stale note of which tab's form was open.
        state.Recipe.PickRecipeTab(dough);
        state.UsageForm.TypeWeightPerPiece(80m);
        state.Recipe.PickRecipeTab(filling);

        state.ErrorMessage.Should().BeNull();
        state.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(80m);
    });

    [Test]
    public Task Undoing_Add_Sub_Assembly_takes_its_tab_away_and_puts_the_previous_form_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        var tabsBefore = state.Recipe.RecipeTabs.Count;
        var linesBefore = state.UsageForm.FormLines.Count;

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);

        state.TryUndo().Should().BeTrue();

        state.Recipe.RecipeTabs.Should().NotContain(state.Recipe.AddSubAssemblyTab);
        state.Recipe.RecipeTabs.Should().HaveCount(tabsBefore);
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>();
        state.UsageForm.FormLines.Should().HaveCount(linesBefore, "the form the add replaced is back with its lines");
    });

    [Test]
    public Task Undoing_a_removed_section_puts_it_back_with_its_tab() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenTwoSectionProductAsync();
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling"));

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();
        state.Recipe.UsageEdits.Should().ContainSingle();

        state.TryUndo().Should().BeTrue();

        state.Recipe.UsageEdits.Should().HaveCount(2);
        state.Recipe.UsageEdits.Select(u => u.RoleName).Should().Equal("Dough", "Filling");
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().HaveCount(2, "the tabs follow the sections");
    });

    [Test]
    public Task The_tabs_stay_in_step_with_the_sections_across_an_undo() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenTwoSectionProductAsync();
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First());

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();
        state.TryUndo().Should().BeTrue();

        var tabs = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList();
        tabs.Should().HaveCount(state.Recipe.UsageEdits.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Usage.Should().BeSameAs(state.Recipe.UsageEdits[i], "each tab shows the section at its own index");
        }

        state.Recipe.SelectedRecipeTab.Should().NotBeNull();
        state.Recipe.RecipeTabs.Should().Contain(state.Recipe.SelectedRecipeTab!);
    });

    [Test]
    public Task A_step_that_changes_nothing_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)state;

        // Opened and closed with nothing done, as a cell clicked into and left alone would be.
        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        state.History.CanUndo.Should().BeFalse();

        // Retyped to the value it already had.
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 1000m);
        state.History.CanUndo.Should().BeFalse();
    });

    [Test]
    public Task A_refused_apply_records_nothing() => UiThread.RunAsync(async () =>
    {
        // Two sections in one role and neither named: the apply is refused, and a refusal changes only the
        // message and whether the add-ins table is open, neither of which is an edit.
        var (state, seeded) = await OpenOneSubAssemblyProductAsync();

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.First(r => r.Name == "Dough"));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = seeded.Ingredients.SugarId, Grams = 500m });
        state.UsageForm.TypeWeightPerPiece(20m);

        var stepsBefore = state.History.CanUndo;
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First());

        state.ErrorMessage.Should().StartWith("This recipe has more than one Dough without a name");
        state.History.CanUndo.Should().Be(stepsBefore, "a refusal changed nothing to take back");
    });

    [Test]
    public Task A_refresh_in_the_middle_of_a_step_is_not_an_edit() => UiThread.RunAsync(async () =>
    {
        // A LoadCommand landing between a field taking the cursor and losing it swaps in freshly priced
        // ingredients. Capturing them by id rather than by reference is what keeps that from reading as a change.
        var (state, seeded) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)state;

        recorder.BeginUndoStep();
        await SetPriceAndRecalculateBufferAsync(seeded.Ingredients.FlourId, null, 0.09m);
        await state.LoadCommand.ExecuteAsync();
        recorder.EndUndoStep();

        state.History.CanUndo.Should().BeFalse("nothing the user did changed");
    });

    [Test]
    public Task The_history_goes_when_the_form_stops_being_the_one_it_recorded() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();

        Edit(state, () => state.EditingName = "Renamed");
        state.History.CanUndo.Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        state.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        Edit(state, () => state.EditingName = "Renamed again");
        state.History.CanUndo.Should().BeTrue();

        // New over unsaved changes opens a tab of its own, with no history; this one keeps its own.
        await state.NewCommand.ExecuteAsync();
        state.View.Form.Should().NotBeSameAs(state);
        state.View.Form!.History.CanUndo.Should().BeFalse("a new product has no history of its own");
        state.History.CanUndo.Should().BeTrue("each tab keeps its own");
    });

    [Test]
    public Task Revert_throws_the_history_away_with_everything_else_since_the_Save() => UiThread.RunAsync(async () =>
    {
        // Revert reads the product back from the database, so the steps recorded against the form it replaced
        // would put back a form that no longer matches what Save compares against.
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        Edit(state, () => state.EditingName = "Renamed");
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 500m);
        state.History.CanUndo.Should().BeTrue();

        await state.RevertCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        state.EditingName.Should().Be("Ube Roll");
        state.History.CanUndo.Should().BeFalse("what was reverted to is the new starting point");
        state.History.CanRedo.Should().BeFalse();
        state.TryUndo().Should().BeFalse();
    });

    [Test]
    public Task Revert_on_a_product_that_was_never_saved_empties_the_form() => UiThread.RunAsync(async () =>
    {
        // The other branch: nothing is selected, so there is nothing to read back and a new product goes back
        // to being a blank one.
        var (state, seeded) = await OpenOneSubAssemblyProductAsync();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        await state.NewCommand.ExecuteAsync();
        state.SelectedProduct.Should().BeNull();
        state.EditingName = "Half-typed";
        Edit(state, () => state.EditingCurrentPrice = 99m);

        await state.RevertCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.EditingName.Should().BeEmpty();
        state.EditingCurrentPrice.Should().BeNull();
        state.Recipe.UsageEdits.Should().BeEmpty();
        state.History.CanUndo.Should().BeFalse();

        // And the saved product is untouched: Revert only reads.
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(p => p.Id == seeded.ProductId, Cancellation)).Name.Should().Be("Ube Roll");
    });

    [Test]
    public Task Revert_turned_down_at_the_prompt_leaves_the_form_and_its_history_alone() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(false));

        Edit(state, () => state.EditingName = "Renamed");

        await state.RevertCommand.ExecuteAsync();

        state.EditingName.Should().Be("Renamed", "the prompt was turned down");
        state.History.CanUndo.Should().BeTrue("and the step is still there to take back by hand");
        state.TryUndo().Should().BeTrue();
        state.EditingName.Should().Be("Ube Roll");
    });

    [Test]
    public Task Save_is_not_offered_until_the_form_is_changed() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();

        state.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        CanSave(state).Should().BeFalse();

        // Typed into the form and not applied: Save applies it on the way in, so it counts.
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 1500m);

        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Moving_between_the_recipe_tabs_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // Each tab opens its section's form over the previous one, so the form holds different values without
        // anything having been edited. What Save measures against follows the form through (RebaseSettledForm).
        var (state, _) = await OpenTwoSectionProductAsync();
        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        state.Recipe.PickRecipeTab(dough);
        CanSave(state).Should().BeFalse();

        state.Recipe.PickRecipeTab(filling);
        CanSave(state).Should().BeFalse();

        state.Recipe.PickRecipeTab(dough);
        CanSave(state).Should().BeFalse("clicking through the sections writes nothing");

        // And the form is still live: typing in the one now showing does offer Save.
        Edit(state, () => state.UsageForm.FormLines.Single().Grams = 1500m);
        CanSave(state).Should().BeTrue();
    });

    [Test]
    [Arguments(UsageFormMode.New)]
    [Arguments(UsageFormMode.Link)]
    [Arguments(UsageFormMode.Copy)]
    public Task Opening_the_add_form_is_not_something_to_save(UsageFormMode mode) => UiThread.RunAsync(async () =>
    {
        // Link and Copy start the form again in their way after the tab opens it in New (the EditingUsageMode
        // subscription); that fresh form is what Save measures against too, until something is picked or typed.
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(cheeseRollId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var (state, _) = await OpenOneSubAssemblyProductAsync();

        await state.Recipe.AddUsageCommand.ExecuteAsync(mode);
        state.UsageForm.EditingUsageMode.Should().Be(mode);
        CanSave(state).Should().BeFalse("nothing has been entered in the add form");

        // Another way picked on the drop-down over the untouched form only changes its way.
        var other = mode == UsageFormMode.Link ? UsageFormMode.Copy : UsageFormMode.Link;
        await state.Recipe.AddUsageCommand.ExecuteAsync(other);
        state.UsageForm.EditingUsageMode.Should().Be(other);
        CanSave(state).Should().BeFalse("the form is still empty");

        // Picking what to link or copy is an entry, though.
        Edit(state, () => state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.First()));
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Save_stops_being_offered_once_the_change_is_taken_back() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        Edit(state, () => state.EditingName = "Renamed");
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        CanSave(state).Should().BeFalse("the form shows what was saved again");
    });

    [Test]
    public Task Save_stops_being_offered_once_it_has_written() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        Edit(state, () => state.EditingName = "Renamed");

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.UsageForm.FormLines.Count == 1,
            () => $"details={state.AreDetailsReady} lines={state.UsageForm.FormLines.Count}");

        CanSave(state).Should().BeFalse("what was written is the new starting point");
    });

    [Test]
    public Task A_cell_still_open_keeps_Save_offered() => UiThread.RunAsync(async () =>
    {
        // What's typed into a cell doesn't reach the line until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)state;

        recorder.IsEditPending = true;
        CanSave(state).Should().BeTrue();

        recorder.IsEditPending = false;
        CanSave(state).Should().BeFalse("the cell closed with nothing typed");
    });

    [Test]
    public Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with() => UiThread.RunAsync(async () =>
    {
        // The screen's real opening order: the blank form is costed at nothing before the defaults exist, and the
        // load then puts them on it. That isn't the user's change, so it mustn't read as something to save.
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 12m, OverheadPercent = 8m, AdministrativePercent = 5m, MarkupPercent = 40m, VatPercent = 12m },
            Cancellation);

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.Costing.EditingLaborPercent.Should().Be(12m, "a new product takes the defaults once they've loaded");
        CanSave(state).Should().BeFalse();
    });

    [Test]
    public Task Showing_the_tab_again_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (state, _) = await OpenOneSubAssemblyProductAsync();

        await state.LoadCommand.ExecuteAsync();

        CanSave(state).Should().BeFalse("a refresh isn't an edit");
    });

    [Test]
    public Task Undoing_Unlink_puts_the_link_back_and_redo_unlinks_again() => UiThread.RunAsync(async () =>
    {
        var (state, doughId) = await OpenLinkedDoughProductAsync();

        await state.Recipe.UnlinkCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        state.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue();
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        var linked = state.Recipe.UsageEdits.Single();
        linked.IsLinked.Should().BeTrue();
        linked.SubAssemblyId.Should().Be(doughId);
        CanSave(state).Should().BeFalse("the recipe links the dough again, as saved, and its form shows it as it was");

        state.TryRedo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue();
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Undoing_Move_to_Sub_Assemblies_keeps_it_the_products_own_and_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1, isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var state = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        state.UsageForm.IsEditingOwnedUsage.Should().BeTrue();

        await state.Recipe.MoveToSubAssembliesCommand.ExecuteAsync(state.UsageForm.EditingUsage!);
        state.Recipe.UsageEdits.Single().IsReleasing.Should().BeTrue();
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        state.Recipe.UsageEdits.Single().IsReleasing.Should().BeFalse();
        state.UsageForm.IsEditingOwnedUsage.Should().BeTrue();
        CanSave(state).Should().BeFalse();

        state.TryRedo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().IsReleasing.Should().BeTrue();
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Undoing_Move_to_In_House_keeps_its_lines_and_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1, isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        await MadeCategoryIdAsync();
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var state = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        state.Recipe.PromptForMadeIngredient.RegisterHandler(interaction => interaction.SetOutput(new ChoicePromptResult("Cheese Filling", 0)));

        await state.Recipe.MoveToInHouseCommand.ExecuteAsync(state.UsageForm.EditingUsage!);
        state.Recipe.UsageEdits.Single().IsMovingToInHouse.Should().BeTrue();
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        state.Recipe.UsageEdits.Single().IsMovingToInHouse.Should().BeFalse();
        state.UsageForm.CanMoveEditingUsageToInHouse.Should().BeTrue();
        CanSave(state).Should().BeFalse();

        state.TryRedo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().ToMadeIngredient!.Name.Should().Be("Cheese Filling");
        CanSave(state).Should().BeTrue();
    });

    [Test]
    public Task Undoing_Make_a_Copy_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenLinkedDoughProductAsync();

        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        CanSave(state).Should().BeTrue();

        state.TryUndo().Should().BeTrue();

        state.Recipe.UsageEdits.Single().IsLinked.Should().BeTrue();
        CanSave(state).Should().BeFalse("opening the copy's form moved what Save measures against, and the undo moves it back");
    });

    [Test]
    public Task Undoing_to_a_form_with_typing_not_yet_applied_still_offers_Save() => UiThread.RunAsync(async () =>
    {
        // The other half of taking the settled form back from when the step was taken: it's the form as the
        // section showed, not as it was typed into, so typing an undo puts back still counts as a change.
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.TypeWeightPerPiece(70m);

        Edit(state, () => state.EditingName = "Renamed");
        state.TryUndo().Should().BeTrue();

        state.EditingName.Should().Be("Ube Roll");
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(70m);
        CanSave(state).Should().BeTrue("the 70 g typed into the form isn't in the recipe yet, and Save would write it");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the page's button reads it.</summary>
    private static bool CanSave(ProductForm state) => state.SaveCommand.CanExecute();

    // ---- seeding and driving ----

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(ProductForm state, Action change)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    private sealed record SeededIngredients(int FlourId, int SugarId);

    private sealed record Seeded(int ProductId, int DoughRoleId, SeededIngredients Ingredients);

    /// <summary>A saved product with one Dough of 1,000 g of flour, opened with its form showing.</summary>
    private async Task<(ProductForm State, Seeded Seeded)> OpenOneSubAssemblyProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));

        var state = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        return (state, new Seeded(productId, doughRoleId, new SeededIngredients(flourId, sugarId)));
    }

    /// <summary>A product linking House Dough from the Sub-Assemblies screen (1,000 g of flour), opened with its form showing.</summary>
    private async Task<(ProductForm State, int DoughId)> OpenLinkedDoughProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var doughId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)]));

        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 1 && vm.UsageForm.FormLines.Count == 1);
        return (state, doughId);
    }

    /// <summary>The same product, with a second line so one can be the base and the other typed as a percentage.</summary>
    private async Task<(ProductForm State, SeededIngredients Ingredients)> OpenTwoLineProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m), (sugarId, 200m)], UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));

        var state = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 2);
        state.UsageForm.FormLines[0].IsFormulaBase = true;
        return (state, new SeededIngredients(flourId, sugarId));
    }

    /// <summary>A product with a Dough and a Filling, so tabs can be moved between, and a box a piece if asked.</summary>
    private async Task<(ProductForm State, int ProductId)> OpenTwoSectionProductAsync(bool withPackaging = false)
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        if (withPackaging)
        {
            await AddPackagingLineAsync(productId);
        }

        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 2 && (vm.Packaging.LineEdits.Count > 0) == withPackaging);
        return (state, productId);
    }

    /// <summary>A box a piece, saved straight to the product.</summary>
    private async Task AddPackagingLineAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = 30m, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<ProductForm> OpenAsync(int productId, Func<ProductForm, bool> ready)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && ready(state),
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}, lines={state.UsageForm.FormLines.Count}");

        state.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return state;
    }

    private ProductForm CreateState()
    {
        var factory = CreateDbContextFactory();
        return ProductsViews.Create(factory, CreateUnitOfWork()).Form!;
    }
}
