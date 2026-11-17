using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Printing;

/// <summary>
/// A saved product as Print puts it on paper (SavedProductPrintout.ToPrintout), read as Print reads it
/// (SavedProductReader, whose own figures Pricing's SavedProductReaderCompositeTests check): a composite's products as
/// its lines, its own sections beside them, each line's packaging when it includes its products', and the cost basis it
/// was read at.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class SavedProductPrintoutTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateTime PrintedAt = new(2026, 9, 29, 9, 0, 0);

    private static CostingParameters Costing() => new()
    {
        LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m,
    };

    /// <summary>
    /// Ensaymada, 2.50 of recipe a piece (a 1,000 g dough of flour at 0.05 a gram buffered, 50 g a piece), and Cheese Roll,
    /// 5.00 (100 g a piece), each at <see cref="Costing"/>. Unbuffered, the flour is 0.04 a gram: 2.00 and 4.00.
    /// </summary>
    private async Task<(Product Ensaymada, Product CheeseRoll)> AddTwoProductsAsync()
    {
        await SetVatAsync(12m);
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

    [Test]
    public async Task A_composite_prints_its_products_as_its_lines_at_their_recipe_costs()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        await AddPackagingAsync(ensaymada.Id, "Ensaymada Bag", 1.50m);
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 2), (cheeseRoll.Id, 1));
        await AddPackagingAsync(box.Id, "Gift Box", 12m);
        var party = await AddCompositeAsync("Party Package", (box.Id, 2), (ensaymada.Id, 1));

        var printout = (await CreateReader().ReadAsync(party, Cancellation)).ToPrintout(PrintedAt);

        printout.CostUnitCaption.Should().Be("Per unit");
        printout.Sections.Should().BeEmpty();
        printout.Components.Should().Equal(
            new PrintedComponent("Pasalubong Box", 2, 10m, 20m),
            new PrintedComponent("Ensaymada", 1, 2.50m, 2.50m));
        printout.Costs.PartsTitle.Should().Be("Products (ingredients)");
    }

    [Test]
    public async Task A_composites_own_topping_prints_as_a_section_beside_its_products()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3, isProductOwned: true);
        var icingId = await AddIngredientAsync("Icing", costPerGram: 0.1m);
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 2), (cheeseRoll.Id, 1));
        await SaveRecipeAsync(box.Id, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(icingId, 500m)], UsageAuthoredField.WeightPerPiece, 100m)],
            []) { Components = [(ensaymada.Id, 2), (cheeseRoll.Id, 1)] });

        var printout = (await CreateReader().ReadAsync(box, Cancellation)).ToPrintout(PrintedAt);

        printout.Components.Should().HaveCount(2);
        printout.Sections.Select(s => s.Name).Should().Equal("Topping");
    }

    // Printed as the Products tab shows it: a Packaging Cost column, and each line's Cost with its packaging in.
    [Test]
    public async Task A_composite_including_its_products_packaging_prints_each_lines_packaging_in_its_cost()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        await AddPackagingAsync(ensaymada.Id, "Ensaymada Bag", 1.50m);
        var box = await AddCompositeAsync("Pasalubong Box", includesComponentPackaging: true, (ensaymada.Id, 2), (cheeseRoll.Id, 1));
        await AddPackagingAsync(box.Id, "Gift Box", 12m);
        var party = await AddCompositeAsync("Party Package", includesComponentPackaging: true, (box.Id, 2), (ensaymada.Id, 1));
        var reader = CreateReader();

        (await reader.ReadAsync(box, Cancellation)).ToPrintout(PrintedAt).Components.Should().Equal(
            new PrintedComponent("Ensaymada", 2, 2.50m, 8m, 1.50m),
            new PrintedComponent("Cheese Roll", 1, 5m, 5m, 0m));
        (await reader.ReadAsync(party, Cancellation)).ToPrintout(PrintedAt).Costs.Packaging.Should().Be(31.50m);
    }

    [Test]
    public async Task A_composite_read_unbuffered_prints_its_products_at_their_true_costs_and_says_so()
    {
        var (ensaymada, cheeseRoll) = await AddTwoProductsAsync();
        var box = await AddCompositeAsync("Pasalubong Box", (ensaymada.Id, 1), (cheeseRoll.Id, 2));

        var printout = (await CreateReader().ReadAsync(box, CostBasis.Unbuffered, Cancellation)).ToPrintout(PrintedAt);

        printout.CostBasis.Should().Be(CostBasis.Unbuffered);
        printout.Components!.Select(c => c.Cost).Should().Equal(2.00m, 8m);
    }

    [Test]
    public async Task A_product_read_unbuffered_prints_its_section_at_its_true_cost_a_piece()
    {
        var (ensaymada, _) = await AddTwoProductsAsync();

        var printout = (await CreateReader().ReadAsync(ensaymada, CostBasis.Unbuffered, Cancellation)).ToPrintout(PrintedAt);

        printout.Sections.Single().CostPerPiece.Should().Be(2.00m, "50 g of a 1,000 g batch of flour at 0.04 a gram");
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
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
    }
}
