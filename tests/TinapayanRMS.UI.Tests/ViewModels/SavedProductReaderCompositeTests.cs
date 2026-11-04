using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// A saved composite read and priced (SavedProductReader), as Reports, Print and the Products screen price it: each
/// product it holds brings its recipe cost, as its own Costing tab's Sub-assemblies figure, times its quantity; its
/// packaging stays out; a composite inside another brings its products'.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SavedProductReaderCompositeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static CostingParameters Costing() => new()
    {
        LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m, VatPercent = 12m,
    };

    /// <summary>
    /// Ensaymada, 2.50 of recipe a piece (a 1,000 g dough of flour at 0.05 a gram buffered, 50 g a piece), and Cheese Roll,
    /// 5.00 (100 g a piece), each at <see cref="Costing"/>. Unbuffered, the flour is 0.04 a gram: 2.00 and 4.00.
    /// </summary>
    private async Task<(Product Ensaymada, Product CheeseRoll)> AddTwoProductsAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.04m, bufferedCostPerGram: 0.05m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var products = new ProductRepository(CreateDbContextFactory());
        foreach (var id in new[] { ensaymadaId, cheeseRollId })
        {
            await products.SaveAsync((await products.GetByIdAsync(id, Cancellation))!, null, null, null, Costing(), Cancellation);
        }

        return ((await products.GetByIdAsync(ensaymadaId, Cancellation))!, (await products.GetByIdAsync(cheeseRollId, Cancellation))!);
    }

    private Task<Product> AddCompositeAsync(string name, params (int ProductId, int Quantity)[] components) =>
        AddCompositeAsync(name, includesComponentPackaging: false, components);

    private async Task<Product> AddCompositeAsync(string name, bool includesComponentPackaging, params (int ProductId, int Quantity)[] components)
    {
        var products = new ProductRepository(CreateDbContextFactory());
        var id = await products.SaveAsync(
            new Product { Name = name, IsComposite = true, IncludesComponentPackaging = includesComponentPackaging },
            new RecipeDraft([], []) { Components = components },
            null,
            null,
            Costing(),
            Cancellation);
        return (await products.GetByIdAsync(id, Cancellation))!;
    }

    /// <summary>A packaging material of one piece, costing <paramref name="cost"/>, as <paramref name="productId"/>'s own packaging line.</summary>
    private async Task AddPackagingAsync(int productId, string name, decimal cost)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync($"{name} Boxes");
        int materialId;
        await using (var dbContext = CreateDbContext())
        {
            var material = new RawMaterial
            {
                Type = RawMaterialType.Packaging, Name = name, CategoryId = categoryId, BaseUnitId = pieceId,
                CostPerBaseUnit = cost, BufferedCostPerBaseUnit = cost,
            };
            dbContext.RawMaterials.Add(material);
            await dbContext.SaveChangesAsync(Cancellation);
            materialId = material.Id;
        }

        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(productId, [(materialId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
    }

    [Fact]
    public async Task A_composite_at_its_products_percentages_prices_at_exactly_what_they_do()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 1), (cheeseRoll.Id, 2));
        var reader = CreateReader();

        var single = await reader.ReadAsync(ensaymada, Cancellation);
        var roll = await reader.ReadAsync(cheeseRoll, Cancellation);
        var composite = await reader.ReadAsync(box, Cancellation);

        composite.Components.Select(c => (c.LineName, c.RecipeCostAt(CostBasis.Buffered), c.CostAt(CostBasis.Buffered))).Should().Equal(
            ("Ensaymada × 1", 2.50m, 2.50m), ("Cheese Roll × 2", 5m, 10m));
        composite.Costs.SubAssemblies.Should().Be(single.Costs.SubAssemblies + 2 * roll.Costs.SubAssemblies);

        // Wastage, labor, overhead and administrative are percentages of the recipe cost, packaging passes straight
        // through, and markup and VAT multiply, so a composite of products at their own percentages, with no packaging, is
        // priced at exactly what they're priced at together. The products bring their recipe cost before their wastage, so
        // the composite adds the 3% once, on all 12.50 of it, never again on top of theirs: 0.375, as theirs add up to.
        composite.Costs.Wastage.Should().Be(0.375m);
        composite.Costs.Wastage.Should().Be(single.Costs.Wastage + 2 * roll.Costs.Wastage);
        composite.Costs.SellingPrice.Should().Be(single.Costs.SellingPrice + 2 * roll.Costs.SellingPrice);
        composite.Costs.SellingPrice.Should().Be(26.026m);
    }

    [Fact]
    public async Task A_products_packaging_stays_out_of_a_composite_and_a_composite_inside_another_brings_its_products()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        await AddPackagingAsync(ensaymada.Id, "Ensaymada Bag", 1.50m);
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 2), (cheeseRoll.Id, 1));
        await AddPackagingAsync(box.Id, "Gift Box", 12m);
        var party = await AddCompositeAsync("Party Package", (box.Id, 2), (ensaymada.Id, 1));
        var reader = CreateReader();

        var single = await reader.ReadAsync(ensaymada, Cancellation);
        var boxed = await reader.ReadAsync(box, Cancellation);
        var partied = await reader.ReadAsync(party, Cancellation);

        single.Costs.Packaging.Should().Be(1.50m);
        boxed.Costs.SubAssemblies.Should().Be(10m, "2 × 2.50 and 1 × 5.00, the bags left out");
        boxed.Costs.Packaging.Should().Be(12m, "the composite's own box only");
        partied.Costs.SubAssemblies.Should().Be(22.50m, "2 boxes of 10.00 in recipe, and 2.50");
        partied.Costs.Packaging.Should().Be(0m, "the boxes' own packaging stays out too");

        var printout = partied.ToPrintout(new DateTime(2026, 9, 29, 9, 0, 0));
        printout.CostUnitCaption.Should().Be("Per unit");
        printout.Sections.Should().BeEmpty();
        printout.Components.Should().Equal(
            new PrintedComponent("Pasalubong Box", 2, 10m, 20m),
            new PrintedComponent("Ensaymada", 1, 2.50m, 2.50m));
        printout.Costs.PartsTitle.Should().Be("Products (ingredients)");
    }

    [Fact]
    public async Task A_composites_topping_adds_to_its_recipe_cost_and_comes_with_it_into_another_composite()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 2), (cheeseRoll.Id, 1));

        // 500 g of icing at 0.10 a gram, 100 g of it on one box: 10.00.
        await SaveRecipeAsync(box.Id, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(icingId, 500m)], UsageAuthoredField.WeightPerPiece, 100m)],
            []) { Components = [(ensaymada.Id, 2), (cheeseRoll.Id, 1)] });
        var party = await AddCompositeAsync("Party Package", (box.Id, 2), (ensaymada.Id, 1));
        var reader = CreateReader();

        var boxed = await reader.ReadAsync(box, Cancellation);
        var partied = await reader.ReadAsync(party, Cancellation);

        boxed.Costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should().Equal(
            ("Ensaymada × 2", 5m), ("Cheese Roll × 1", 5m), ("Topping", 10m));
        boxed.Costs.SubAssemblies.Should().Be(20m);
        boxed.Costs.PartsTitle.Should().Be("Products and sub-assemblies (ingredients)");
        partied.Costs.SubAssemblies.Should().Be(42.50m, "2 boxes of 20.00 in recipe, their toppings included, and 2.50");
        partied.Costs.PartsTitle.Should().Be("Products (ingredients)");

        var printout = boxed.ToPrintout(new DateTime(2026, 9, 29, 9, 0, 0));
        printout.Components.Should().HaveCount(2);
        printout.Sections.Select(s => s.Name).Should().Equal("Topping");
    }

    [Fact]
    public async Task What_a_product_brings_is_read_by_id_and_a_product_thats_gone_is_left_out()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 2), (cheeseRoll.Id, 1));

        await AddPackagingAsync(box.Id, "Gift Box", 12m);

        var costs = await CreateReader().ReadComponentCostsAsync([ensaymada.Id, box.Id, 999_999], Cancellation);

        costs.Should().BeEquivalentTo(new Dictionary<int, ComponentCosts>
        {
            [ensaymada.Id] = new(2.50m, 2.00m, 0m, 0m),
            [box.Id] = new(10m, 8m, 12m, 12m),
        });
    }

    [Fact]
    public async Task A_composite_including_its_products_packaging_adds_it_to_its_own_and_to_a_composite_holding_it()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        await AddPackagingAsync(ensaymada.Id, "Ensaymada Bag", 1.50m);
        var box = await AddCompositeAsync("Pasalubong Box", includesComponentPackaging: true, (ensaymada.Id, 2), (cheeseRoll.Id, 1));
        await AddPackagingAsync(box.Id, "Gift Box", 12m);
        var party = await AddCompositeAsync("Party Package", includesComponentPackaging: true, (box.Id, 2), (ensaymada.Id, 1));
        var reader = CreateReader();

        var boxed = await reader.ReadAsync(box, Cancellation);
        var partied = await reader.ReadAsync(party, Cancellation);

        boxed.Costs.SubAssemblies.Should().Be(10m, "the recipe is the same either way");
        boxed.Costs.OwnPackaging.Should().Be(12m);
        boxed.Costs.ComponentPackaging.Should().Be(3m, "2 bags of 1.50; Cheese Roll has none");
        boxed.Costs.Packaging.Should().Be(15m);
        boxed.Costs.PackagingTitle.Should().Be("Packaging (own and products')");
        boxed.Components.Select(c => c.PackagingCostAt(CostBasis.Buffered)).Should().Equal(3m, 0m);

        // Printed as the Products tab shows it: a Packaging Cost column, and each line's Cost with its packaging in.
        boxed.ToPrintout(new DateTime(2026, 9, 30, 9, 0, 0)).Components.Should().Equal(
            new PrintedComponent("Ensaymada", 2, 2.50m, 8m, 1.50m),
            new PrintedComponent("Cheese Roll", 1, 5m, 5m, 0m));

        // Labor, overhead and administrative stay percentages of the recipe alone.
        boxed.Costs.Labor.Should().Be(2.50m, "25% of 10.00, the packaging left out");

        // A box brings all of its packaging, its own and its products', and the loose Ensaymada its bag.
        partied.Costs.ComponentPackaging.Should().Be(31.50m, "2 boxes of 15.00, and 1.50");
        partied.Costs.OwnPackaging.Should().Be(0m);
        partied.ToPrintout(new DateTime(2026, 9, 30, 9, 0, 0)).Costs.Packaging.Should().Be(31.50m);
    }

    [Fact]
    public async Task A_composite_including_its_products_packaging_at_their_percentages_prices_at_exactly_what_they_do()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        await AddPackagingAsync(ensaymada.Id, "Ensaymada Bag", 1.50m);
        var box = await AddCompositeAsync("Pasalubong Box", includesComponentPackaging: true, (ensaymada.Id, 1), (cheeseRoll.Id, 2));
        var reader = CreateReader();

        var single = await reader.ReadAsync(ensaymada, Cancellation);
        var roll = await reader.ReadAsync(cheeseRoll, Cancellation);
        var composite = await reader.ReadAsync(box, Cancellation);

        // Packaging passes straight through in both, so the products' prices, bag and all, add up to the composite's.
        composite.Costs.SellingPrice.Should().Be(single.Costs.SellingPrice + 2 * roll.Costs.SellingPrice);
        composite.Costs.Packaging.Should().Be(1.50m);
    }

    [Fact]
    public async Task A_composite_read_unbuffered_brings_its_products_at_their_true_costs_and_its_printout_says_so()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 1), (cheeseRoll.Id, 2));

        var composite = await CreateReader().ReadAsync(box, CostBasis.Unbuffered, Cancellation);

        composite.Components.Select(c => (c.LineName, c.RecipeCostAt(CostBasis.Unbuffered), c.CostAt(CostBasis.Unbuffered))).Should().Equal(
            ("Ensaymada × 1", 2.00m, 2.00m), ("Cheese Roll × 2", 4m, 8m));
        composite.Costs.SubAssemblies.Should().Be(10m, "2.00 + 2 × 4.00, the flour at 0.04 a gram");

        var printout = composite.ToPrintout(new DateTime(2026, 9, 29, 9, 0, 0));
        printout.CostBasis.Should().Be(CostBasis.Unbuffered);
        printout.Components!.Select(c => c.Cost).Should().Equal(2.00m, 8m);
    }

    [Fact]
    public async Task A_product_read_unbuffered_prices_its_sections_at_their_true_costs()
    {
        var (ensaymada, _) = await AddTwoProductsAsync();

        var buffered = await CreateReader().ReadAsync(ensaymada, Cancellation);
        var unbuffered = await CreateReader().ReadAsync(ensaymada, CostBasis.Unbuffered, Cancellation);

        buffered.Costs.SubAssemblies.Should().Be(2.50m);
        unbuffered.Costs.SubAssemblies.Should().Be(2.00m, "50 g of a 1,000 g batch of flour at 0.04 a gram");
        unbuffered.ToPrintout(new DateTime(2026, 9, 29, 9, 0, 0)).Sections.Single().CostPerPiece.Should().Be(2.00m);
    }

    private SavedProductReader CreateReader()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory));
    }
}
