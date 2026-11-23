using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A section that goes whole into another of the product's, on the Products screen: the one it goes into weighs and costs
/// its whole batch too, shows it under its lines, and shares it out by its piece size; the section itself has no pieces, so
/// the costing counts it once. Its form says what it goes into, which is undone and saved as any of its fields is.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductsStateFoldTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Spanish Bread: a dough of 1,000 g of flour (50.00) at 70 g a piece, and a sponge of 400 g of flour (20.00) going
    /// into it, saved, and open in the form.
    /// </summary>
    private async Task<ProductForm> OpenSpongeIntoDoughAsync()
    {
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 70m),
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m) { GoesInto = RecipeDraft.SectionIdOf(0, 0) },
            ],
            []));

        var state = ProductsViews.Create(CreateDbContextFactory(), CreateUnitOfWork()).Form!;
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 2, () => $"ready={state.AreDetailsReady}, sections={state.Recipe.UsageEdits.Count}");
        return state;
    }

    private static SubAssemblyRecipeTab Tab(ProductForm state, string header) =>
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single(t => t.Header == header);

    [Test]
    public Task The_dough_counts_the_whole_sponge_and_the_costing_counts_it_once() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();
        var dough = Tab(state, "Dough").Usage;
        var sponge = Tab(state, "Sponge").Usage;

        sponge.GoesInto.Should().Be(dough.Key);
        var doughFigures = state.Recipe.Draft.FiguresOf(dough);
        doughFigures.TotalWeight.Should().Be(1400m);
        doughFigures.TotalCostAt(CostBasis.Buffered).Should().Be(70m);
        doughFigures.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.5m, "70 g of a 1,400 g, 70.00 batch");
        state.Recipe.Draft.FiguresOf(sponge).CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);

        state.Costing.Costs.SubAssemblies.Should().Be(3.5m, "the sponge is in the dough's piece, and only there");
        state.Costing.NoPieceSizeText.Should().BeNull("a section that goes into another needs no piece size");
    });

    [Test]
    public Task The_doughs_form_shows_the_sponge_under_its_lines_and_in_its_batch() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        var row = state.UsageForm.FormFoldedRows.Should().ContainSingle().Subject;
        (row.Name, row.Grams, row.Cost).Should().Be(("Sponge", 400m, 20m));
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,400 g");
        state.UsageForm.UsageFormBatchCostText.Should().Be("70.00");
        state.UsageForm.EditingUsageYield.Should().Be(20m, "1,400 g at 70 g a piece");
        state.UsageForm.Fold.GoesWhole.Should().BeFalse();
    });

    [Test]
    public Task The_sponges_form_says_where_it_goes_and_offers_no_piece_size() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        state.UsageForm.Fold.GoesWhole.Should().BeTrue();
        state.UsageForm.Fold.Target.Name.Should().Be("Dough");
        state.UsageForm.Fold.Options.Select(o => o.Name).Should().Equal(FoldTargetOption.None.Name, "Dough");
        state.UsageForm.UsageFormPieceSizeCaption.Should().StartWith("Its whole batch, 400 g, goes into Dough");
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("—");
    });

    [Test]
    public Task The_dough_cannot_go_into_the_sponge_that_goes_into_it() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        state.UsageForm.Fold.Options.Should().Equal(FoldTargetOption.None);
    });

    [Test]
    public Task Taking_the_sponge_out_of_the_dough_offers_Save_and_is_undone() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();
        var recorder = (IUndoRecorder)state;
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        recorder.BeginUndoStep();
        state.UsageForm.Fold.Pick(FoldTargetOption.None);
        recorder.EndUndoStep();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        Tab(state, "Sponge").Usage.GoesWhole.Should().BeFalse();
        state.UsageForm.FormFoldedRows.Should().BeEmpty();
        state.History.IsDirty.Should().BeTrue();
        state.Costing.NoPieceSizeText.Should().Contain("Sponge", "on its own it has no piece size yet");

        state.TryUndo().Should().BeTrue();

        Tab(state, "Sponge").Usage.GoesInto.Should().Be(Tab(state, "Dough").Usage.Key);
        state.Costing.NoPieceSizeText.Should().BeNull();
    });

    [Test]
    public Task Taking_the_sponge_out_saves_and_reads_back() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));
        state.UsageForm.Fold.Pick(FoldTargetOption.None);
        state.UsageForm.TypeYield(1m);
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.CountAsync(u => u.GoesIntoSubAssemblyId != null, Cancellation)).Should().Be(0);
    });

    [Test]
    public Task Removing_the_dough_leaves_the_sponge_with_pieces_of_its_own_to_size() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();

        var sponge = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        sponge.GoesWhole.Should().BeFalse();
        state.SuccessMessage.Should().Contain("Sponge went into Dough");
    });

    [Test]
    public Task The_section_left_after_removing_the_dough_is_edited_once_not_twice() => UiThread.RunAsync(async () =>
    {
        // The sponge's tab shows once the dough goes, its form on the sponge as freed, so applying it replaces that one.
        var state = await OpenSpongeIntoDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        await state.Recipe.RemoveUsageCommand.ExecuteAsync();

        state.UsageForm.EditingUsage.Should().BeSameAs(state.Recipe.UsageEdits.Single());
        state.UsageForm.Fold.GoesWhole.Should().BeFalse();
        state.UsageForm.TypeYield(4m);
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);

        state.Recipe.UsageEdits.Should().ContainSingle().Which.AuthoredValue.Should().Be(4m);
    });

    [Test]
    public Task A_pick_of_a_new_section_survives_the_save_that_writes_it() => UiThread.RunAsync(async () =>
    {
        // Saved apart, the sponge is sized by itself; a new dough is added, and the sponge's form picks it, unapplied.
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.Yield, 1m)], []));
        var state = ProductsViews.Create(CreateDbContextFactory(), CreateUnitOfWork()).Form!;
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"ready={state.AreDetailsReady}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == doughRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        state.UsageForm.TypeWeightPerPiece(70m);
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));
        state.UsageForm.Fold.Pick(state.UsageForm.Fold.Options.Single(o => o.Name == "Dough"));

        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && !state.History.IsDirty, () => $"ready={state.AreDetailsReady}, dirty={state.History.IsDirty}");

        // The dough is read back under its sub-assembly's id, and the sponge's form still says it goes into it.
        state.UsageForm.Fold.Target.Name.Should().Be("Dough");
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        Tab(state, "Sponge").Usage.GoesInto.Should().Be(Tab(state, "Dough").Usage.Key);
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_section_put_into_another_on_the_form_saves_as_going_into_it() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeIntoDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));
        state.UsageForm.Fold.Pick(FoldTargetOption.None);
        state.UsageForm.TypeYield(1m);
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        await state.SaveCommand.ExecuteAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));
        state.UsageForm.Fold.Pick(state.UsageForm.Fold.Options.Single(o => o.Name == "Dough"));
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        Tab(state, "Sponge").Usage.AuthoredValue.Should().Be(0m, "the piece size it had goes as it goes into the dough");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductSubAssemblyUsages.CountAsync(u => u.GoesIntoSubAssemblyId != null, Cancellation)).Should().Be(1);
    });
}
