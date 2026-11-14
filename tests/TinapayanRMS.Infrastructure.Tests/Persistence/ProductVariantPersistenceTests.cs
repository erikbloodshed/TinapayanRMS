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
/// A variant (IProductCloner.CreateVariantAsync): a new product that links every sub-assembly its source uses, the
/// source's own included, at the source's piece sizes, so it follows every change made there. Its packaging isn't
/// carried over. Its costing is copied, as a clone's is.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductVariantPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory());

    private Task<int> CreateVariantAsync(int sourceProductId, string name) =>
        new ProductCloner(CreateDbContextFactory()).CreateVariantAsync(sourceProductId, name, Cancellation);

    private static NewSubAssemblyDraft Own(int roleId, IReadOnlyList<RecipeLine> lines, int id = 0, string? sectionName = null, decimal weightPerPiece = 10m) =>
        new(roleId, lines, UsageAuthoredField.WeightPerPiece, weightPerPiece, sectionName) { Id = id };

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

    /// <summary>"Ensaymada Big", with a filling of its own (a product-owned role) at 60 g a piece.</summary>
    private async Task<(int SourceId, int FillingId, int FillingRoleId, int FlourId)> AddSourceWithOwnFillingAsync()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sourceId = await AddProductAsync("Ensaymada Big");
        await SaveRecipeAsync(sourceId, new RecipeDraft([Own(fillingRoleId, [(flourId, 1000m)], weightPerPiece: 60m)], []));
        return (sourceId, await SubAssemblyIdAsync(sourceId, fillingRoleId), fillingRoleId, flourId);
    }

    [Test]
    public async Task Links_every_sub_assembly_the_source_uses_its_own_included_at_its_piece_sizes_with_its_add_ins()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.3m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var flavourId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var doughId = await new SubAssemblyRepository(CreateDbContextFactory())
            .SaveStandaloneAsync(new SubAssembly { Name = "Sweet Dough", RoleId = doughRoleId }, [(flourId, 1000m)], Cancellation);
        var sourceId = await AddProductAsync("Ube Loaf Big");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.Yield, 10m),
                Own(toppingRoleId, [(sugarId, 50m)], weightPerPiece: 5m),
            ],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m) { AddIns = [(flavourId, 20m)] }]));
        var fillingId = await SubAssemblyIdAsync(sourceId, fillingRoleId);
        var toppingId = await SubAssemblyIdAsync(sourceId, toppingRoleId);

        var variantId = await CreateVariantAsync(sourceId, "Ube Loaf Small");

        var usages = await CreateUsageRepository().GetForProductAsync(variantId, Cancellation);
        // The piece sizes come over: the variant makes its sections the same way.
        usages.Select(u => (u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)).Should().BeEquivalentTo(
            new[]
            {
                (fillingId, UsageAuthoredField.Yield, 10m),
                (toppingId, UsageAuthoredField.WeightPerPiece, 5m),
                (doughId, UsageAuthoredField.WeightPerPiece, 60m),
            });
        usages.Where(u => u.SubAssemblyId != doughId)
            .Should().AllSatisfy(u => u.SubAssembly!.OwnerProduct!.Name.Should().Be("Ube Loaf Big", "each is read with the product it's kept in"));
        usages.Single(u => u.SubAssemblyId == doughId).SubAssembly!.OwnerProduct.Should().BeNull("the dough is kept on the Sub-Assemblies screen");

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.CountAsync(Cancellation))
            .Should().Be(3, "a variant links every section; it makes none");
        (await dbContext.UsageAddInLines.AsNoTracking()
                .Where(l => l.Usage!.ProductId == variantId)
                .Select(l => new { l.Usage!.SubAssemblyId, l.IngredientId, l.Grams })
                .ToListAsync(Cancellation))
            .Should().ContainSingle().Which.Should().Be(new { SubAssemblyId = doughId, IngredientId = flavourId, Grams = 20m }, "the source's add-ins are the variant's own");
        (await dbContext.SubAssemblies.Where(s => s.Id == fillingId).Select(s => s.OwnerProductId).SingleAsync(Cancellation))
            .Should().Be(sourceId, "the filling stays the source's own");
        (await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation))
            .Should().HaveCount(3, "the source's recipe is untouched");
    }

    [Test]
    public async Task Follows_changes_made_to_the_source_recipe()
    {
        var (sourceId, fillingId, fillingRoleId, flourId) = await AddSourceWithOwnFillingAsync();
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        await SaveRecipeAsync(sourceId, new RecipeDraft([Own(fillingRoleId, [(flourId, 1500m)], id: fillingId, weightPerPiece: 60m)], []));

        var usage = (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(fillingId);
        (await SubAssemblyTotalsAsync(usage.SubAssemblyId)).TotalWeight.Should().Be(1500m, "the variant reads the source's filling as it is now");
    }

    [Test]
    public async Task Keeps_its_links_through_a_later_save_of_its_own_recipe()
    {
        var (sourceId, fillingId, _, flourId) = await AddSourceWithOwnFillingAsync();
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        // A smaller piece, and the filling's lines changed in the variant: Save writes them to the source's own.
        await SaveRecipeAsync(variantId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(fillingId, UsageAuthoredField.WeightPerPiece, 30m) { Lines = [(flourId, 1200m)] }]));

        var usage = (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(fillingId);
        usage.AuthoredValue.Should().Be(30m);
        (await SubAssemblyTotalsAsync(usage.SubAssemblyId)).TotalWeight.Should().Be(1200m);
    }

    [Test]
    public async Task Can_not_link_the_sources_own_again_once_it_has_dropped_it()
    {
        var (sourceId, fillingId, _, _) = await AddSourceWithOwnFillingAsync();
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");
        await SaveRecipeAsync(variantId, new RecipeDraft([], []));

        var relink = () => SaveRecipeAsync(variantId, new RecipeDraft([], [new LinkedUsageDraft(fillingId, UsageAuthoredField.WeightPerPiece, 30m)]));

        await relink.Should().ThrowAsync<InvalidOperationException>().WithMessage("*is Ensaymada Big's own, so it can't be linked*");
    }

    [Test]
    public async Task The_source_can_not_drop_its_own_while_a_variant_links_it()
    {
        var (sourceId, fillingId, _, _) = await AddSourceWithOwnFillingAsync();
        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        var drop = () => SaveRecipeAsync(sourceId, new RecipeDraft([], []));

        await drop.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Ensaymada Small links Ensaymada Big Filling. Make a copy of it there, or remove it there, first.");
        (await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == fillingId);

        await SaveRecipeAsync(variantId, new RecipeDraft([], []));
        await drop();
        (await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task The_source_can_not_be_deleted_while_a_variant_links_its_own()
    {
        var (sourceId, _, _, _) = await AddSourceWithOwnFillingAsync();
        await CreateVariantAsync(sourceId, "Ensaymada Small");
        var products = new ProductRepository(CreateDbContextFactory());

        var delete = () => products.DeleteAsync(sourceId, Cancellation);
        var deletePermanently = () => products.DeletePermanentlyAsync(sourceId, Cancellation);

        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada Small links Ensaymada Big Filling.*");
        await deletePermanently.Should().ThrowAsync<InvalidOperationException>().WithMessage("Ensaymada Small links Ensaymada Big Filling.*");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Has_no_packaging_whatever_the_source_has(bool sourceLinksSet)
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box");
        var sourceId = await AddProductAsync("Big");
        if (sourceLinksSet)
        {
            var setId = await new SubAssemblyRepository(CreateDbContextFactory())
                .SaveStandalonePackagingAsync(new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
            await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(sourceId, setId, Cancellation);
        }
        else
        {
            await CreatePackagingLineRepository().SetLinesAsync(sourceId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);
        }

        var variantId = await CreateVariantAsync(sourceId, "Small");

        (await CreatePackagingLinkRepository().GetForProductAsync(variantId, Cancellation)).Should().BeNull("a variant's packaging is neither linked");
        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(variantId, Cancellation)).Should().BeEmpty("nor copied");
    }

    [Test]
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
        await costingRepository.SetAsync(sourceId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, Cancellation);

        var variantId = await CreateVariantAsync(sourceId, "Ensaymada Small");

        var variant = (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(variantId, Cancellation))!;
        variant.Name.Should().Be("Ensaymada Small");
        variant.CategoryId.Should().Be(categoryId);
        variant.Description.Should().Be("Buttery and soft");
        variant.QuantityRequired.Should().Be(8, "a variant sells the same way its source does until it's changed");
        variant.CurrentPrice.Should().Be(35m);

        // Every percentage the costing has, not only the two named: a variant's costing is its source's.
        var costing = (await costingRepository.GetForProductAsync(variantId, Cancellation))!;
        costing.Should().BeEquivalentTo(
            (await costingRepository.GetForProductAsync(sourceId, Cancellation))!,
            options => options.Excluding(c => c.Id).Excluding(c => c.ProductId).Excluding(c => c.Product));
        costing.MarginPercent.Should().Be(40m);
        costing.LaborPercent.Should().Be(5m);
    }

    [Test]
    public async Task Keeps_two_sections_in_one_role_apart_by_their_names()
    {
        var roleId = await AddRoleAsync("Filling", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour");
        var sourceId = await AddProductAsync("Source");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [Own(roleId, [(flourId, 1000m)], sectionName: "Cheese"), Own(roleId, [(flourId, 800m)], sectionName: "Ube")],
            []));

        var variantId = await CreateVariantAsync(sourceId, "Variant");

        (await CreateUsageRepository().GetForProductAsync(variantId, Cancellation))
            .Select(u => u.SubAssembly!.Name).Should().BeEquivalentTo(["Source Filling: Cheese", "Source Filling: Ube"]);
    }
}
