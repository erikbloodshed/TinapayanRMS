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
/// A section on another's lines, on the Products screen: picked from the other's ingredients list, in grams at its cost per
/// gram, so the other's batch, piece and the costing take it in; the section's own pieces are of what's left. The line is
/// undone, saved and read back as any line, and a section another uses can't be taken out of the recipe from under it.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductsStateSectionLineTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Spanish Bread: a dough of 1,000 g of flour (50.00) at 70 g a piece, with 300 g of a sponge of 400 g of flour (20.00)
    /// on its lines after the flour, the sponge one piece of what's left, saved and open in the form.
    /// </summary>
    private async Task<ProductForm> OpenSpongeInDoughAsync(decimal spongeGrams = 300m)
    {
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 70m)
                {
                    SectionLines = spongeGrams > 0 ? [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), spongeGrams, null, 1)] : [],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.Yield, 1m),
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

    private static LinePick SpongePick(ProductForm state) => state.UsageForm.Sections.Picks.Single(p => p.Name == "Sponge (section)");

    private async Task<List<(decimal Grams, int Position)>> SavedSectionLinesAsync()
    {
        await using var dbContext = CreateDbContext();
        return [.. (await dbContext.UsageSectionLines.ToListAsync(Cancellation)).Select(l => (l.Grams, l.Position))];
    }

    [Test]
    public Task The_dough_costs_its_grams_of_sponge_and_the_sponge_its_pieces_of_the_rest() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();
        var dough = Tab(state, "Dough").Usage;
        var sponge = Tab(state, "Sponge").Usage;

        dough.SectionLines.Should().Equal(new UsedSectionEdit(sponge.Key, 300m, null, 1));
        var doughFigures = state.Recipe.Draft.FiguresOf(dough);
        (doughFigures.TotalWeight, doughFigures.TotalCostAt(CostBasis.Buffered)).Should().Be((1300m, 65m));
        doughFigures.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.5m, "70 g of a 1,300 g, 65.00 batch");
        var spongeFigures = state.Recipe.Draft.FiguresOf(sponge);
        (spongeFigures.WeightPerPiece, spongeFigures.CostPerPieceAt(CostBasis.Buffered)).Should().Be((100m, 5m), "one piece of the 100 g left");

        state.Costing.Costs.SubAssemblies.Should().Be(8.5m);
        state.Costing.NoPieceSizeText.Should().BeNull();
    });

    [Test]
    public Task The_doughs_form_shows_the_sponge_among_its_lines_and_in_its_batch() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        state.UsageForm.FormLines.Select(r => r.Picked?.Name).Should().Equal("Flour", "Sponge (section)");
        var row = state.UsageForm.FormLines[1];
        (row.Grams, row.CostPerGram, row.Cost, row.IsFormulaBase).Should().Be((300m, 0.05m, 15m, false));
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,300 g");
        state.UsageForm.UsageFormBatchCostText.Should().Be("65.00");
        state.UsageForm.Sections.Picks.Select(p => p.Name).Should().StartWith("Sponge (section)").And.Contain("Flour");
    });

    [Test]
    public Task The_sponges_form_says_what_the_dough_takes_and_offers_no_loop() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();

        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        state.UsageForm.Sections.UsedElsewhere.Should().Be(300m);
        state.UsageForm.Sections.Users.Should().Equal("Dough");
        state.UsageForm.Sections.Picks.Should().NotContain(p => p.Section != null, "the dough uses the sponge, so it can't be on the sponge's lines");
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(100m, "one piece of the 100 g the dough leaves");
        state.UsageForm.UsageFormPieceSizeCaption.Should().Be("The yield stays at 1 piece of the 100 g that Dough leaves, if the batch changes.");
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("5.00");
    });

    [Test]
    public Task A_section_picked_on_the_doughs_lines_offers_Save_and_is_undone() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync(spongeGrams: 0m);
        var recorder = (IUndoRecorder)state;
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        recorder.BeginUndoStep();
        var row = new IngredientLineRow();
        row.Pick(SpongePick(state));
        row.Grams = 250m;
        state.UsageForm.FormLines.Add(row);
        recorder.EndUndoStep();
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        Tab(state, "Dough").Usage.SectionLines.Should().ContainSingle().Which.Grams.Should().Be(250m);
        state.History.IsDirty.Should().BeTrue();
        state.Recipe.Draft.FiguresOf(Tab(state, "Dough").Usage).TotalWeight.Should().Be(1250m);

        state.TryUndo().Should().BeTrue();

        Tab(state, "Dough").Usage.SectionLines.Should().BeEmpty();
    });

    [Test]
    public Task Grams_changed_and_the_line_taken_off_save_and_read_back() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        state.UsageForm.FormLines[1].Grams = 200m;
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await SavedSectionLinesAsync()).Should().Equal((200m, 1));

        await Settle.UntilAsync(state, () => state.AreDetailsReady && !state.History.IsDirty, () => $"dirty={state.History.IsDirty}");
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        state.UsageForm.FormLines.Remove(state.UsageForm.FormLines[1]);
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await SavedSectionLinesAsync()).Should().BeEmpty();
    });

    [Test]
    public Task A_section_another_uses_cannot_be_taken_out() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Sponge"));

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();

        state.Recipe.UsageEdits.Should().HaveCount(2);
        state.ErrorMessage.Should().Be("Dough uses Sponge. Take it off its lines first.");
    });

    [Test]
    public Task A_section_with_another_on_its_lines_cannot_move_to_Sub_Assemblies() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        await state.Recipe.MoveToSubAssembliesCommand.ExecuteAsync(Tab(state, "Dough").Usage);

        Tab(state, "Dough").Usage.IsReleasing.Should().BeFalse();
        state.ErrorMessage.Should().StartWith("Dough has another of this recipe's sections on its lines");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Taking_out_the_dough_takes_its_line_of_the_sponge_with_it() => UiThread.RunAsync(async () =>
    {
        var state = await OpenSpongeInDoughAsync();
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();

        var sponge = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        state.Recipe.Draft.FiguresOf(sponge).UsedElsewhereWeight.Should().Be(0m);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(400m, "its one piece is its whole batch again");
    });

    [Test]
    public Task A_new_section_on_anothers_lines_survives_the_save_that_writes_it() => UiThread.RunAsync(async () =>
    {
        // Saved apart, the dough is sized by itself; a new sponge is added, and the dough's form puts it on its lines, unapplied.
        await WithoutWastageAsync();
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 70m)], []));
        var state = ProductsViews.Create(CreateDbContextFactory(), CreateUnitOfWork()).Form!;
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"ready={state.AreDetailsReady}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == spongeRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 400m });
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        var row = new IngredientLineRow();
        row.Pick(SpongePick(state));
        row.Grams = 400m;
        state.UsageForm.FormLines.Add(row);

        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && !state.History.IsDirty, () => $"ready={state.AreDetailsReady}, dirty={state.History.IsDirty}");

        // The sponge is read back under its sub-assembly's id, and the dough's line of it with it.
        state.ErrorMessage.Should().BeNull();
        (await SavedSectionLinesAsync()).Should().Equal((400m, 1));
        Tab(state, "Dough").Usage.SectionLines.Should().ContainSingle().Which.Key.Should().Be(Tab(state, "Sponge").Usage.Key);
        state.Recipe.PickRecipeTab(Tab(state, "Dough"));
        state.UsageForm.FormLines.Select(r => r.Picked?.Name).Should().Equal("Flour", "Sponge (section)");
        state.History.IsDirty.Should().BeFalse();
    });
}
