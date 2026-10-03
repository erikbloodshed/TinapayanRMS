using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Product.QuantityRequired: how many finished pieces make one sellable unit, which replaced ProductPack on
/// 2026-09-20. The recipe stays per piece; the costing multiplies by it, and the packaging lines are already
/// for one whole unit. A "Box of 8" is a variant of the single with a quantity of 8 and a packaging set of
/// its own, not a pack row hanging off it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductQuantityRequiredPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    private ProductCloner CreateCloner() => new(CreateDbContextFactory());

    private async Task<int> AddPackagingAsync(string name, decimal costPerBaseUnit, int categoryId)
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            CategoryId = categoryId,
            BaseUnitId = unitId,
            CostPerBaseUnit = costPerBaseUnit,
            BufferedCostPerBaseUnit = costPerBaseUnit,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Fact]
    public async Task A_product_saved_without_one_holds_a_single_piece()
    {
        var id = await CreateProductRepository().SaveAsync(
            new Product { Name = "Ensaymada" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await CreateProductRepository().GetByIdAsync(id, Cancellation))!.QuantityRequired.Should().Be(1);
    }

    [Fact]
    public async Task Saves_and_reads_back_a_quantity_of_more_than_one_piece()
    {
        var repository = CreateProductRepository();
        var id = await repository.SaveAsync(
            new Product { Name = "Ensaymada Box of 8", QuantityRequired = 8 },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await repository.GetByIdAsync(id, Cancellation))!.QuantityRequired.Should().Be(8);

        await repository.SaveAsync(
            new Product { Id = id, Name = "Ensaymada Box of 12", QuantityRequired = 12 },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await repository.GetByIdAsync(id, Cancellation))!.QuantityRequired.Should().Be(12);
    }

    [Fact]
    public async Task A_quantity_of_no_pieces_is_refused_by_the_database()
    {
        // CK_Products_QuantityRequired, beside the rule ProductValidator applies on the screen.
        var act = () => CreateProductRepository().SaveAsync(
            new Product { Name = "Nothing At All", QuantityRequired = 0 },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_clone_and_a_variant_both_take_the_sources_quantity()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (sourceId, _) = await AddProductWithSubAssemblyAsync("Ensaymada Box of 8", roleId, [(flourId, 1000m)]);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == sourceId, Cancellation)).QuantityRequired = 8;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var cloneId = await CreateCloner().CloneAsync(sourceId, "Ensaymada Box of 8 Copy", Cancellation);
        var variantId = await CreateCloner().CreateVariantAsync(sourceId, "Ensaymada Box of 8 Variant", Cancellation);

        var repository = CreateProductRepository();
        (await repository.GetByIdAsync(cloneId, Cancellation))!.QuantityRequired.Should().Be(8);
        (await repository.GetByIdAsync(variantId, Cancellation))!.QuantityRequired.Should().Be(8);
    }

    [Fact]
    public async Task A_boxed_variant_costs_its_pieces_by_the_quantity_plus_one_whole_packaging_set()
    {
        // The worked example from the rework: Ensaymada at 10 g of a 1,000 g / P50 dough is P0.50 of recipe a
        // piece. "Ensaymada Box of 8" is a variant of it — the recipe linked, so still 10 g a piece — with a
        // quantity of 8 and a packaging set of its own holding the 8 bags and the one box. So its materials
        // come to 8 x P0.50 + 8 x P0.25 + P12 = P18, and not eight boxes over.
        var doughRoleId = await AddRoleAsync("Dough");
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var packagingCategoryId = await AddPackagingCategoryAsync();
        var bagId = await AddPackagingAsync("Cello Bag", 0.25m, packagingCategoryId);
        var boxId = await AddPackagingAsync("8-Piece Box", 12m, packagingCategoryId);

        var (singleId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)]);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Ensaymada Box of 8 Packaging", RoleId = packagingRoleId },
            [(bagId, null, 8m), (boxId, null, 1m)],
            Cancellation);

        var variantId = await CreateCloner().CreateVariantAsync(singleId, "Ensaymada Box of 8", Cancellation);
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(variantId, setId, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == variantId, Cancellation)).QuantityRequired = 8;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var usages = await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(variantId, Cancellation);
        var usageCosts = usages
            .Select(u =>
            {
                var subAssembly = u.SubAssembly!;
                var (weightPerPiece, _) = ProductRules.ResolvePieceSize(u.AuthoredField, u.AuthoredValue, subAssembly.TotalWeight);
                return ProductRules.CalculateUsageCostPerPiece(subAssembly.TotalCost, weightPerPiece, subAssembly.TotalWeight);
            })
            .ToList();
        usageCosts.Should().ContainSingle().Which.Should().Be(0.50m, "the recipe is linked, so a piece is still 10 g of the dough");

        var lines = await CreatePackagingLineRepository().GetEffectiveForProductAsync(variantId, Cancellation);
        var packagingCosts = lines
            .Select(l => ProductRules.CalculatePackagingLineCost(l.PackagingMaterial!.BufferedCostPerBaseUnit, l.QtyPerUnit, l.Unit?.BaseUnitsPerUnit))
            .ToList();
        packagingCosts.Should().Equal(2m, 12m);

        var quantityRequired = (await CreateProductRepository().GetByIdAsync(variantId, Cancellation))!.QuantityRequired;
        ProductRules.CalculateMaterialCost(usageCosts, quantityRequired, packagingCosts).Should().Be(18m);

        // And the single it came from is untouched: one piece, no packaging of its own.
        var singleLines = await CreatePackagingLineRepository().GetEffectiveForProductAsync(singleId, Cancellation);
        singleLines.Should().BeEmpty();
        (await CreateProductRepository().GetByIdAsync(singleId, Cancellation))!.QuantityRequired.Should().Be(1);
    }
}
