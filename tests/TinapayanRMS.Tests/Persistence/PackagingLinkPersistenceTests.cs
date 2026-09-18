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
/// Derived products: a packaging link reads through to another product's packaging, and a clone is an
/// independent copy — its own copy of every sub-assembly, with no links.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class PackagingLinkPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

    private PackagingLinkRepository CreatePackagingLinkRepository() => new(CreateDbContextFactory(), CreatePackagingLineRepository());

    private ProductCloner CreateProductCloner() => new(CreateDbContextFactory());

    private async Task<int> SeedBoxAsync(string name = "6-Piece Box")
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes" };
        var box = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
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
    public async Task SetLinkAsync_makes_the_packaging_resolve_through_the_linked_product()
    {
        var boxId = await SeedBoxAsync();
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await CreatePackagingLineRepository().SetLinesAsync(bigId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);

        await CreatePackagingLinkRepository().SetLinkAsync(smallId, bigId, Cancellation);

        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(smallId, Cancellation))
            .Should().ContainSingle(l => l.PackagingMaterial!.Name == "6-Piece Box");
        (await CreatePackagingLineRepository().GetForProductAsync(smallId, Cancellation)).Should().BeEmpty("its own lines are untouched");
        (await CreatePackagingLinkRepository().GetForProductAsync(smallId, Cancellation))!.LinkedProduct!.Name.Should().Be("Big");
    }

    [Fact]
    public async Task GetEffectiveForProductAsync_resolves_through_a_chain_of_two_links()
    {
        var boxId = await SeedBoxAsync();
        var aId = await AddProductAsync("A");
        var bId = await AddProductAsync("B");
        var cId = await AddProductAsync("C");
        await CreatePackagingLineRepository().SetLinesAsync(aId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);

        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(bId, aId, Cancellation); // b -> a
        await links.SetLinkAsync(cId, bId, Cancellation); // c -> b -> a

        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(cId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task SetLinkAsync_rejects_a_self_link_and_a_cycle()
    {
        var aId = await AddProductAsync("A");
        var bId = await AddProductAsync("B");
        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(bId, aId, Cancellation); // b -> a

        var selfLink = () => links.SetLinkAsync(aId, aId, Cancellation);
        var cycle = () => links.SetLinkAsync(aId, bId, Cancellation); // a -> b would close the loop

        await selfLink.Should().ThrowAsync<InvalidOperationException>();
        await cycle.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinkAsync_can_repoint_an_existing_link_to_a_different_product()
    {
        var boxId = await SeedBoxAsync();
        var bagId = await SeedBoxAsync("Paper Bag");
        var aId = await AddProductAsync("A");
        var bId = await AddProductAsync("B");
        var derivedId = await AddProductAsync("Derived");
        await CreatePackagingLineRepository().SetLinesAsync(aId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);
        await CreatePackagingLineRepository().SetLinesAsync(bId, [(bagId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(derivedId, aId, Cancellation);
        await links.SetLinkAsync(derivedId, bId, Cancellation); // repoint to B

        (await CreatePackagingLineRepository().GetEffectiveForProductAsync(derivedId, Cancellation))
            .Should().ContainSingle(l => l.PackagingMaterial!.Name == "Paper Bag");
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(l => l.ProductId == derivedId, Cancellation)).Should().Be(1, "still one link, not two");
    }

    [Fact]
    public async Task RemoveLinkAsync_materializes_the_packaging_it_showed_then_unlinks()
    {
        var boxId = await SeedBoxAsync();
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await CreatePackagingLineRepository().SetLinesAsync(bigId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);
        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(smallId, bigId, Cancellation);

        await links.RemoveLinkAsync(smallId, Cancellation);

        (await links.GetForProductAsync(smallId, Cancellation)).Should().BeNull();
        (await CreatePackagingLineRepository().GetForProductAsync(smallId, Cancellation))
            .Should().ContainSingle(l => l.UnitsPerPackagingUnit == 6m, "it owns a copy of what it showed a moment ago");
    }

    [Fact]
    public async Task RemoveLinkAsync_on_unlinked_packaging_is_a_no_op()
    {
        var productId = await AddProductAsync("Solo");

        var act = () => CreatePackagingLinkRepository().RemoveLinkAsync(productId, Cancellation);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_editing_linked_packaging()
    {
        var boxId = await SeedBoxAsync();
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await CreatePackagingLineRepository().SetLinesAsync(bigId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);
        await CreatePackagingLinkRepository().SetLinkAsync(smallId, bigId, Cancellation);

        var act = () => CreatePackagingLineRepository().SetLinesAsync(smallId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 4m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_product_whose_packaging_another_product_links_is_refused()
    {
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await CreatePackagingLinkRepository().SetLinkAsync(smallId, bigId, Cancellation);

        var act = () => CreateProductRepository().DeleteAsync(bigId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Small links this product's packaging*");
    }

    [Fact]
    public async Task Deleting_a_product_cascades_to_its_own_packaging_link()
    {
        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        var links = CreatePackagingLinkRepository();
        await links.SetLinkAsync(smallId, bigId, Cancellation);

        await CreateProductRepository().DeleteAsync(smallId, Cancellation);

        (await links.GetForProductAsync(smallId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task ProductCloner_CloneAsync_gives_the_clone_its_own_copy_of_everything_and_no_links()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.3m);
        var boxId = await SeedBoxAsync();
        var (otherId, linkedDoughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);
        var sourceId = await AddProductAsync("Source");
        await using (var dbContext = CreateDbContext())
        {
            var source = await dbContext.Products.SingleAsync(p => p.Id == sourceId, Cancellation);
            source.Description = "Soft bread with ube filling";
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.Yield, 10m)],
            [new LinkedUsageDraft(linkedDoughId, UsageAuthoredField.WeightPerPiece, 40m)]));
        await CreatePackagingLineRepository().SetLinesAsync(otherId, [(boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m)], Cancellation);
        await CreatePackagingLinkRepository().SetLinkAsync(sourceId, otherId, Cancellation);
        await CreateCostingParametersRepository().SetAsync(sourceId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1.25m, marginPercent: 40m, vatPercent: 12m, Cancellation);

        var cloneId = await CreateProductCloner().CloneAsync(sourceId, "Clone Of Source", Cancellation);

        var clone = (await CreateProductRepository().GetByIdAsync(cloneId, Cancellation))!;
        clone.Description.Should().Be("Soft bread with ube filling");
        clone.UpdatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1), "a clone is saved when it's made");

        var usages = await CreateUsageRepository().GetForProductAsync(cloneId, Cancellation);
        usages.Should().HaveCount(2).And.OnlyContain(u => u.SubAssembly!.OwnerProductId == cloneId, "every sub-assembly is the clone's own copy");
        usages.Select(u => (u.SubAssembly!.Role!.Name, u.AuthoredField, u.AuthoredValue, u.SubAssembly.TotalWeight)).Should().BeEquivalentTo(new[]
        {
            ("Filling", UsageAuthoredField.Yield, 10m, 100m),
            ("Dough", UsageAuthoredField.WeightPerPiece, 40m, 1000m),
        });
        usages.Should().OnlyContain(u => u.SubAssembly!.Name == null, "the clone's own go by their roles");

        (await CreatePackagingLinkRepository().GetForProductAsync(cloneId, Cancellation)).Should().BeNull("a clone never inherits a link");
        (await CreatePackagingLineRepository().GetForProductAsync(cloneId, Cancellation))
            .Should().ContainSingle(l => l.PackagingMaterial!.Name == "6-Piece Box", "it copies the packaging the source shows");

        var costing = (await CreateCostingParametersRepository().GetForProductAsync(cloneId, Cancellation))!;
        costing.MarginPercent.Should().Be(40m);
        costing.AdministrativePercent.Should().Be(1.25m);

        // The clone's recipe is its own: removing its copy of the dough leaves the original, and the
        // source's link to it, alone.
        await SaveRecipeAsync(cloneId, new RecipeDraft([], []));
        (await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation)).Should().Contain(u => u.SubAssemblyId == linkedDoughId);
    }

    [Fact]
    public async Task ProductCloner_CloneAsync_keeps_two_sub_assemblies_in_one_role_apart_by_the_names_they_went_by()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        var (_, otherDoughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 800m)]);
        var (sourceId, _) = await AddProductWithSubAssemblyAsync("Source", roleId, [(flourId, 1000m)]);
        await using (var dbContext = CreateDbContext())
        {
            // The source both owns a Dough and links Pandesal's, which goes by its owner there: "Dough" and "Dough: Pandesal".
            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = sourceId,
                SubAssemblyId = otherDoughId,
                AuthoredField = UsageAuthoredField.WeightPerPiece,
                AuthoredValue = 20m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var cloneId = await CreateProductCloner().CloneAsync(sourceId, "Clone Of Source", Cancellation);

        await using var check = CreateDbContext();
        (await check.SubAssemblies.Where(s => s.OwnerProductId == cloneId).Select(s => s.Name).ToListAsync(Cancellation))
            .Should().BeEquivalentTo([null, "Pandesal"], "both are the clone's own now, and Pandesal's keeps what told it apart");
    }

}
