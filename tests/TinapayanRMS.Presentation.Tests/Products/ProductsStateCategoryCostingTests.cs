using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A category's own costing percentages (Settings → Product Categories), which a new product in it starts with in place of
/// Settings' (CostingRules.DefaultCostingFor) and Apply Costing Defaults gives its products; and the Warn Below %,
/// which the list's short-of-target flag waits for, a category's in place of Settings'.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class ProductsStateCategoryCostingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly (decimal, decimal, decimal, decimal) Settings = (35m, 25m, 15m, 30m);

    [Test]
    public Task A_new_product_takes_its_categorys_costing_as_it_is_picked_and_an_undo_puts_both_back() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m, MarkupPercent = 80m });
        await AddCategoryAsync(new ProductCategory { Name = "Breads" });
        var state = await LoadedStateAsync();
        await state.NewCommand.ExecuteAsync();
        state.Costing.Percentages.Should().Be(Settings, "uncategorized, it starts with Settings'");

        Pick(state, "Cakes");
        state.Costing.Percentages.Should().Be((50m, 25m, 15m, 80m), "Cakes sets its labor and markup, Settings the rest");

        Pick(state, "Breads");
        state.Costing.Percentages.Should().Be(Settings, "Breads sets none of its own");

        await state.UndoCommand.ExecuteAsync();
        state.EditingCategory!.Name.Should().Be("Cakes");
        state.Costing.Percentages.Should().Be((50m, 25m, 15m, 80m), "the undo puts the category and the costing it brought back together");

        state.EditingName = "Ube Cake";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.CostingParameters.SingleAsync(Cancellation);
        (saved.LaborPercent, saved.OverheadPercent, saved.AdministrativePercent, saved.MarkupPercent).Should().Be((50m, 25m, 15m, 80m));
    });

    [Test]
    public Task A_new_products_costing_typed_into_stays_when_its_category_changes() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m });
        var state = await LoadedStateAsync();
        await state.NewCommand.ExecuteAsync();
        state.Costing.EditingLaborPercent = 40m;

        Pick(state, "Cakes");

        state.Costing.Percentages.Should().Be((40m, 25m, 15m, 30m), "what was typed is the user's");
    });

    [Test]
    public Task A_saved_products_costing_is_its_own_whatever_category_it_is_put_in() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m });
        var productId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pandesal" }, recipe: null, packagingLink: null, packagingLines: null,
            costing: new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 30m }, Cancellation);
        var state = await LoadedStateAsync();
        state.OpenRecord(productId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.SelectedProduct?.Id == productId, () => $"ready={state.AreDetailsReady}");

        Pick(state, "Cakes");

        state.Costing.Percentages.Should().Be(Settings);
    });

    [Test]
    public Task A_category_written_by_anything_but_a_pick_leaves_a_new_products_costing_alone() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m });
        var state = await LoadedStateAsync();
        await state.NewCommand.ExecuteAsync();

        state.EditingCategory = state.Categories.Single(c => c.Name == "Cakes");

        state.Costing.Percentages.Should().Be(Settings, "only the user's pick brings a category's costing");
    });

    [Test]
    public Task A_new_product_still_on_its_categorys_costing_follows_a_change_to_it_in_Settings_with_the_next_load() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        var cakesId = await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m });
        var state = await LoadedStateAsync();
        await state.NewCommand.ExecuteAsync();
        Pick(state, "Cakes");
        state.Costing.EditingOverheadPercent = 20m;
        state.Costing.EditingOverheadPercent = 25m;

        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = cakesId, Name = "Cakes", LaborPercent = 45m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();

        state.Costing.Percentages.Should().Be((45m, 25m, 15m, 30m), "it still showed Cakes' costing, so it takes Cakes' new one");
        await state.UndoCommand.ExecuteAsync();
        state.EditingCategory.Should().BeNull("the undo takes the pick back");
        state.Costing.Percentages.Should().Be(Settings, "uncategorized, it's on Settings' own again");
        await state.RedoCommand.ExecuteAsync();
        state.Costing.Percentages.Should().Be((45m, 25m, 15m, 30m), "a redo of the pick lands on Cakes' new costing, not its old one");
    });

    [Test]
    public Task Apply_Costing_Defaults_gives_each_product_its_categorys_and_asks_with_a_line_for_each() => UiThread.RunAsync(async () =>
    {
        await SetDefaultsAsync();
        var cakesId = await AddCategoryAsync(new ProductCategory { Name = "Cakes", LaborPercent = 50m, MarkupPercent = 80m });
        var breadsId = await AddCategoryAsync(new ProductCategory { Name = "Breads" });
        var products = new ProductRepository(CreateDbContextFactory());
        var ubeCakeId = await products.SaveAsync(new Product { Name = "Ube Cake", CategoryId = cakesId }, null, null, null, null, Cancellation);
        var pandesalId = await products.SaveAsync(new Product { Name = "Pandesal", CategoryId = breadsId }, null, null, null, null, Cancellation);
        var state = await LoadedStateAsync();
        string? asked = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(true);
        });
        var list = state.View.List;
        list.SelectedTreeNodes.Add(list.ProductTree.Find(ubeCakeId)!);
        list.SelectedTreeNodes.Add(list.ProductTree.Find(pandesalId)!);

        var apply = list.TreeMenuFor(list.ProductTree.Find(pandesalId)).Single(i => i.Header == "Apply Costing Defaults to 2 Products…");
        await ((ScreenCommand<IReadOnlyList<Product>>)apply.Command!).ExecuteAsync((IReadOnlyList<Product>)apply.Parameter!);

        state.ErrorMessage.Should().BeNull();
        asked.Should().Be(
            "Set the costing of Ube Cake and Pandesal to their categories' costing defaults?\n\n"
            + "Cakes: labor 50%, overhead 25%, administrative 15% and markup 80%.\n"
            + "Breads: labor 35%, overhead 25%, administrative 15% and markup 30%.\n\n"
            + "A version is recorded of each one this changes.");
        await using var dbContext = CreateDbContext();
        (await dbContext.CostingParameters.Where(c => c.ProductId == ubeCakeId).Select(c => c.LaborPercent).SingleAsync(Cancellation)).Should().Be(50m);
        (await dbContext.CostingParameters.Where(c => c.ProductId == pandesalId).Select(c => c.LaborPercent).SingleAsync(Cancellation)).Should().Be(35m);
    });

    [Test]
    public Task The_lists_short_of_target_flag_waits_for_the_Warn_Below_Markup_its_categorys_or_Settings() => UiThread.RunAsync(async () =>
    {
        // Flour at ₱20 a piece and a 30% target, no VAT or wastage: ₱25.70 earns 28.5%.
        var defaults = new CostingDefaultsRepository(CreateDbContextFactory());
        await defaults.SetAsync(new CostingDefaults { WastagePercent = 0m }, Cancellation);
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.2m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [new RecipeLine(flourId, 100m)], weightPerPiece: 100m);
        await new CostingParametersRepository(CreateDbContextFactory()).SetAsync(productId, 0m, 0m, 0m, 30m, Cancellation);
        var breadsId = await AddCategoryAsync(new ProductCategory { Name = "Breads" });
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Products.Where(p => p.Id == productId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.CurrentPrice, 25.70m).SetProperty(p => p.CategoryId, breadsId), Cancellation);
        }

        var state = await LoadedStateAsync();
        bool? Flagged() => state.View.List.ProductTree.Find(productId)?.IsBelowTargetMarkup;
        await Settle.UntilAsync(state, () => Flagged() == true, () => "never flagged");

        await defaults.SetAsync(new CostingDefaults { WastagePercent = 0m, MarkupFloorPercent = 28m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => Flagged() == false, () => "still flagged, though it earns over Settings' 28%");

        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = breadsId, Name = "Breads", MarkupFloorPercent = 29m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => Flagged() == true, () => "not flagged under Breads' own 29%");
    });

    /// <summary>The Category picker's pick, an undo step of its own, as the page makes it.</summary>
    private static void Pick(ProductForm state, string category)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.Draft.PickCategory(state.Categories.Single(c => c.Name == category), isNewProduct: state.EditingId == 0);
        recorder.EndUndoStep();
    }

    private Task SetDefaultsAsync() => new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
        new CostingDefaults { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 30m }, Cancellation);

    private async Task<int> AddCategoryAsync(ProductCategory category)
    {
        await new ProductCategoryRepository(CreateDbContextFactory()).AddAsync(category, Cancellation);
        return category.Id;
    }

    private async Task<ProductForm> LoadedStateAsync()
    {
        var state = ProductsViews.Create(CreateDbContextFactory(), CreateUnitOfWork(), null).Form!;
        await state.LoadCommand.ExecuteAsync();
        return state;
    }
}
