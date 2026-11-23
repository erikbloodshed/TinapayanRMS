using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A form's Save says when the record it opened had last been saved, and is refused over a save made since, as another PC
/// on the same database makes one (SavedVersionRules). What moves that time is a save of the record itself, never a cost the
/// cascade moves.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class SavedVersionPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private readonly TestClock _clock = new(new DateTime(2026, 11, 18, 8, 0, 0));

    private ProductRepository Products => new(CreateDbContextFactory(_clock));

    private RawMaterialRepository RawMaterials => new(CreateDbContextFactory(_clock));

    private SubAssemblyRepository SubAssemblies => new(CreateDbContextFactory(_clock));

    private Task<int> SaveAsync(Product product) =>
        Products.SaveAsync(product, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private Task<int> SaveAsync(RawMaterial material) =>
        RawMaterials.SaveAsync(material, [], brandPrices: null, addIns: null, Cancellation);

    private async Task<Product> ProductAsync(int id) => (await Products.GetByIdAsync(id, Cancellation))!;

    private async Task<RawMaterial> RawMaterialAsync(int id) => (await RawMaterials.GetByIdAsync(id, Cancellation))!;

    private async Task<SubAssembly> SubAssemblyAsync(int id) => (await SubAssemblies.GetByIdsAsync([id], Cancellation)).Single();

    private void AMinuteLater() => _clock.Advance(TimeSpan.FromMinutes(1));

    [Test]
    public async Task A_product_saved_since_it_was_opened_refuses_the_save_and_keeps_the_one_made()
    {
        var id = await SaveAsync(new Product { Name = "Mamon" });
        var opened = await ProductAsync(id);

        AMinuteLater();
        var theirs = await ProductAsync(id);
        theirs.Name = "Mamon Classic";
        await SaveAsync(theirs);

        AMinuteLater();
        opened.CurrentPrice = 20m;
        var act = () => SaveAsync(opened);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Mamon Classic"));
        var stored = await ProductAsync(id);
        (stored.Name, stored.CurrentPrice).Should().Be(("Mamon Classic", (decimal?)null));
    }

    [Test]
    public async Task A_stamped_time_is_the_time_read_back_whatever_the_clock_read()
    {
        // A clock reading to the tick, where PostgreSQL keeps whole microseconds. A form's next Save is compared with the time
        // this one stamped, so what's stamped and what's read back have to be one time.
        var clock = new TestClock(new DateTime(2026, 11, 18, 8, 0, 0).AddTicks(1234567));
        var factory = CreateDbContextFactory(clock);
        var products = new ProductRepository(factory);
        await using var dbContext = await factory.CreateDbContextAsync(Cancellation);
        var stamped = dbContext.LocalNow();

        var id = await products.SaveAsync(new Product { Name = "Mamon" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        var saved = (await products.GetByIdAsync(id, Cancellation))!;

        saved.UpdatedAt.Should().Be(stamped);
        saved.CurrentPrice = 20m;
        var act = () => products.SaveAsync(saved, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_raw_material_saved_since_it_was_opened_refuses_the_save_and_keeps_the_one_made()
    {
        var id = await AddIngredientAsync("Flour", 0.05m);
        await SaveAsync(await RawMaterialAsync(id));
        var opened = await RawMaterialAsync(id);

        AMinuteLater();
        var theirs = await RawMaterialAsync(id);
        theirs.Name = "Bread Flour";
        await SaveAsync(theirs);

        AMinuteLater();
        opened.Name = "Cake Flour";
        var act = () => SaveAsync(opened);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Bread Flour"));
        (await RawMaterialAsync(id)).Name.Should().Be("Bread Flour");
    }

    [Test]
    public async Task A_cost_the_cascade_moves_isnt_a_save_of_the_ingredient()
    {
        var sugarId = await AddIngredientAsync("Sugar");
        await SetPriceAsync(sugarId, unitId: null, price: 0.05m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        var opened = await RawMaterialAsync(syrupId);

        // Sugar's new price cascades into the syrup while its form is open.
        await SetPriceAsync(sugarId, unitId: null, price: 0.09m);

        opened.Name = "Simple Syrup";
        var act = () => SaveAsync(opened);

        await act.Should().NotThrowAsync("nobody saved the syrup: only its cost followed the sugar");
        var saved = await RawMaterialAsync(syrupId);
        (saved.Name, saved.CostPerBaseUnit).Should().Be(("Simple Syrup", 0.09m));
    }

    [Test]
    public async Task A_sub_assembly_saved_since_it_was_opened_refuses_the_save_and_keeps_the_lines_saved()
    {
        var (id, flourId) = await AddDoughAsync();
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await SubAssemblies.SaveStandaloneAsync(await SubAssemblyAsync(id), [new RecipeLine(flourId, 200m)], Cancellation);

        AMinuteLater();
        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Dough"));
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.SingleAsync(l => l.SubAssemblyId == id, Cancellation)).Grams.Should().Be(200m);
    }

    [Test]
    public async Task A_sub_assembly_read_back_after_its_save_saves_again()
    {
        var (id, flourId) = await AddDoughAsync();

        AMinuteLater();
        var act = async () => await SubAssemblies.SaveStandaloneAsync(await SubAssemblyAsync(id), [new RecipeLine(flourId, 200m)], Cancellation);

        await act.Should().NotThrowAsync();
        (await SubAssemblyAsync(id)).UpdatedAt.Should().Be(_clock.Now);
    }

    [Test]
    public async Task A_rename_from_the_list_goes_through_and_a_form_opened_before_it_is_refused()
    {
        var (id, flourId) = await AddDoughAsync();
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await SubAssemblies.RenameAsync(id, "Sweet Dough", Cancellation);

        (await SubAssemblyAsync(id)).UpdatedAt.Should().Be(_clock.Now);
        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Sweet Dough"));
    }

    [Test]
    public async Task A_products_save_relining_a_sub_assembly_it_links_is_a_save_of_the_sub_assembly()
    {
        var (id, flourId) = await AddDoughAsync();
        var productId = await AddProductAsync("Cream Loaf");
        var opened = await SubAssemblyAsync(id);

        AMinuteLater();
        await Products.SaveAsync(
            await ProductAsync(productId),
            new RecipeDraft([], [new LinkedUsageDraft(id, UsageAuthoredField.WeightPerPiece, 50m) { Lines = [new RecipeLine(flourId, 500m)] }]),
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);

        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedVersionRules.SavedSinceText("Dough"));
    }

    [Test]
    public async Task A_variants_save_relining_its_sources_own_section_is_a_save_of_the_source()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var roleId = await AddRoleAsync("Topping", isProductOwned: true);
        var (sourceId, sectionId) = await AddProductWithSubAssemblyAsync("Loaf", roleId, [(flourId, 100m)]);
        var variantId = await new ProductCloner(CreateDbContextFactory(_clock)).CreateVariantAsync(sourceId, "Loaf Mini", Cancellation);

        // Its own piece size of the section leaves the source's recipe as it was.
        AMinuteLater();
        var before = (await ProductAsync(sourceId)).UpdatedAt;
        await Products.SaveAsync(
            await ProductAsync(variantId),
            new RecipeDraft([], [new LinkedUsageDraft(sectionId, UsageAuthoredField.WeightPerPiece, 20m)]),
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);
        (await ProductAsync(sourceId)).UpdatedAt.Should().Be(before);

        var opened = await ProductAsync(sourceId);
        opened.UpdatedAt = SavedVersionRules.OpenedAs(opened.UpdatedAt);
        AMinuteLater();
        await Products.SaveAsync(
            await ProductAsync(variantId),
            new RecipeDraft([], [new LinkedUsageDraft(sectionId, UsageAuthoredField.WeightPerPiece, 20m) { Lines = [new RecipeLine(flourId, 250m)] }]),
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);

        var act = () => SaveAsync(opened);
        await act.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Loaf"));
    }

    [Test]
    public async Task A_sub_assembly_no_save_has_stamped_saves_alone_and_is_refused_once_another_save_stamps_it()
    {
        var (id, flourId) = await AddDoughAsync();
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, (DateTime?)null), Cancellation);
        }

        var opened = await SubAssemblyAsync(id);
        opened.UpdatedAt.Should().BeNull();
        opened.UpdatedAt = SavedVersionRules.OpenedAs(null);

        // Nobody has saved it since: this save goes through, and stamps it.
        AMinuteLater();
        await SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 200m)], Cancellation);
        (await SubAssemblyAsync(id)).UpdatedAt.Should().Be(_clock.Now);

        // Opened the same way by a form still showing it as it was: that save is one made since.
        AMinuteLater();
        var act = () => SubAssemblies.SaveStandaloneAsync(opened, [new RecipeLine(flourId, 300m)], Cancellation);
        await act.Should().ThrowAsync<SavedSinceException>();
    }

    [Test]
    public async Task A_products_rename_and_moves_from_the_list_are_refused_over_a_save_made_since_the_form_opened_it()
    {
        var id = await SaveAsync(new Product { Name = "Mamon", Stage = ProductStage.Innovation });
        var otherId = await SaveAsync(new Product { Name = "Ensaymada", Stage = ProductStage.Innovation });
        SavedVersionCheck[] showing = [new(id, (await ProductAsync(id)).UpdatedAt!.Value)];

        AMinuteLater();
        var theirs = await ProductAsync(id);
        theirs.Name = "Mamon Classic";
        await SaveAsync(theirs);

        var rename = () => Products.RenameAsync(id, "Mamon Tostado", showing, Cancellation);
        var move = () => Products.MoveAsync([new ProductMove(otherId, ProductStage.Production, null), new ProductMove(id, ProductStage.Production, null)], showing, Cancellation);
        var promote = () => Products.MoveToStageAsync([otherId, id], ProductStage.Production, showing, Cancellation);

        await rename.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Mamon Classic"));
        await move.Should().ThrowAsync<SavedSinceException>();
        await promote.Should().ThrowAsync<SavedSinceException>();

        var stored = await ProductAsync(id);
        (stored.Name, stored.Stage).Should().Be(("Mamon Classic", ProductStage.Innovation));
        (await ProductAsync(otherId)).Stage.Should().Be(ProductStage.Innovation, "a move refused moves nothing");

        // Another product isn't checked, and the one showing is once the form has the time it's saved at.
        await Products.RenameAsync(otherId, "Ensaymada Especial", showing, Cancellation);
        await Products.RenameAsync(id, "Mamon Tostado", [new SavedVersionCheck(id, stored.UpdatedAt!.Value)], Cancellation);
        (await ProductAsync(id)).Name.Should().Be("Mamon Tostado");
    }

    // A record view has a tab for each product open: a move of several sends each tab's check, and one product saved
    // elsewhere since its tab took it refuses the whole move, though another tab's product is the one picked first.
    [Test]
    public async Task A_move_of_products_two_tabs_hold_with_changes_is_refused_over_a_save_of_either()
    {
        var firstId = await SaveAsync(new Product { Name = "Mamon", Stage = ProductStage.Innovation });
        var secondId = await SaveAsync(new Product { Name = "Ensaymada", Stage = ProductStage.Innovation });
        SavedVersionCheck[] tabs =
        [
            new(firstId, (await ProductAsync(firstId)).UpdatedAt!.Value),
            new(secondId, (await ProductAsync(secondId)).UpdatedAt!.Value),
        ];

        AMinuteLater();
        var theirs = await ProductAsync(secondId);
        theirs.Description = "Saved on the other PC";
        await SaveAsync(theirs);

        var move = () => Products.MoveAsync(
            [new ProductMove(firstId, ProductStage.Production, null), new ProductMove(secondId, ProductStage.Production, null)], tabs, Cancellation);

        await move.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Ensaymada"));
        (await ProductAsync(firstId)).Stage.Should().Be(ProductStage.Innovation, "a move refused moves nothing");
        (await ProductAsync(secondId)).Stage.Should().Be(ProductStage.Innovation);
    }

    [Test]
    public async Task A_folder_moved_to_the_other_stage_is_refused_over_a_save_of_the_product_the_form_shows_in_it()
    {
        var folders = new ProductFolderRepository(CreateDbContextFactory(_clock));
        var folder = new ProductFolder { Name = "Breads", Stage = ProductStage.Innovation };
        await folders.AddAsync(folder, Cancellation);
        var id = await SaveAsync(new Product { Name = "Mamon", Stage = ProductStage.Innovation, FolderId = folder.Id });
        SavedVersionCheck[] showing = [new(id, (await ProductAsync(id)).UpdatedAt!.Value)];

        AMinuteLater();
        var theirs = await ProductAsync(id);
        theirs.Description = "Saved on the other PC";
        await SaveAsync(theirs);

        var act = () => folders.MoveAsync(new ProductFolderMove(folder.Id, ProductStage.Production, null), showing, Cancellation);

        await act.Should().ThrowAsync<SavedSinceException>();
        (await ProductAsync(id)).Stage.Should().Be(ProductStage.Innovation);
    }

    // A folder's move takes every tab's check, as its products aren't known before it: each it restages is checked against
    // its own tab's, so a save from another PC of any of them refuses it.
    [Test]
    public async Task A_folder_moved_to_the_other_stage_is_refused_over_a_save_of_any_product_a_tab_holds_in_it()
    {
        var folders = new ProductFolderRepository(CreateDbContextFactory(_clock));
        var folder = new ProductFolder { Name = "Breads", Stage = ProductStage.Innovation };
        await folders.AddAsync(folder, Cancellation);
        var firstId = await SaveAsync(new Product { Name = "Mamon", Stage = ProductStage.Innovation, FolderId = folder.Id });
        var secondId = await SaveAsync(new Product { Name = "Ensaymada", Stage = ProductStage.Innovation, FolderId = folder.Id });
        var elsewhereId = await SaveAsync(new Product { Name = "Pandesal", Stage = ProductStage.Innovation });
        SavedVersionCheck[] tabs =
        [
            new(elsewhereId, (await ProductAsync(elsewhereId)).UpdatedAt!.Value),
            new(firstId, (await ProductAsync(firstId)).UpdatedAt!.Value),
            new(secondId, (await ProductAsync(secondId)).UpdatedAt!.Value),
        ];

        AMinuteLater();
        var theirs = await ProductAsync(secondId);
        theirs.Description = "Saved on the other PC";
        await SaveAsync(theirs);

        var act = () => folders.MoveAsync(new ProductFolderMove(folder.Id, ProductStage.Production, null), tabs, Cancellation);

        await act.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Ensaymada"));
        (await ProductAsync(firstId)).Stage.Should().Be(ProductStage.Innovation);
        (await ProductAsync(secondId)).Stage.Should().Be(ProductStage.Innovation);
    }

    [Test]
    public async Task A_raw_materials_rename_and_move_from_the_list_are_refused_over_a_save_made_since_the_form_opened_it()
    {
        var id = await AddIngredientAsync("Flour", 0.05m);
        await SaveAsync(await RawMaterialAsync(id));
        var opened = await RawMaterialAsync(id);
        SavedVersionCheck[] showing = [new(id, opened.UpdatedAt!.Value)];
        int categoryId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new RawMaterialCategory { Name = "Flours", Type = RawMaterialCategoryType.Ingredient };
            dbContext.RawMaterialCategories.Add(category);
            await dbContext.SaveChangesAsync(Cancellation);
            categoryId = category.Id;
        }

        AMinuteLater();
        var theirs = await RawMaterialAsync(id);
        theirs.Name = "Bread Flour";
        await SaveAsync(theirs);

        var rename = () => RawMaterials.RenameAsync(id, "Cake Flour", showing, Cancellation);
        var move = () => RawMaterials.MoveAsync([new RawMaterialMove(id, categoryId)], showing, Cancellation);

        await rename.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Bread Flour"));
        await move.Should().ThrowAsync<SavedSinceException>();

        var stored = await RawMaterialAsync(id);
        (stored.Name, stored.CategoryId).Should().Be(("Bread Flour", opened.CategoryId));
    }

    [Test]
    public async Task A_sub_assemblys_rename_and_move_from_the_list_are_refused_over_a_save_made_since_the_form_opened_it()
    {
        var (id, flourId) = await AddDoughAsync();
        var opened = await SubAssemblyAsync(id);
        SavedVersionCheck[] showing = [new(id, opened.UpdatedAt!.Value)];
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);

        AMinuteLater();
        await SubAssemblies.SaveStandaloneAsync(await SubAssemblyAsync(id), [new RecipeLine(flourId, 200m)], Cancellation);

        var rename = () => SubAssemblies.RenameAsync(id, "Sweet Dough", showing, Cancellation);
        var move = () => SubAssemblies.MoveAsync([new SubAssemblyMove(id, fillingRoleId)], showing, Cancellation);

        await rename.Should().ThrowAsync<SavedSinceException>().WithMessage(SavedVersionRules.SavedSinceText("Dough"));
        await move.Should().ThrowAsync<SavedSinceException>();

        var stored = await SubAssemblyAsync(id);
        (stored.Name, stored.RoleId).Should().Be(("Dough", opened.RoleId));
    }

    private async Task<(int Id, int FlourId)> AddDoughAsync()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var roleId = await AddRoleAsync("Base");
        var id = await SubAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Dough", RoleId = roleId }, [new RecipeLine(flourId, 100m)], Cancellation);
        return (id, flourId);
    }
}
