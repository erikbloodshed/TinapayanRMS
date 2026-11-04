using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>What a raw material makes up of each product's material cost, for Raw Materials' Used By tiles.</summary>
[Collection(DatabaseCollection.Name)]
public class MaterialCostSharesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_material_is_its_share_of_a_products_material_cost_wherever_it_is_in_the_recipe()
    {
        var (ensaymadaId, flourId, sugarId, syrupId) = await AddEnsaymadaAsync();
        var shares = CreateShares();

        // A batch of 100: 900 g of flour at 0.05 is 45.00, and 100 g of syrup, made from sugar at 0.09, is 9.00.
        (await shares.ReadAsync(flourId, [ensaymadaId], Cancellation))[ensaymadaId].Should().BeApproximately(45m / 54m, 0.0000001m);
        (await shares.ReadAsync(syrupId, [ensaymadaId], Cancellation))[ensaymadaId].Should().BeApproximately(9m / 54m, 0.0000001m);
        (await shares.ReadAsync(sugarId, [ensaymadaId], Cancellation))[ensaymadaId].Should().BeApproximately(9m / 54m, 0.0000001m, "sugar is in it through the syrup");
    }

    [Fact]
    public async Task A_composite_is_priced_with_all_the_products_it_holds_even_those_without_the_material()
    {
        var (ensaymadaId, flourId, _, _) = await AddEnsaymadaAsync();
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.10m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", await RoleIdAsync("Dough"), [(cheeseId, 1000m)], weightPerPiece: 100m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 2), (cheeseRollId, 1)] },
            null,
            null,
            null,
            Cancellation);

        // Two Ensaymada bring 2 × 0.54, 0.90 of it flour, and a Cheese Roll 10.00.
        var shares = await CreateShares().ReadAsync(flourId, [ensaymadaId, boxId], Cancellation);

        shares[boxId].Should().BeApproximately(0.90m / 11.08m, 0.0000001m);
        MaterialCostShares.Text(shares[boxId]).Should().Be("8.1% of materials");
    }

    [Fact]
    public void A_share_reads_as_a_percentage_of_materials()
    {
        MaterialCostShares.Text(0.38m).Should().Be("38% of materials");
        MaterialCostShares.Text(0.0004m).Should().Be("Under 0.1% of materials");
        MaterialCostShares.Text(null).Should().BeNull();
    }

    /// <summary>Ensaymada: a Dough of 900 g of flour at 0.05 and 100 g of syrup made from sugar at 0.09, 10 g a piece.</summary>
    private async Task<(int ProductId, int FlourId, int SugarId, int SyrupId)> AddEnsaymadaAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 900m), (syrupId, 100m)], weightPerPiece: 10m);
        return (productId, flourId, sugarId, syrupId);
    }

    private async Task<int> RoleIdAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return dbContext.SubAssemblyRoles.Where(r => r.Name == name).Select(r => r.Id).Single();
    }

    private MaterialCostShares CreateShares()
    {
        var factory = CreateDbContextFactory();
        return new MaterialCostShares(
            new ProductRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory),
                new CostingDefaultsRepository(factory)));
    }
}
