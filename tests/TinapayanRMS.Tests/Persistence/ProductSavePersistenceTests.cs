using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// The Products screen's one Save (IProductRepository.SaveAsync): the product, its recipe (its own
/// sub-assemblies and the ones it links), its packaging lines and its costing in one transaction, so a
/// part that's rejected saves none of it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductSavePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedBoxAsync()
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Boxes" };
        var box = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "6-Piece Box",
            Category = category,
            BaseUnitId = pieceId,
            CostPerBaseUnit = 30m,
            BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);
        return box.Id;
    }

    [Fact]
    public async Task SaveAsync_saves_a_new_product_with_its_recipe_packaging_and_costing_at_once()
    {
        var doughRoleId = await AddRoleAsync("Dough", 1);
        var fillingRoleId = await AddRoleAsync("Filling", 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var creamId = await AddIngredientAsync("Cream", costPerGram: 0.2m);
        var (_, linkedFillingId) = await AddProductWithSubAssemblyAsync("Cream Loaf", "Butter Cream", fillingRoleId, [(creamId, 500m)]);
        var boxId = await SeedBoxAsync();
        var product = new Product { Name = "Cream Loaf Mini", Description = "A small cream loaf" };

        var id = await CreateProductRepository().SaveAsync(
            product,
            new RecipeDraft(
                [new OwnedSubAssemblyDraft(0, "Sweet Dough", doughRoleId, [(flourId, 1000m)], UsageAuthoredField.Yield, 40m)],
                [new LinkedUsageDraft(linkedFillingId, UsageAuthoredField.WeightPerPiece, 10m)]),
            [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)],
            new CostingParameters { LaborPercent = 6m, OverheadPercent = 5.5m, AdministrativePercent = 3.5m, MarginPercent = 30m, VatPercent = 12m },
            Cancellation);

        product.Id.Should().Be(0, "the caller's entity is only read");
        var saved = (await CreateProductRepository().GetByIdAsync(id, Cancellation))!;
        saved.Name.Should().Be("Cream Loaf Mini");
        saved.Description.Should().Be("A small cream loaf");
        saved.UpdatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));

        var usages = await CreateUsageRepository().GetForProductAsync(id, Cancellation);
        usages.Select(u => (u.SubAssembly!.Name, u.SubAssembly.OwnerProductId == id, u.AuthoredField, u.AuthoredValue)).Should().BeEquivalentTo(new[]
        {
            ("Sweet Dough", true, UsageAuthoredField.Yield, 40m),
            ("Butter Cream", false, UsageAuthoredField.WeightPerPiece, 10m),
        });
        (await CreatePackagingLineRepository().GetForProductAsync(id, Cancellation)).Should().ContainSingle(l => l.PiecesPerPackagingUnit == 6m);

        var costing = (await CreateCostingParametersRepository().GetForProductAsync(id, Cancellation))!;
        costing.LaborPercent.Should().Be(6m);
        costing.AdministrativePercent.Should().Be(3.5m);
        costing.VatPercent.Should().Be(12m);
    }

    [Fact]
    public async Task SaveAsync_leaves_the_parts_it_isnt_sent_alone()
    {
        var fillingRoleId = await AddRoleAsync("Filling", 1);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var boxId = await SeedBoxAsync();
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", "Custard", fillingRoleId, [(sugarId, 100m)]);
        await CreatePackagingLineRepository().SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)], Cancellation);
        await CreateCostingParametersRepository().SetAsync(productId, 5m, 3m, 1m, 40m, 12m, Cancellation);

        await CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Custard Ensaymada" },
            recipe: null,
            packagingLines: null,
            costing: null,
            Cancellation);

        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.Name.Should().Be("Custard Ensaymada");
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == custardId);
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation))!.LaborPercent.Should().Be(5m);
    }

    [Fact]
    public async Task SaveAsync_saves_nothing_when_a_part_is_rejected()
    {
        var fillingRoleId = await AddRoleAsync("Filling", 1);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.3m);
        var boxId = await SeedBoxAsync();
        var (productId, custardId) = await AddProductWithSubAssemblyAsync("Ensaymada", "Custard", fillingRoleId, [(sugarId, 100m)]);

        // The same box twice is rejected, after the name and the recipe were written.
        var act = () => CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Ube Ensaymada", Description = "Now with ube" },
            new RecipeDraft([new OwnedSubAssemblyDraft(custardId, "Ube Custard", fillingRoleId, [(ubeId, 200m)], UsageAuthoredField.WeightPerPiece, 15m)], []),
            [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m), (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 4m)],
            new CostingParameters { LaborPercent = 5m },
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var product = (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!;
        product.Name.Should().Be("Ensaymada");
        product.Description.Should().BeNull();
        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssembly!.Name.Should().Be("Custard");
        usage.AuthoredValue.Should().Be(10m);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.SingleAsync(Cancellation)).IngredientId.Should().Be(sugarId);
        (await dbContext.SubAssemblies.SingleAsync(Cancellation)).TotalCost.Should().Be(9m);
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_adds_no_product_or_sub_assembly_when_a_part_of_a_new_one_is_rejected()
    {
        var fillingRoleId = await AddRoleAsync("Filling", 1);
        var sugarId = await AddIngredientAsync("Sugar");
        var boxId = await SeedBoxAsync();

        var act = () => CreateProductRepository().SaveAsync(
            new Product { Name = "Custard Bun" },
            new RecipeDraft([new OwnedSubAssemblyDraft(0, "Custard", fillingRoleId, [(sugarId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []),
            [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m), (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 4m)],
            costing: null,
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblies.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_keeps_a_sub_assemblys_id_when_it_is_resaved_so_its_links_still_hold()
    {
        var doughRoleId = await AddRoleAsync("Dough", 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", "Sweet Dough", doughRoleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var smallId = await AddProductAsync("Ensaymada Small");
        await SaveRecipeAsync(smallId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        // The owner edits its dough: the smaller product, which links it, follows.
        await SaveRecipeAsync(bigId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(doughId, "Sweet Dough", doughRoleId, [(flourId, 1000m), (butterId, 100m)], UsageAuthoredField.WeightPerPiece, 60m)], []));

        var linked = (await CreateUsageRepository().GetForProductAsync(smallId, Cancellation)).Should().ContainSingle().Subject;
        linked.SubAssemblyId.Should().Be(doughId);
        linked.SubAssembly!.TotalWeight.Should().Be(1100m);
        linked.SubAssembly.TotalCost.Should().Be(90m); // 1000*0.05 + 100*0.4
    }
}
