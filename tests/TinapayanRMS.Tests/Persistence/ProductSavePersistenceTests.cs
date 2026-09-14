using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// The Products screen's one Save (IProductRepository.SaveAsync): the product, its changed roles' usages,
/// its packaging lines and its costing in one transaction, so a part that's rejected saves none of it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductSavePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

    private ProductTabLinkRepository CreateTabLinkRepository() => new(CreateDbContextFactory(), CreateUsageRepository(), CreatePackagingLineRepository());

    private async Task<int> SeedRoleAsync(string name, int sortOrder)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = name, SortOrder = sortOrder };
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<int> SeedSubAssemblyAsync(string name, int roleId)
    {
        await using var dbContext = CreateDbContext();
        var subAssembly = new SubAssembly { Name = name, RoleId = roleId, TotalWeight = 1000m, TotalCost = 50m };
        dbContext.SubAssemblies.Add(subAssembly);
        await dbContext.SaveChangesAsync(Cancellation);
        return subAssembly.Id;
    }

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

    private async Task<int> SeedProductAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var product = new Product { Name = name };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(Cancellation);
        return product.Id;
    }

    [Fact]
    public async Task SaveAsync_saves_a_new_product_with_its_recipe_packaging_and_costing_at_once()
    {
        var doughRoleId = await SeedRoleAsync("Dough", 1);
        var fillingRoleId = await SeedRoleAsync("Filling", 2);
        var doughId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId);
        var custardId = await SeedSubAssemblyAsync("Custard", fillingRoleId);
        var boxId = await SeedBoxAsync();
        var product = new Product { Name = "Cream Loaf", Description = "Soft bread with a creamy filling" };

        var id = await CreateProductRepository().SaveAsync(
            product,
            [(doughRoleId, [(doughId, 50m)]), (fillingRoleId, [(custardId, 20m)])],
            [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)],
            new CostingParameters { LaborCost = 6m, OverheadCost = 5.5m, AdministrativeCost = 3.5m, MarginPercent = 30m, VatPercent = 12m },
            Cancellation);

        product.Id.Should().Be(0, "the caller's entity is only read");
        var saved = (await CreateProductRepository().GetByIdAsync(id, Cancellation))!;
        saved.Name.Should().Be("Cream Loaf");
        saved.Description.Should().Be("Soft bread with a creamy filling");
        saved.UpdatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));

        var usages = await CreateUsageRepository().GetForProductAsync(id, Cancellation);
        usages.Select(u => (u.SubAssemblyId, u.WeightPerPiece)).Should().BeEquivalentTo(new[] { (doughId, 50m), (custardId, 20m) });
        (await CreatePackagingLineRepository().GetForProductAsync(id, Cancellation)).Should().ContainSingle(l => l.PiecesPerPackagingUnit == 6m);

        var costing = (await CreateCostingParametersRepository().GetForProductAsync(id, Cancellation))!;
        costing.LaborCost.Should().Be(6m);
        costing.AdministrativeCost.Should().Be(3.5m);
        costing.VatPercent.Should().Be(12m);
    }

    [Fact]
    public async Task SaveAsync_replaces_only_the_roles_it_is_sent_and_leaves_the_rest_alone()
    {
        var doughRoleId = await SeedRoleAsync("Dough", 1);
        var fillingRoleId = await SeedRoleAsync("Filling", 2);
        var doughId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId);
        var custardId = await SeedSubAssemblyAsync("Custard", fillingRoleId);
        var ubeId = await SeedSubAssemblyAsync("Ube Filling", fillingRoleId);
        var boxId = await SeedBoxAsync();
        var productId = await SeedProductAsync("Ensaymada");
        await CreateUsageRepository().SetUsagesForRoleAsync(productId, doughRoleId, [(doughId, 40m)], Cancellation);
        await CreateUsageRepository().SetUsagesForRoleAsync(productId, fillingRoleId, [(custardId, 10m)], Cancellation);
        await CreatePackagingLineRepository().SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)], Cancellation);
        await CreateCostingParametersRepository().SetAsync(productId, 5m, 3m, 1m, 40m, 12m, Cancellation);

        await CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Ensaymada" },
            [(fillingRoleId, [(ubeId, 15m)])],
            packagingLines: null,
            costing: null,
            Cancellation);

        var usages = await CreateUsageRepository().GetForProductAsync(productId, Cancellation);
        usages.Select(u => (u.SubAssemblyId, u.WeightPerPiece)).Should().BeEquivalentTo(new[] { (doughId, 40m), (ubeId, 15m) });
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation))!.LaborCost.Should().Be(5m);
    }

    [Fact]
    public async Task SaveAsync_saves_nothing_when_a_part_is_rejected()
    {
        var fillingRoleId = await SeedRoleAsync("Filling", 1);
        var custardId = await SeedSubAssemblyAsync("Custard", fillingRoleId);
        var ubeId = await SeedSubAssemblyAsync("Ube Filling", fillingRoleId);
        var boxId = await SeedBoxAsync();
        var productId = await SeedProductAsync("Ensaymada");
        await CreateUsageRepository().SetUsagesForRoleAsync(productId, fillingRoleId, [(custardId, 10m)], Cancellation);

        // The same box twice is rejected, after the name and the usages were written.
        var act = () => CreateProductRepository().SaveAsync(
            new Product { Id = productId, Name = "Ube Ensaymada", Description = "Now with ube" },
            [(fillingRoleId, [(ubeId, 15m)])],
            [(boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m), (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 4m)],
            new CostingParameters { LaborCost = 5m },
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var product = (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!;
        product.Name.Should().Be("Ensaymada");
        product.Description.Should().BeNull();
        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle(u => u.SubAssemblyId == custardId);
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_adds_no_product_when_a_part_of_a_new_one_is_rejected()
    {
        var fillingRoleId = await SeedRoleAsync("Filling", 1);
        var custardId = await SeedSubAssemblyAsync("Custard", fillingRoleId);

        var act = () => CreateProductRepository().SaveAsync(
            new Product { Name = "Custard Bun" },
            [(fillingRoleId, [(custardId, 10m), (custardId, 12m)])],
            packagingLines: null,
            costing: null,
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_rejects_usages_for_a_linked_role_and_keeps_the_product_as_it_was()
    {
        var doughRoleId = await SeedRoleAsync("Dough", 1);
        var doughId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId);
        var bigId = await SeedProductAsync("Ensaymada Big");
        var smallId = await SeedProductAsync("Ensaymada Small");
        await CreateUsageRepository().SetUsagesForRoleAsync(bigId, doughRoleId, [(doughId, 60m)], Cancellation);
        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, doughRoleId, bigId, Cancellation);

        var act = () => CreateProductRepository().SaveAsync(
            new Product { Id = smallId, Name = "Ensaymada Mini" },
            [(doughRoleId, [(doughId, 30m)])],
            packagingLines: null,
            costing: null,
            Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Live Template-linked*");
        (await CreateProductRepository().GetByIdAsync(smallId, Cancellation))!.Name.Should().Be("Ensaymada Small");
    }
}
