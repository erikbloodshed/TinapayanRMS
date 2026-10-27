using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Lines keep the order the user drags them into. Every line table stores each line's position (SortOrder), written
/// from the order the lines are handed over in and read back in that order, then by Id: a sub-assembly's lines, a
/// packaging set's, a made ingredient's BOM, a product's packaging and a linked usage's add-ins.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class LineOrderPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRepository CreateSubAssemblyRepository() => new(CreateDbContextFactory());

    private async Task<int> AddPackagingAsync(string name)
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes", Type = RawMaterialCategoryType.Packaging };
        var material = new RawMaterial { Type = RawMaterialType.Packaging, Name = name, Category = category, BaseUnitId = unitId };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<(int SubAssemblyId, int RoleId, int FlourId, int SugarId, int SaltId)> SaveDoughAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var saltId = await AddIngredientAsync("Salt");
        var id = await CreateSubAssemblyRepository().SaveStandaloneAsync(
            new SubAssembly { Name = "Sweet Dough", RoleId = roleId }, [(flourId, 1000m), (sugarId, 200m), (saltId, 20m)], Cancellation);
        return (id, roleId, flourId, sugarId, saltId);
    }

    private async Task<IReadOnlyList<SubAssemblyLine>> SubAssemblyLinesAsync(int subAssemblyId) =>
        await new SubAssemblyLineRepository(CreateDbContextFactory()).GetForSubAssembliesAsync([subAssemblyId], Cancellation);

    [Fact]
    public async Task A_sub_assemblys_lines_come_back_in_the_order_they_were_saved_in()
    {
        var (id, roleId, flourId, sugarId, saltId) = await SaveDoughAsync();

        await CreateSubAssemblyRepository().SaveStandaloneAsync(
            new SubAssembly { Id = id, Name = "Sweet Dough", RoleId = roleId }, [(saltId, 20m), (flourId, 1000m), (sugarId, 200m)], Cancellation);

        var lines = await SubAssemblyLinesAsync(id);
        lines.Select(l => l.IngredientId).Should().Equal(saltId, flourId, sugarId);
        lines.Select(l => l.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Lines_are_read_in_their_stored_order_rather_than_the_order_their_rows_were_added_in()
    {
        var (id, _, flourId, sugarId, saltId) = await SaveDoughAsync();
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblyLines
                .Where(l => l.SubAssemblyId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.SortOrder, l => 10 - l.SortOrder), Cancellation);
        }

        (await SubAssemblyLinesAsync(id)).Select(l => l.IngredientId).Should().Equal(saltId, sugarId, flourId);
    }

    [Fact]
    public async Task Lines_saved_before_they_had_an_order_keep_the_order_they_were_added_in()
    {
        var (id, _, flourId, sugarId, saltId) = await SaveDoughAsync();
        await using (var dbContext = CreateDbContext())
        {
            // What the migration left every existing line at.
            await dbContext.SubAssemblyLines
                .Where(l => l.SubAssemblyId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.SortOrder, 0), Cancellation);
        }

        (await SubAssemblyLinesAsync(id)).Select(l => l.IngredientId).Should().Equal(flourId, sugarId, saltId);
    }

    [Fact]
    public async Task A_packaging_sets_lines_come_back_in_the_order_they_were_saved_in()
    {
        var roleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Box");
        var bagId = await AddPackagingAsync("Bag");
        var tagId = await AddPackagingAsync("Tag");
        var repository = CreateSubAssemblyRepository();
        var id = await repository.SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Set", RoleId = roleId }, [(boxId, null, 1m), (bagId, null, 8m), (tagId, null, 1m)], Cancellation);

        await repository.SaveStandalonePackagingAsync(
            new SubAssembly { Id = id, Name = "Gift Set", RoleId = roleId }, [(tagId, null, 1m), (boxId, null, 1m), (bagId, null, 8m)], Cancellation);

        var lines = await repository.GetPackagingLinesAsync(id, Cancellation);
        lines.Select(l => l.PackagingMaterialId).Should().Equal(tagId, boxId, bagId);
        lines.Select(l => l.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task A_made_ingredients_bom_comes_back_in_its_sub_assemblys_order_then_its_add_ins()
    {
        var flourId = await AddIngredientAsync("Flour");
        var butterId = await AddIngredientAsync("Butter");
        var sugarId = await AddIngredientAsync("Sugar");
        var vanillaId = await AddIngredientAsync("Vanilla");
        var cinnamonId = await AddIngredientAsync("Cinnamon");
        var madeId = await AddMadeIngredientAsync("Streusel", [(flourId, 100m), (butterId, 50m), (sugarId, 50m)]);
        await SetRecipeLinesAsync(madeId, [(sugarId, 50m), (flourId, 100m), (butterId, 50m)]);
        var rawMaterials = new RawMaterialRepository(CreateDbContextFactory());
        await rawMaterials.SaveAsync((await rawMaterials.GetByIdAsync(madeId, Cancellation))!, [], brandPrices: null, [(vanillaId, 2m), (cinnamonId, 1m)], Cancellation);

        var lines = await new IngredientBomLineRepository(CreateDbContextFactory()).GetForIngredientAsync(madeId, Cancellation);

        lines.Select(l => l.ComponentIngredientId).Should().Equal(sugarId, flourId, butterId, vanillaId, cinnamonId);
        lines.Select(l => (l.IsAddIn, l.SortOrder)).Should().Equal((false, 0), (false, 1), (false, 2), (true, 0), (true, 1));
    }

    [Fact]
    public async Task A_products_packaging_lines_come_back_in_the_order_they_were_saved_in()
    {
        var productId = await AddProductAsync("Ensaymada");
        var boxId = await AddPackagingAsync("Box");
        var bagId = await AddPackagingAsync("Bag");
        var ribbonId = await AddPackagingAsync("Ribbon");
        var repository = new PackagingLineRepository(CreateDbContextFactory());
        const PackagingLineAuthoredField qty = PackagingLineAuthoredField.QtyPerUnit;
        await repository.SetLinesAsync(productId, [(boxId, null, qty, 1m), (bagId, null, qty, 1m), (ribbonId, null, qty, 1m)], Cancellation);

        await repository.SetLinesAsync(productId, [(ribbonId, null, qty, 1m), (boxId, null, qty, 1m), (bagId, null, qty, 1m)], Cancellation);

        var lines = await repository.GetForProductAsync(productId, Cancellation);
        lines.Select(l => l.PackagingMaterialId).Should().Equal(ribbonId, boxId, bagId);
        lines.Select(l => l.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task A_linked_usages_add_ins_come_back_in_the_order_they_were_saved_in()
    {
        var (doughId, _, _, _, _) = await SaveDoughAsync();
        var productId = await AddProductAsync("Ube Cheese Pandesal");
        var ubeId = await AddIngredientAsync("Ube Flavour");
        var cheeseId = await AddIngredientAsync("Cheese");
        var pandanId = await AddIngredientAsync("Pandan Colour");

        LinkedUsageDraft Linked(params (int IngredientId, decimal Grams)[] addIns) =>
            new(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = addIns };

        await SaveRecipeAsync(productId, new RecipeDraft([], [Linked((ubeId, 20m), (cheeseId, 50m), (pandanId, 5m))]));
        await SaveRecipeAsync(productId, new RecipeDraft([], [Linked((pandanId, 5m), (ubeId, 20m), (cheeseId, 50m))]));

        var addIns = await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetAddInsForProductAsync(productId, Cancellation);
        addIns.Select(l => l.IngredientId).Should().Equal(pandanId, ubeId, cheeseId);
        addIns.Select(l => l.SortOrder).Should().Equal(0, 1, 2);
    }
}
