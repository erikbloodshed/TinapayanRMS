using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Pricing.Tests.Fixtures;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// Many products read at once (SavedProductReader.ReadDetailsAsync and ReadAllAsync), as Reports and Print
/// read them: the round trips don't grow with the number of products, and each product comes out as it would read alone.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SavedProductReaderBulkTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reading_every_product_takes_as_many_queries_for_ten_products_as_for_three()
    {
        var setup = await SetUpAsync();
        await AddProductsAsync(setup, "Bread", count: 3);
        var reader = CreateReader();
        var products = new ProductRepository(CreateDbContextFactory());

        int forThree;
        using (var counter = SqlCommandCounter.Start())
        {
            (await reader.ReadAllAsync(await products.GetAllAsync(Cancellation), CostBasis.Buffered, Cancellation)).Should().HaveCount(3);
            forThree = counter.Commands;
        }

        await AddProductsAsync(setup, "Roll", count: 7);

        int forTen;
        using (var counter = SqlCommandCounter.Start())
        {
            (await reader.ReadAllAsync(await products.GetAllAsync(Cancellation), CostBasis.Buffered, Cancellation)).Should().HaveCount(10);
            forTen = counter.Commands;
        }

        forThree.Should().BeGreaterThan(5, "it counted the reads it's comparing");
        forTen.Should().Be(forThree, "the products are read together, one query a kind of thing rather than a product");
        forTen.Should().BeLessThan(15);
    }

    [Fact]
    public async Task Each_product_read_with_the_others_comes_out_as_it_reads_alone()
    {
        var setup = await SetUpAsync();
        await AddProductsAsync(setup, "Bread", count: 2);
        var all = await new ProductRepository(CreateDbContextFactory()).GetAllAsync(Cancellation);
        var reader = CreateReader();

        var together = await reader.ReadAllAsync(all, CostBasis.Buffered, Cancellation);

        foreach (var product in together)
        {
            var alone = await reader.ReadAsync(product.Product, Cancellation);
            product.Costs.Should().BeEquivalentTo(alone.Costs);
            product.Sections.Select(s => s.SectionName).Should().Equal(alone.Sections.Select(s => s.SectionName));
            product.PackagingLines.Select(l => l.CostPerUnit).Should().Equal(alone.PackagingLines.Select(l => l.CostPerUnit));
        }
    }

    [Fact]
    public async Task Products_that_link_the_same_packaging_each_show_it()
    {
        var setup = await SetUpAsync();
        var (bigId, smallId, tinyId) = (await AddProductAsync("Big"), await AddProductAsync("Small"), await AddProductAsync("Tiny"));
        await new PackagingLineRepository(CreateDbContextFactory()).SetLinesAsync(
            bigId, [(setup.BoxId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)], Cancellation);
        var links = new PackagingLinkRepository(CreateDbContextFactory());
        await links.SetLinkAsync(smallId, bigId, Cancellation);
        await links.SetLinkAsync(tinyId, smallId, Cancellation);

        var details = await CreateReader().ReadDetailsAsync([bigId, smallId, tinyId], Cancellation);

        foreach (var id in new[] { bigId, smallId, tinyId })
        {
            details[id].Lines.Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m, "Tiny links Small, which links Big's two boxes");
        }

        details[tinyId].PackagingLink!.LinkedProduct!.Name.Should().Be("Small", "the banner names the product it links, not the end of the chain");
    }

    [Fact]
    public async Task A_composite_read_with_others_brings_the_products_it_holds_in_with_it()
    {
        var setup = await SetUpAsync();
        var (breadIds, _) = await AddProductsAsync(setup, "Bread", count: 2);
        var products = new ProductRepository(CreateDbContextFactory());
        var giftBoxId = await products.SaveAsync(
            new Product { Name = "Gift Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(breadIds[0], 1), (breadIds[1], 3)] },
            null,
            null,
            null,
            Cancellation);

        var details = await CreateReader().ReadDetailsAsync([giftBoxId], Cancellation);

        details.Keys.Should().BeEquivalentTo([giftBoxId, breadIds[0], breadIds[1]], "the products it holds are read with it");
        details[giftBoxId].Components.Select(c => (c.Component.ComponentProductId, c.RecipeCostAt(CostBasis.Buffered)))
            .Should().Equal((breadIds[0], 2.5m), (breadIds[1], 2.5m));
    }

    [Fact]
    public async Task Composites_holding_each_other_are_read_with_the_loop_cut_and_said_of_and_the_rest_as_usual()
    {
        var setup = await SetUpAsync();
        var (breadIds, _) = await AddProductsAsync(setup, "Bread", count: 2);
        var products = new ProductRepository(CreateDbContextFactory());
        async Task<int> AddCompositeAsync(string name) => await products.SaveAsync(
            new Product { Name = name, IsComposite = true },
            new RecipeDraft([], []) { Components = [(breadIds[0], 1), (breadIds[1], 1)] },
            null,
            null,
            null,
            Cancellation);
        var (aId, bId) = (await AddCompositeAsync("Box A"), await AddCompositeAsync("Box B"));
        await using (var dbContext = CreateDbContext())
        {
            // As only an edit outside the app could leave them: Save refuses a composite that would hold itself.
            dbContext.ProductComponents.Add(new ProductComponent { ProductId = aId, ComponentProductId = bId, Quantity = 1, SortOrder = 2 });
            dbContext.ProductComponents.Add(new ProductComponent { ProductId = bId, ComponentProductId = aId, Quantity = 1, SortOrder = 2 });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var details = await CreateReader().ReadDetailsAsync([aId, bId, breadIds[0]], Cancellation);

        details[aId].Components.Select(c => (c.Component.ComponentProductId, c.RecipeCostAt(CostBasis.Buffered)))
            .Should().Equal((breadIds[0], 2.5m), (breadIds[1], 2.5m), (bId, 5m));
        details[bId].Components.Select(c => (c.Component.ComponentProductId, c.RecipeCostAt(CostBasis.Buffered)))
            .Should().Equal([(breadIds[0], 2.5m), (breadIds[1], 2.5m), (aId, 0m)], "Box A, read first, is the one that comes round again");
        (details[aId].HoldsItself, details[bId].HoldsItself, details[breadIds[0]].HoldsItself).Should().Be((true, true, false));

        var all = await CreateReader().ReadAllAsync(await products.GetAllAsync(Cancellation), CostBasis.Buffered, Cancellation);
        all.Select(p => p.Product.Name).Should().Contain(["Bread 1", "Bread 2", "Box A", "Box B"], "one loop no longer fails every product's read");
    }

    [Fact]
    public async Task A_packaging_chain_that_comes_round_again_shows_no_packaging_and_is_said_of_rather_than_failing_the_read()
    {
        var setup = await SetUpAsync();
        var (breadIds, _) = await AddProductsAsync(setup, "Bread", count: 1);
        var (aId, bId) = (await AddProductAsync("A"), await AddProductAsync("B"));
        await using (var dbContext = CreateDbContext())
        {
            // As only a manual data edit could leave them: SetLinkAsync refuses a loop.
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = aId, LinkedProductId = bId });
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = bId, LinkedProductId = aId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var details = await CreateReader().ReadDetailsAsync([aId, bId, breadIds[0]], Cancellation);

        (details[aId].Lines.Count, details[bId].Lines.Count, details[breadIds[0]].Lines.Count).Should().Be((0, 0, 1));
        (details[aId].PackagingLinkedRoundInALoop, details[bId].PackagingLinkedRoundInALoop, details[breadIds[0]].PackagingLinkedRoundInALoop)
            .Should().Be((true, true, false));

        // A write that copies a product's packaging still refuses it (ProductCloner and unlinking read one product's).
        var copying = () => new PackagingLineRepository(CreateDbContextFactory()).GetEffectiveForProductAsync(aId, Cancellation);
        await copying.Should().ThrowAsync<BusinessRuleException>().WithMessage(PackagingLinkRules.LoopText);
    }

    [Fact]
    public async Task A_product_whose_category_sets_its_own_wastage_is_priced_with_it_and_the_rest_with_the_shop_wides()
    {
        var setup = await SetUpAsync();
        var (productIds, _) = await AddProductsAsync(setup, "Pandesal", 2);
        await new ProductCategoryRepository(CreateDbContextFactory()).AddAsync(new ProductCategory { Name = "Breads", WastagePercent = 10m }, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            var breadsId = await dbContext.ProductCategories.Select(c => c.Id).SingleAsync(Cancellation);
            await dbContext.Products.Where(p => p.Id == productIds[0]).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, breadsId), Cancellation);
        }

        var details = await CreateReader().ReadDetailsAsync(productIds, Cancellation);

        details[productIds[0]].WastagePercent.Should().Be(10m);
        details[productIds[1]].WastagePercent.Should().Be(CostingDefaults.DefaultWastagePercent, "no category: Settings' shop-wide one");
    }

    private sealed record Setup(int RoleId, int FlourId, int BoxId);

    /// <summary>A Dough role, flour at 0.05 a gram, and a box at 5.00 a piece.</summary>
    private async Task<Setup> SetUpAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync();
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial { Name = "Box", Type = RawMaterialType.Packaging, CategoryId = categoryId, BaseUnitId = pieceId, CostPerBaseUnit = 5m, BufferedCostPerBaseUnit = 5m };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);
        return new Setup(roleId, flourId, box.Id);
    }

    /// <summary>
    /// <paramref name="count"/> products named after <paramref name="prefix"/>, each with a 1,000 g dough of flour at 50 g a
    /// piece (2.50 a piece) and one box of packaging: all the same shape, so each brings the same kinds of rows to read.
    /// </summary>
    private async Task<(List<int> ProductIds, List<int> SubAssemblyIds)> AddProductsAsync(Setup setup, string prefix, int count)
    {
        List<int> productIds = [];
        List<int> subAssemblyIds = [];
        var lines = new PackagingLineRepository(CreateDbContextFactory());
        for (var i = 1; i <= count; i++)
        {
            var (productId, subAssemblyId) = await AddProductWithSubAssemblyAsync($"{prefix} {i}", setup.RoleId, [(setup.FlourId, 1000m)], weightPerPiece: 50m);
            await lines.SetLinesAsync(productId, [(setup.BoxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
            productIds.Add(productId);
            subAssemblyIds.Add(subAssemblyId);
        }

        return (productIds, subAssemblyIds);
    }

    private SavedProductReader CreateReader()
    {
        var factory = CreateDbContextFactory();
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory));
    }
}
