using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

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
            [new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.Yield, 10m)],
            [
                new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m) { AddIns = [(flavourId, 20m)] },
                new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m),
            ]));
        var fillingId = await OwnedSubAssemblyIdAsync(sourceId, fillingRoleId);

        var variantId = await CreateVariantAsync(sourceId, "Ube Loaf Small");

        var usages = await CreateUsageRepository().GetForProductAsync(variantId, Cancellation);
        usages.Select(u => (u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)).Should().BeEquivalentTo(new[]
        {
            (fillingId, UsageAuthoredField.Yield, 10m),
            (doughId, UsageAuthoredField.WeightPerPiece, 60m),
            (glazeId, UsageAuthoredField.WeightPerPiece, 5m),
        });

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.CountAsync(s => s.OwnerProductId == variantId, Cancellation))
            .Should().Be(0, "a variant links every section; it owns none");
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

        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(doughId, roleId, [(flourId, 1500m)], UsageAuthoredField.WeightPerPiece, 60m)],
            []));

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
            .SaveStandalonePackagingAsync(new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, 1m)], Cancellation);
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
    public async Task Copies_the_packs_costing_and_details()
    {
        var boxId = await AddPackagingAsync("Pack Box", 12m);
        var sourceId = await AddProductAsync("Ensaymada Big");
        int categoryId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new ProductCategory { Name = "Breads" };
            dbContext.ProductCategories.Add(category);
            var source = await dbContext.Products.SingleAsync(p => p.Id == sourceId, Cancellation);
            source.Category = category;
            source.Description = "Buttery and soft";
            source.CurrentPrice = 35m;
            await dbContext.SaveChangesAsync(Cancellation);
            categoryId = category.Id;
        }

        var packRepository = new ProductPackRepository(CreateDbContextFactory());
        await packRepository.SetPacksAsync(
            sourceId,
            [new ProductPackDraft(0, "Pack of 6", 6, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)])],
            Cancellation);
        var costingRepository = new CostingParametersRepository(CreateDbContextFactory());
        await costingRepository.SetAsync(sourceId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, vatPercent: 12m, Cancellation);

        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        var variant = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(variantId, Cancellation))!;
        variant.Name.Should().Be("Ensaymada Small");
        variant.CategoryId.Should().Be(categoryId);
        variant.Description.Should().Be("Buttery and soft");
        variant.CurrentPrice.Should().Be(35m);

        var pack = (await packRepository.GetForProductAsync(variantId, Cancellation)).Should().ContainSingle().Subject;
        pack.Name.Should().Be("Pack of 6");
        pack.Lines.Should().ContainSingle().Which.PackagingMaterialId.Should().Be(boxId);
        pack.Id.Should().NotBe((await packRepository.GetForProductAsync(sourceId, Cancellation)).Single().Id, "packs are never linked");

        var costing = (await costingRepository.GetForProductAsync(variantId, Cancellation))!;
        costing.MarginPercent.Should().Be(40m);
        costing.LaborPercent.Should().Be(5m);
    }

    [Fact]
    public async Task Keeps_two_sections_in_one_role_apart_by_their_owners_names()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, pandesalDoughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m)]);
        var (sourceId, sourceDoughId) = await AddProductWithSubAssemblyAsync("Source", roleId, [(flourId, 1000m)]);
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(sourceDoughId, roleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 10m)],
            [new LinkedUsageDraft(pandesalDoughId, UsageAuthoredField.WeightPerPiece, 20m)]));

        var variantId = await CreateVariantAsync(sourceId, "Variant");

        // "Dough" and "Dough: Pandesal" on the source; "Dough: Source" and "Dough: Pandesal" on the variant.
        (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation))
            .Select(u => u.SubAssemblyId).Should().BeEquivalentTo([sourceDoughId, pandesalDoughId]);
    }

    [Fact]
    public async Task The_source_cant_drop_a_sub_assembly_its_variant_links()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (sourceId, _) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)]);
        await CreateVariantAsync(sourceId, "Ensaymada Small");

        var act = () => SaveRecipeAsync(sourceId, new RecipeDraft([], []));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ensaymada Small*");
    }

    [Fact]
    public async Task Leaves_nothing_behind_when_the_recipe_is_refused()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (sourceId, _) = await AddProductWithSubAssemblyAsync("Almond Cheese Bread", roleId, [(flourId, 1000m)]);
        await using (var dbContext = CreateDbContext())
        {
            // A second unnamed Dough of its own, saved before that was refused: in the variant, both would go by the source's name.
            var second = new SubAssembly { RoleId = roleId, OwnerProductId = sourceId };
            dbContext.SubAssemblies.Add(second);
            await dbContext.SaveChangesAsync(Cancellation);
            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = sourceId,
                SubAssemblyId = second.Id,
                AuthoredField = UsageAuthoredField.WeightPerPiece,
                AuthoredValue = 20m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => CreateVariantAsync(sourceId, "Variant");

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var check = CreateDbContext();
        (await check.Products.AnyAsync(p => p.Name == "Variant", Cancellation)).Should().BeFalse("it's one transaction");
    }
}
