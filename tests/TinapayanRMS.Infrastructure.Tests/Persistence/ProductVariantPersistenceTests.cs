using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A variant (IProductCloner.CreateVariantAsync): a new product that links everything its source's recipe uses, at the
/// source's piece sizes, and links its packaging, so it follows every change made there. Its packs and costing are
/// copied, as a clone's are.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductVariantPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory(), CreatePackagingLineRepository());

    private Task<int> CreateVariantAsync(int sourceProductId, string name) =>
        new ProductCloner(CreateDbContextFactory()).CreateVariantAsync(sourceProductId, name, Cancellation);

    /// <summary>A packaging material counted in pieces, costing <paramref name="cost"/> a piece.</summary>
    private async Task<int> AddPackagingAsync(string name, decimal cost = 30m)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes" };
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            Category = category,
            BaseUnitId = pieceId,
            CostPerBaseUnit = cost,
            BufferedCostPerBaseUnit = cost,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Fact]
    public async Task Links_every_sub_assembly_the_source_uses_at_its_piece_sizes_with_its_add_ins()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.3m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var flavourId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory())
            .SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = glazeRoleId }, [(sugarId, 100m)], Cancellation);
        var sourceId = await AddProductAsync("Ube Loaf Big");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.Yield, 10m) { ShrinkagePercent = 12m }],
            [
                new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m) { AddIns = [(flavourId, 20m)], ShrinkagePercent = 8m },
                new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m),
            ]));
        var fillingId = await SubAssemblyIdAsync(sourceId, fillingRoleId);

        var variantId = await CreateVariantAsync(sourceId, "Ube Loaf Small");

        var usages = await CreateUsageRepository().GetForProductAsync(variantId, Cancellation);
        // The shrinkage comes over with the piece size: the variant makes its sections the same way.
        usages.Select(u => (u.SubAssemblyId, u.AuthoredField, u.AuthoredValue, u.ShrinkagePercent)).Should().BeEquivalentTo(new[]
        {
            (fillingId, UsageAuthoredField.Yield, 10m, 12m),
            (doughId, UsageAuthoredField.WeightPerPiece, 60m, 8m),
            (glazeId, UsageAuthoredField.WeightPerPiece, 5m, 0m),
        });

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.CountAsync(Cancellation))
            .Should().Be(3, "a variant links every section; it makes none");
        (await dbContext.UsageAddInLines.AsNoTracking()
                .Where(l => l.Usage!.ProductId == variantId)
                .Select(l => new { l.Usage!.SubAssemblyId, l.IngredientId, l.Grams })
                .ToListAsync(Cancellation))
            .Should().ContainSingle().Which.Should().Be(new { SubAssemblyId = doughId, IngredientId = flavourId, Grams = 20m }, "the source's add-ins are the variant's own");
        (await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation))
            .Should().HaveCount(3, "the source's recipe is untouched");
    }

    [Fact]
    public async Task Follows_changes_made_to_the_source_recipe()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (sourceId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Ensaymada Big Dough", RoleId = roleId }, [(flourId, 1500m)], Cancellation);

        var usage = (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(doughId);
        usage.SubAssembly!.TotalWeight.Should().Be(1500m, "the variant reads the source's dough as it is now");
    }

    [Fact]
    public async Task Links_the_source_packaging_when_it_has_its_own_lines()
    {
        var boxId = await AddPackagingAsync("6-Piece Box");
        var sourceId = await AddProductAsync("Big");
        await CreatePackagingLineRepository().SetLinesAsync(sourceId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);

        var variantId = await CreateVariantAsync(sourceId, "Small");

        var link = (await CreatePackagingLinkRepository().GetForProductAsync(variantId, Cancellation))!;
        link.LinkedProductId.Should().Be(sourceId);
        link.LinkedSubAssemblyId.Should().BeNull();
        (await CreatePackagingLineRepository().GetForProductAsync(variantId, Cancellation)).Should().BeEmpty("the lines stay the source's");
        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(variantId, Cancellation))
            .Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
    }

    [Fact]
    public async Task Links_the_source_when_the_source_links_a_packaging_set_and_resolves_to_the_set()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box");
        var setId = await new SubAssemblyRepository(CreateDbContextFactory())
            .SaveStandalonePackagingAsync(new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var sourceId = await AddProductAsync("Big");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(sourceId, setId, Cancellation);

        var variantId = await CreateVariantAsync(sourceId, "Small");

        (await CreatePackagingLinkRepository().GetForProductAsync(variantId, Cancellation))!
            .LinkedProductId.Should().Be(sourceId, "a variant follows its source's packaging, whatever that links");
        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(variantId, Cancellation))
            .Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
    }

    [Fact]
    public async Task Has_no_packaging_link_when_the_source_has_no_packaging()
    {
        var sourceId = await AddProductAsync("Big");

        var variantId = await CreateVariantAsync(sourceId, "Small");

        (await CreatePackagingLinkRepository().GetForProductAsync(variantId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task Copies_the_quantity_required_costing_and_details()
    {
        var sourceId = await AddProductAsync("Ensaymada Big");
        int categoryId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new ProductCategory { Name = "Breads" };
            dbContext.ProductCategories.Add(category);
            var source = await dbContext.Products.SingleAsync(p => p.Id == sourceId, Cancellation);
            source.Category = category;
            source.Description = "Buttery and soft";
            source.QuantityRequired = 8;
            source.CurrentPrice = 35m;
            await dbContext.SaveChangesAsync(Cancellation);
            categoryId = category.Id;
        }

        var costingRepository = new CostingParametersRepository(CreateDbContextFactory());
        await costingRepository.SetAsync(sourceId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, vatPercent: 12m, Cancellation);

        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        var variant = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(variantId, Cancellation))!;
        variant.Name.Should().Be("Ensaymada Small");
        variant.CategoryId.Should().Be(categoryId);
        variant.Description.Should().Be("Buttery and soft");
        variant.QuantityRequired.Should().Be(8, "a variant sells the same way its source does until it's changed");
        variant.CurrentPrice.Should().Be(35m);

        var costing = (await costingRepository.GetForProductAsync(variantId, Cancellation))!;
        costing.MarginPercent.Should().Be(40m);
        costing.LaborPercent.Should().Be(5m);
    }

    [Fact]
    public async Task Keeps_two_sections_in_one_role_apart_by_their_names()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, pandesalDoughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m)]);
        var (sourceId, sourceDoughId) = await AddProductWithSubAssemblyAsync("Source", roleId, [(flourId, 1000m)]);
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [],
            [
                new LinkedUsageDraft(sourceDoughId, UsageAuthoredField.WeightPerPiece, 10m),
                new LinkedUsageDraft(pandesalDoughId, UsageAuthoredField.WeightPerPiece, 20m),
            ]));

        var variantId = await CreateVariantAsync(sourceId, "Variant");

        // "Dough: Source Dough" and "Dough: Pandesal Dough", on the source and the variant alike.
        (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation))
            .Select(u => u.SubAssemblyId).Should().BeEquivalentTo([sourceDoughId, pandesalDoughId]);
    }

    [Fact]
    public async Task The_source_dropping_a_sub_assembly_leaves_its_variant_linking_it()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (sourceId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)]);
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        await SaveRecipeAsync(sourceId, new RecipeDraft([], []));

        (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == doughId);
    }

    [Fact]
    public async Task Leaves_nothing_behind_when_the_recipe_is_refused()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var ubeId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Ube", RoleId = roleId }, [(flourId, 100m)], Cancellation);
        var variantUbeId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Variant Dough: Ube", RoleId = roleId }, [(flourId, 200m)], Cancellation);
        var sourceId = await AddProductAsync("Almond Cheese Bread");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [],
            [
                new LinkedUsageDraft(ubeId, UsageAuthoredField.WeightPerPiece, 10m),
                new LinkedUsageDraft(variantUbeId, UsageAuthoredField.WeightPerPiece, 10m),
            ]));

        // In a product called Variant, "Variant Dough: Ube" goes by Ube, as the other one does.
        var act = () => CreateVariantAsync(sourceId, "Variant");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*two Dough sub-assemblies called Ube*");
        await using var check = CreateDbContext();
        (await check.Products.AnyAsync(p => p.Name == "Variant", Cancellation)).Should().BeFalse("it's one transaction");
    }
}
