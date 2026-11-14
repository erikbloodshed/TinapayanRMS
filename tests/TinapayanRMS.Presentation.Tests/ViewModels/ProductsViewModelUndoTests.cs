using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// Undo and redo on the Products screen. One step is one finished change, and a step is recorded by bracketing
/// the change with BeginUndoStep/EndUndoStep — which is exactly what the Controls/ behaviours do around a cell
/// edit, an added or deleted row, and a field typed and left, so driving the screen that way here exercises the
/// real path without a WPF control.
///
/// What these are mostly guarding is that a restore puts back state the public setters can't express, and that
/// it leaves alone the baselines Save compares against: a change undone and a change never made have to come to
/// the same thing, in the form and in the database.
///
/// The last few cover whether Save has anything to write, which is measured against the same snapshots.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsViewModelUndoTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Undoing_a_grams_edit_puts_the_line_and_the_costs_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        var line = viewModel.UsageForm.FormLines.Single();
        viewModel.UsageForm.UsageFormTotalWeightText.Should().Be("1,000 g");

        Edit(viewModel, () => line.Grams = 500m);

        viewModel.History.CanUndo.Should().BeTrue();
        viewModel.UsageForm.UsageFormTotalWeightText.Should().Be("500 g");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.UsageForm.FormLines.Single().Grams.Should().Be(1000m);
        viewModel.UsageForm.UsageFormTotalWeightText.Should().Be("1,000 g", "the restore works the form's figures out again, once");
        viewModel.History.CanUndo.Should().BeFalse();
        viewModel.History.CanRedo.Should().BeTrue();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.UsageForm.FormLines.Single().Grams.Should().Be(500m);
    });

    [Test]
    public Task Undoing_keeps_a_typed_percentage_that_is_waiting_for_a_base_to_come_back() => UiThread.RunAsync(async () =>
    {
        // The state a restore through the public setters loses silently: a percentage typed, then the base
        // unticked, so nothing can be typed into the percentage column and the figure is only being held.
        var (viewModel, ingredients) = await OpenTwoLineProductAsync();
        var flour = viewModel.UsageForm.FormLines[0];
        var sugar = viewModel.UsageForm.FormLines[1];

        sugar.Percent = 65m;
        sugar.AuthoredBakersPercent.Should().Be(65m);
        flour.IsFormulaBase = false;
        sugar.CanTypeBakersPercent.Should().BeFalse("with no base there's nothing to read a percentage against");

        // An unrelated change, then take it back: the held percentage has to survive the round trip.
        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.TryUndo().Should().BeTrue();

        var restored = viewModel.UsageForm.FormLines[1];
        restored.AuthoredBakersPercent.Should().Be(65m, "the percentage was waiting for a base, not thrown away");

        // And it still works out the grams once a base comes back, which is the only way the loss would show.
        viewModel.UsageForm.FormLines[0].IsFormulaBase = true;
        viewModel.UsageForm.FormLines[0].Grams = 2000m;
        viewModel.UsageForm.FormLines[1].Grams.Should().Be(1300m, "65% of the 2,000 g base");
    });

    [Test]
    public Task Undoing_a_grams_edit_leaves_another_lines_typed_percentage_alone() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenTwoLineProductAsync();
        viewModel.UsageForm.FormLines[1].Percent = 50m;

        Edit(viewModel, () => viewModel.UsageForm.FormLines[0].Grams = 1200m);
        viewModel.TryUndo().Should().BeTrue();

        viewModel.UsageForm.FormLines[0].Grams.Should().Be(1000m);
        viewModel.UsageForm.FormLines[1].AuthoredBakersPercent.Should().Be(50m);
        viewModel.UsageForm.FormLines[1].IsPercentTyped.Should().BeTrue("the figure it keeps is still the typed percentage");
    });

    [Test]
    public Task Undoing_puts_back_a_line_that_was_only_half_filled_in() => UiThread.RunAsync(async () =>
    {
        var (viewModel, seeded) = await OpenOneSubAssemblyProductAsync();

        // An ingredient picked with no grams yet: a legitimate row, and one no constructor can rebuild.
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = seeded.Ingredients.SugarId }));
        Edit(viewModel, () => viewModel.EditingName = "Renamed");

        viewModel.TryUndo().Should().BeTrue();

        var added = viewModel.UsageForm.FormLines.Should().HaveCount(2).And.Subject.Last();
        added.IngredientId.Should().Be(seeded.Ingredients.SugarId);
        added.Grams.Should().BeNull("it was half filled in, and that's how it comes back");
    });

    [Test]
    public Task Undoing_a_removed_line_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenTwoLineProductAsync();
        viewModel.UsageForm.SelectedFormLine = viewModel.UsageForm.FormLines[1];

        await viewModel.UsageForm.RemoveLineCommand.Execute();
        viewModel.UsageForm.FormLines.Should().ContainSingle();

        viewModel.TryUndo().Should().BeTrue();
        viewModel.UsageForm.FormLines.Should().HaveCount(2);
    });

    [Test]
    public Task Undo_leaves_what_Save_compares_against_alone() => UiThread.RunAsync(async () =>
    {
        // The direct test that the baselines weren't restored: a change undone must leave the database exactly
        // as a change never made would. Restoring _savedRecipe would make Save think the recipe had changed.
        var (viewModel, seeded) = await OpenOneSubAssemblyProductAsync();
        var subAssemblyId = await SubAssemblyIdAsync(seeded.ProductId, seeded.DoughRoleId);

        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 500m);
        viewModel.TryUndo().Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == subAssemblyId).ToListAsync(Cancellation);
        lines.Should().ContainSingle().Which.Grams.Should().Be(1000m);
    });

    [Test]
    public Task Undoing_a_quantity_required_change_puts_the_figure_and_the_costing_back() => UiThread.RunAsync(async () =>
    {
        // Quantity Required multiplies every cost on the screen, so an undo has to put both back: the typed
        // figure and the summary worked out from it.
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        var costPerPiece = viewModel.Costing.Costs.Material;
        costPerPiece.Should().BeGreaterThan(0m);

        Edit(viewModel, () => viewModel.EditingQuantityRequired = 8);
        viewModel.Costing.Costs.Material.Should().Be(costPerPiece * 8m);

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingQuantityRequired.Should().Be(1);
        viewModel.Costing.Costs.Material.Should().Be(costPerPiece);
    });

    [Test]
    public Task Undoing_a_section_applied_by_leaving_its_tab_does_not_apply_it_again() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        var dough = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        viewModel.Recipe.SelectedRecipeTab = dough;
        viewModel.UsageForm.EditingUsageWeightPerPiece = 75m;

        // Leaving the tab applies the form, which is one step.
        viewModel.Recipe.SelectedRecipeTab = filling;
        viewModel.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(75m);

        viewModel.TryUndo().Should().BeTrue();

        viewModel.ErrorMessage.Should().BeNull("restoring must not run the apply again");
        viewModel.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(50m);
        viewModel.Recipe.SelectedRecipeTab.Should().BeSameAs(filling, "the undo takes back the change, not where you navigated to");
    });

    [Test]
    public Task Moving_between_the_recipe_tabs_is_not_something_to_undo() => UiThread.RunAsync(async () =>
    {
        // Leaving a tab applies its form, which closes it, and a form closed isn't an edit: only the section it
        // applied is, and nothing was typed.
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        var dough = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        viewModel.Recipe.SelectedRecipeTab = filling;
        viewModel.Recipe.SelectedRecipeTab = dough;
        viewModel.Recipe.SelectedRecipeTab = filling;

        viewModel.History.CanUndo.Should().BeFalse("clicking through the sections changes nothing");
    });

    [Test]
    public Task A_step_left_open_across_a_change_of_tab_records_nothing() => UiThread.RunAsync(async () =>
    {
        // WPF selects a tab as it takes the cursor, before the field it takes it from hears so: the field's step
        // is still open while the form changes over to the other tab's section, and closes after it.
        var (viewModel, _) = await OpenTwoSectionProductAsync(withPackaging: true);
        var recorder = (IUndoRecorder)viewModel;

        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.PackagingTab;
        recorder.BeginUndoStep();
        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        recorder.EndUndoStep();

        viewModel.History.CanUndo.Should().BeFalse("the form opening on the tab picked isn't an edit");

        // And a field still holding the cursor after the change records what's typed into it next.
        recorder.BeginUndoStep();
        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");
        viewModel.UsageForm.EditingUsageWeightPerPiece = 12m;
        recorder.EndUndoStep();

        viewModel.History.CanUndo.Should().BeTrue();
        viewModel.TryUndo().Should().BeTrue();
        viewModel.UsageForm.EditingUsageWeightPerPiece.Should().Be(10m);
        viewModel.History.CanUndo.Should().BeFalse("the change of tab was never a step of its own");
    });

    [Test]
    public Task A_tab_change_after_an_undo_still_applies_the_form_it_leaves() => UiThread.RunAsync(async () =>
    {
        // Guards the bookkeeping a restore does by hand: RecipeTabShown is skipped while the tab is put back,
        // and it's the only other thing that records which tab's form is open.
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        var dough = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        viewModel.Recipe.SelectedRecipeTab = dough;
        viewModel.UsageForm.EditingUsageWeightPerPiece = 75m;
        viewModel.Recipe.SelectedRecipeTab = filling;
        viewModel.TryUndo().Should().BeTrue();

        // Back to Dough's tab by hand, then type and leave: the apply must still happen, which it wouldn't if
        // the restore had left behind a stale note of which tab's form was open.
        viewModel.Recipe.SelectedRecipeTab = dough;
        viewModel.UsageForm.EditingUsageWeightPerPiece = 80m;
        viewModel.Recipe.SelectedRecipeTab = filling;

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Recipe.UsageEdits.Single(u => u.RoleName == "Dough").WeightPerPiece.Should().Be(80m);
    });

    [Test]
    public Task Undoing_Add_Sub_Assembly_takes_its_tab_away_and_puts_the_previous_form_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        var tabsBefore = viewModel.Recipe.RecipeTabs.Count;
        var linesBefore = viewModel.UsageForm.FormLines.Count;

        await viewModel.Recipe.AddUsageCommand.Execute();
        viewModel.Recipe.SelectedRecipeTab.Should().BeSameAs(viewModel.Recipe.AddSubAssemblyTab);

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Recipe.RecipeTabs.Should().NotContain(viewModel.Recipe.AddSubAssemblyTab);
        viewModel.Recipe.RecipeTabs.Should().HaveCount(tabsBefore);
        viewModel.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>();
        viewModel.UsageForm.FormLines.Should().HaveCount(linesBefore, "the form the add replaced is back with its lines");
    });

    [Test]
    public Task Undoing_a_removed_section_puts_it_back_with_its_tab() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        await viewModel.Recipe.RemoveUsageCommand.Execute();
        viewModel.Recipe.UsageEdits.Should().ContainSingle();

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Recipe.UsageEdits.Should().HaveCount(2);
        viewModel.Recipe.UsageEdits.Select(u => u.RoleName).Should().Equal("Dough", "Filling");
        viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().HaveCount(2, "the tabs follow the sections");
    });

    [Test]
    public Task The_tabs_stay_in_step_with_the_sections_across_an_undo() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();

        await viewModel.Recipe.RemoveUsageCommand.Execute();
        viewModel.TryUndo().Should().BeTrue();

        var tabs = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList();
        tabs.Should().HaveCount(viewModel.Recipe.UsageEdits.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Usage.Should().BeSameAs(viewModel.Recipe.UsageEdits[i], "each tab shows the section at its own index");
        }

        viewModel.Recipe.SelectedRecipeTab.Should().NotBeNull();
        viewModel.Recipe.RecipeTabs.Should().Contain(viewModel.Recipe.SelectedRecipeTab!);
    });

    [Test]
    public Task A_step_that_changes_nothing_is_not_recorded() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)viewModel;

        // Opened and closed with nothing done, as a cell clicked into and left alone would be.
        recorder.BeginUndoStep();
        recorder.EndUndoStep();
        viewModel.History.CanUndo.Should().BeFalse();

        // Retyped to the value it already had.
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 1000m);
        viewModel.History.CanUndo.Should().BeFalse();
    });

    [Test]
    public Task A_refused_apply_records_nothing() => UiThread.RunAsync(async () =>
    {
        // Two sections in one role and neither named: the apply is refused, and a refusal changes only the
        // message and whether the add-ins table is open, neither of which is an edit.
        var (viewModel, seeded) = await OpenOneSubAssemblyProductAsync();

        await viewModel.Recipe.AddUsageCommand.Execute();
        viewModel.UsageForm.EditingUsageRole = viewModel.UsageForm.FormRoleOptions.First(r => r.Name == "Dough");
        viewModel.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = seeded.Ingredients.SugarId, Grams = 500m });
        viewModel.UsageForm.EditingUsageWeightPerPiece = 20m;

        var stepsBefore = viewModel.History.CanUndo;
        viewModel.Recipe.SelectedRecipeTab = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();

        viewModel.ErrorMessage.Should().StartWith("This recipe has more than one Dough without a name");
        viewModel.History.CanUndo.Should().Be(stepsBefore, "a refusal changed nothing to take back");
    });

    [Test]
    public Task A_refresh_in_the_middle_of_a_step_is_not_an_edit() => UiThread.RunAsync(async () =>
    {
        // A LoadCommand landing between a field taking the cursor and losing it swaps in freshly priced
        // ingredients. Capturing them by id rather than by reference is what keeps that from reading as a change.
        var (viewModel, seeded) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.BeginUndoStep();
        await SetPriceAndRecalculateBufferAsync(seeded.Ingredients.FlourId, null, 0.09m);
        await viewModel.LoadCommand.Execute();
        recorder.EndUndoStep();

        viewModel.History.CanUndo.Should().BeFalse("nothing the user did changed");
    });

    [Test]
    public Task The_history_goes_when_the_form_stops_being_the_one_it_recorded() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.History.CanUndo.Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.History.CanUndo.Should().BeFalse("what's saved is the new starting point");

        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        Edit(viewModel, () => viewModel.EditingName = "Renamed again");
        viewModel.History.CanUndo.Should().BeTrue();

        // New over unsaved changes asks about them first: not saving them is how they're left behind.
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        await viewModel.NewCommand.Execute();
        viewModel.History.CanUndo.Should().BeFalse("a new product has no history of its own");
    });

    [Test]
    public Task Revert_throws_the_history_away_with_everything_else_since_the_Save() => UiThread.RunAsync(async () =>
    {
        // Revert reads the product back from the database, so the steps recorded against the form it replaced
        // would put back a form that no longer matches what Save compares against.
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 500m);
        viewModel.History.CanUndo.Should().BeTrue();

        await viewModel.RevertCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.Recipe.UsageEdits.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.Recipe.UsageEdits.Count}");
        viewModel.EditingName.Should().Be("Ube Roll");
        viewModel.History.CanUndo.Should().BeFalse("what was reverted to is the new starting point");
        viewModel.History.CanRedo.Should().BeFalse();
        viewModel.TryUndo().Should().BeFalse();
    });

    [Test]
    public Task Revert_on_a_product_that_was_never_saved_empties_the_form() => UiThread.RunAsync(async () =>
    {
        // The other branch: nothing is selected, so there is nothing to read back and a new product goes back
        // to being a blank one.
        var (viewModel, seeded) = await OpenOneSubAssemblyProductAsync();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));

        await viewModel.NewCommand.Execute();
        viewModel.SelectedProduct.Should().BeNull();
        viewModel.EditingName = "Half-typed";
        Edit(viewModel, () => viewModel.EditingCurrentPrice = 99m);

        await viewModel.RevertCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.EditingCurrentPrice.Should().BeNull();
        viewModel.Recipe.UsageEdits.Should().BeEmpty();
        viewModel.History.CanUndo.Should().BeFalse();

        // And the saved product is untouched: Revert only reads.
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(p => p.Id == seeded.ProductId, Cancellation)).Name.Should().Be("Ube Roll");
    });

    [Test]
    public Task Revert_turned_down_at_the_prompt_leaves_the_form_and_its_history_alone() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(false));

        Edit(viewModel, () => viewModel.EditingName = "Renamed");

        await viewModel.RevertCommand.Execute();

        viewModel.EditingName.Should().Be("Renamed", "the prompt was turned down");
        viewModel.History.CanUndo.Should().BeTrue("and the step is still there to take back by hand");
        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Ube Roll");
    });

    [Test]
    public Task Save_is_not_offered_until_the_form_is_changed() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();

        viewModel.History.IsDirty.Should().BeFalse("the load itself isn't a change");
        (await CanSave(viewModel)).Should().BeFalse();

        // Typed into the form and not applied: Save applies it on the way in, so it counts.
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 1500m);

        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Moving_between_the_recipe_tabs_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // Each tab opens its section's form over the previous one, so the form holds different values without
        // anything having been edited. What Save measures against follows the form through (RebaseSettledForm).
        var (viewModel, _) = await OpenTwoSectionProductAsync();
        var dough = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = viewModel.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        viewModel.Recipe.SelectedRecipeTab = dough;
        (await CanSave(viewModel)).Should().BeFalse();

        viewModel.Recipe.SelectedRecipeTab = filling;
        (await CanSave(viewModel)).Should().BeFalse();

        viewModel.Recipe.SelectedRecipeTab = dough;
        (await CanSave(viewModel)).Should().BeFalse("clicking through the sections writes nothing");

        // And the form is still live: typing in the one now showing does offer Save.
        Edit(viewModel, () => viewModel.UsageForm.FormLines.Single().Grams = 1500m);
        (await CanSave(viewModel)).Should().BeTrue();
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
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();

        await viewModel.Recipe.AddUsageCommand.Execute(mode);
        viewModel.UsageForm.EditingUsageMode.Should().Be(mode);
        (await CanSave(viewModel)).Should().BeFalse("nothing has been entered in the add form");

        // Another way picked on the drop-down over the untouched form only changes its way.
        var other = mode == UsageFormMode.Link ? UsageFormMode.Copy : UsageFormMode.Link;
        await viewModel.Recipe.AddUsageCommand.Execute(other);
        viewModel.UsageForm.EditingUsageMode.Should().Be(other);
        (await CanSave(viewModel)).Should().BeFalse("the form is still empty");

        // Picking what to link or copy is an entry, though.
        Edit(viewModel, () => viewModel.UsageForm.EditingUsageSource = viewModel.UsageForm.LinkableSubAssemblies.First());
        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Save_stops_being_offered_once_the_change_is_taken_back() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        (await CanSave(viewModel)).Should().BeFalse("the form shows what was saved again");
    });

    [Test]
    public Task Save_stops_being_offered_once_it_has_written() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        Edit(viewModel, () => viewModel.EditingName = "Renamed");

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.UsageForm.FormLines.Count == 1,
            () => $"details={viewModel.AreDetailsReady} lines={viewModel.UsageForm.FormLines.Count}");

        (await CanSave(viewModel)).Should().BeFalse("what was written is the new starting point");
    });

    [Test]
    public Task A_cell_still_open_keeps_Save_offered() => UiThread.RunAsync(async () =>
    {
        // What's typed into a cell doesn't reach the line until the edit ends, and the click that saves is the
        // click that ends it - a Save disabled under the cursor would swallow it.
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        var recorder = (IUndoRecorder)viewModel;

        recorder.IsEditPending = true;
        (await CanSave(viewModel)).Should().BeTrue();

        recorder.IsEditPending = false;
        (await CanSave(viewModel)).Should().BeFalse("the cell closed with nothing typed");
    });

    [Test]
    public Task Save_is_not_offered_on_the_blank_form_the_screen_opens_with() => UiThread.RunAsync(async () =>
    {
        // The screen's real opening order: the blank form is costed at nothing before the defaults exist, and the
        // load then puts them on it. That isn't the user's change, so it mustn't read as something to save.
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 12m, OverheadPercent = 8m, AdministrativePercent = 5m, MarginPercent = 40m, VatPercent = 12m },
            Cancellation);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.Costing.EditingLaborPercent.Should().Be(12m, "a new product takes the defaults once they've loaded");
        (await CanSave(viewModel)).Should().BeFalse();
    });

    [Test]
    public Task Showing_the_tab_again_is_not_something_to_save() => UiThread.RunAsync(async () =>
    {
        // LoadCommand runs every time the tab is shown; it only merges in fresh data and re-points the pickers.
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();

        await viewModel.LoadCommand.Execute();

        (await CanSave(viewModel)).Should().BeFalse("a refresh isn't an edit");
    });

    [Test]
    public Task Undoing_Unlink_puts_the_link_back_and_redo_unlinks_again() => UiThread.RunAsync(async () =>
    {
        var (viewModel, doughId) = await OpenLinkedDoughProductAsync();

        await viewModel.Recipe.UnlinkCommand.Execute(viewModel.Recipe.UsageEdits.Single());

        viewModel.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        var linked = viewModel.Recipe.UsageEdits.Single();
        linked.IsLinked.Should().BeTrue();
        linked.SubAssemblyId.Should().Be(doughId);
        (await CanSave(viewModel)).Should().BeFalse("the recipe links the dough again, as saved, and its form shows it as it was");

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Undoing_Move_to_Sub_Assemblies_keeps_it_the_products_own_and_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1, isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var viewModel = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        viewModel.UsageForm.IsEditingOwnedUsage.Should().BeTrue();

        await viewModel.Recipe.MoveToSubAssembliesCommand.Execute(viewModel.UsageForm.EditingUsage!);
        viewModel.Recipe.UsageEdits.Single().IsReleasing.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Recipe.UsageEdits.Single().IsReleasing.Should().BeFalse();
        viewModel.UsageForm.IsEditingOwnedUsage.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeFalse();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Recipe.UsageEdits.Single().IsReleasing.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();
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
        var viewModel = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        viewModel.Recipe.PromptForMadeIngredient.RegisterHandler(interaction => interaction.SetOutput(new ChoicePromptResult("Cheese Filling", 0)));

        await viewModel.Recipe.MoveToInHouseCommand.Execute(viewModel.UsageForm.EditingUsage!);
        viewModel.Recipe.UsageEdits.Single().IsMovingToInHouse.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Recipe.UsageEdits.Single().IsMovingToInHouse.Should().BeFalse();
        viewModel.UsageForm.CanMoveEditingUsageToInHouse.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeFalse();

        viewModel.TryRedo().Should().BeTrue();
        viewModel.Recipe.UsageEdits.Single().ToMadeIngredient!.Name.Should().Be("Cheese Filling");
        (await CanSave(viewModel)).Should().BeTrue();
    });

    [Test]
    public Task Undoing_Make_a_Copy_stops_offering_Save() => UiThread.RunAsync(async () =>
    {
        var (viewModel, _) = await OpenLinkedDoughProductAsync();

        await viewModel.Recipe.MakeCopyCommand.Execute(viewModel.Recipe.UsageEdits.Single());
        (await CanSave(viewModel)).Should().BeTrue();

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Recipe.UsageEdits.Single().IsLinked.Should().BeTrue();
        (await CanSave(viewModel)).Should().BeFalse("opening the copy's form moved what Save measures against, and the undo moves it back");
    });

    [Test]
    public Task Undoing_to_a_form_with_typing_not_yet_applied_still_offers_Save() => UiThread.RunAsync(async () =>
    {
        // The other half of taking the settled form back from when the step was taken: it's the form as the
        // section showed, not as it was typed into, so typing an undo puts back still counts as a change.
        var (viewModel, _) = await OpenOneSubAssemblyProductAsync();
        viewModel.UsageForm.EditingUsageWeightPerPiece = 70m;

        Edit(viewModel, () => viewModel.EditingName = "Renamed");
        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingName.Should().Be("Ube Roll");
        viewModel.UsageForm.EditingUsageWeightPerPiece.Should().Be(70m);
        (await CanSave(viewModel)).Should().BeTrue("the 70 g typed into the form isn't in the recipe yet, and Save would write it");
    });

    /// <summary>Whether the Save button would be enabled: the command's own CanExecute, as the View binds it.</summary>
    private static async Task<bool> CanSave(ProductsViewModel viewModel) =>
        await viewModel.SaveCommand.CanExecute.FirstAsync();

    // ---- seeding and driving ----

    /// <summary>One finished change, as the Controls/ behaviours bracket a gesture in the app.</summary>
    private static void Edit(ProductsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    private sealed record SeededIngredients(int FlourId, int SugarId);

    private sealed record Seeded(int ProductId, int DoughRoleId, SeededIngredients Ingredients);

    /// <summary>A saved product with one Dough of 1,000 g of flour, opened with its form showing.</summary>
    private async Task<(ProductsViewModel ViewModel, Seeded Seeded)> OpenOneSubAssemblyProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));

        var viewModel = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1);
        return (viewModel, new Seeded(productId, doughRoleId, new SeededIngredients(flourId, sugarId)));
    }

    /// <summary>A product linking House Dough from the Sub-Assemblies screen (1,000 g of flour), opened with its form showing.</summary>
    private async Task<(ProductsViewModel ViewModel, int DoughId)> OpenLinkedDoughProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var doughId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)]));

        var viewModel = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 1 && vm.UsageForm.FormLines.Count == 1);
        return (viewModel, doughId);
    }

    /// <summary>The same product, with a second line so one can be the base and the other typed as a percentage.</summary>
    private async Task<(ProductsViewModel ViewModel, SeededIngredients Ingredients)> OpenTwoLineProductAsync()
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

        var viewModel = await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 2);
        viewModel.UsageForm.FormLines[0].IsFormulaBase = true;
        return (viewModel, new SeededIngredients(flourId, sugarId));
    }

    /// <summary>A product with a Dough and a Filling, so tabs can be moved between, and a box a piece if asked.</summary>
    private async Task<(ProductsViewModel ViewModel, int ProductId)> OpenTwoSectionProductAsync(bool withPackaging = false)
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

        var viewModel = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 2 && (vm.Packaging.LineEdits.Count > 0) == withPackaging);
        return (viewModel, productId);
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

    private async Task<ProductsViewModel> OpenAsync(int productId, Func<ProductsViewModel, bool> ready)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && ready(viewModel),
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.Recipe.UsageEdits.Count}, lines={viewModel.UsageForm.FormLines.Count}");

        viewModel.History.CanUndo.Should().BeFalse("the load itself isn't a change");
        return viewModel;
    }

    private ProductsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            CreateUnitOfWork(),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }
}
