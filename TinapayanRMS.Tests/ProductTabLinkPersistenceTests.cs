using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class ProductTabLinkPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

    private ProductTabLinkRepository CreateTabLinkRepository() => new(CreateDbContextFactory(), CreateUsageRepository(), CreatePackagingLineRepository());

    private ProductCloner CreateProductCloner() => new(CreateDbContextFactory(), CreateUsageRepository(), CreatePackagingLineRepository(), CreateCostingParametersRepository());

    private async Task<int> SeedRoleAsync(string name, int sortOrder = 1)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = name, SortOrder = sortOrder };
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    private async Task<int> SeedSubAssemblyAsync(string name, int roleId, decimal totalWeight, decimal totalCost)
    {
        await using var dbContext = CreateDbContext();
        var subAssembly = new SubAssembly { Name = name, RoleId = roleId, TotalWeight = totalWeight, TotalCost = totalCost };
        dbContext.SubAssemblies.Add(subAssembly);
        await dbContext.SaveChangesAsync(Cancellation);
        return subAssembly.Id;
    }

    private async Task<int> SeedPackagingCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new PackagingCategory { Name = name };
        dbContext.PackagingCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> SeedPackagingMaterialAsync(string name, int categoryId, int standardUnitId, decimal purchaseUnitQty, decimal currentCost)
    {
        await using var dbContext = CreateDbContext();
        var material = new PackagingMaterial
        {
            Name = name,
            PackagingCategoryId = categoryId,
            StandardUnitId = standardUnitId,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = purchaseUnitQty,
            CurrentCost = currentCost,
        };
        dbContext.PackagingMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
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
    public async Task SetLinkAsync_makes_the_derived_products_effective_usages_resolve_through_the_linked_product()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Ube Filling", roleId, totalWeight: 100m, totalCost: 24m);
        var bigId = await SeedProductAsync("Special Ensaymada Big");
        var smallId = await SeedProductAsync("Special Ensaymada Small");
        await CreateUsageRepository().SetUsagesForRoleAsync(bigId, roleId, [(subAssemblyId, 15m)], Cancellation);

        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, bigId, Cancellation);

        var effective = await CreateUsageRepository().GetEffectiveForRoleAsync(smallId, roleId, Cancellation);
        var usage = effective.Should().ContainSingle().Subject;
        usage.WeightPerPiece.Should().Be(15m);
        usage.SubAssembly!.Name.Should().Be("Ube Filling");

        // The linked product's own usages are untouched.
        (await CreateUsageRepository().GetForProductAsync(smallId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetLinkAsync_makes_the_packaging_tab_resolve_through_the_linked_product()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, WellKnownUnitOfMeasureIds.Piece, purchaseUnitQty: 1m, currentCost: 30m);
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        await CreatePackagingLineRepository().SetLinesAsync(bigId, [(boxId, WellKnownUnitOfMeasureIds.Piece, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)], Cancellation);

        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.Packaging, null, bigId, Cancellation);

        var effective = await CreatePackagingLineRepository().GetEffectiveForProductAsync(smallId, Cancellation);
        effective.Should().ContainSingle(l => l.PackagingMaterial!.Name == "6-Piece Box");
    }

    [Fact]
    public async Task SetLinkAsync_rejects_a_self_link()
    {
        var productId = await SeedProductAsync("Solo");
        var roleId = await SeedRoleAsync("Dough");

        var act = () => CreateTabLinkRepository().SetLinkAsync(productId, ProductTabKind.SubAssemblyRole, roleId, productId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinkAsync_rejects_a_cycle()
    {
        var roleId = await SeedRoleAsync("Dough");
        var aId = await SeedProductAsync("A");
        var bId = await SeedProductAsync("B");
        await CreateTabLinkRepository().SetLinkAsync(bId, ProductTabKind.SubAssemblyRole, roleId, aId, Cancellation); // b -> a

        var act = () => CreateTabLinkRepository().SetLinkAsync(aId, ProductTabKind.SubAssemblyRole, roleId, bId, Cancellation); // a -> b would close the loop

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetEffectiveForRoleAsync_resolves_through_a_chain_of_two_links()
    {
        var roleId = await SeedRoleAsync("Dough");
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId, totalWeight: 1000m, totalCost: 50m);
        var aId = await SeedProductAsync("A");
        var bId = await SeedProductAsync("B");
        var cId = await SeedProductAsync("C");
        await CreateUsageRepository().SetUsagesForRoleAsync(aId, roleId, [(subAssemblyId, 30m)], Cancellation);

        var tabLinks = CreateTabLinkRepository();
        await tabLinks.SetLinkAsync(bId, ProductTabKind.SubAssemblyRole, roleId, aId, Cancellation); // b -> a
        await tabLinks.SetLinkAsync(cId, ProductTabKind.SubAssemblyRole, roleId, bId, Cancellation); // c -> b -> a

        var effective = await CreateUsageRepository().GetEffectiveForRoleAsync(cId, roleId, Cancellation);
        effective.Should().ContainSingle(u => u.SubAssembly!.Name == "Bread Dough" && u.WeightPerPiece == 30m);
    }

    [Fact]
    public async Task SetLinkAsync_can_repoint_an_existing_link_to_a_different_product()
    {
        var roleId = await SeedRoleAsync("Dough");
        var doughAId = await SeedSubAssemblyAsync("Dough A", roleId, totalWeight: 1000m, totalCost: 50m);
        var doughBId = await SeedSubAssemblyAsync("Dough B", roleId, totalWeight: 1000m, totalCost: 80m);
        var aId = await SeedProductAsync("A");
        var bId = await SeedProductAsync("B");
        var derivedId = await SeedProductAsync("Derived");
        await CreateUsageRepository().SetUsagesForRoleAsync(aId, roleId, [(doughAId, 30m)], Cancellation);
        await CreateUsageRepository().SetUsagesForRoleAsync(bId, roleId, [(doughBId, 30m)], Cancellation);

        var tabLinks = CreateTabLinkRepository();
        await tabLinks.SetLinkAsync(derivedId, ProductTabKind.SubAssemblyRole, roleId, aId, Cancellation);
        await tabLinks.SetLinkAsync(derivedId, ProductTabKind.SubAssemblyRole, roleId, bId, Cancellation); // repoint to B

        var effective = await CreateUsageRepository().GetEffectiveForRoleAsync(derivedId, roleId, Cancellation);
        effective.Should().ContainSingle(u => u.SubAssembly!.Name == "Dough B");

        var links = await tabLinks.GetForProductAsync(derivedId, Cancellation);
        links.Should().ContainSingle(); // still one link, not two
    }

    [Fact]
    public async Task RemoveLinkAsync_materializes_the_currently_effective_usages_then_unlinks()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Ube Filling", roleId, totalWeight: 100m, totalCost: 24m);
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        await CreateUsageRepository().SetUsagesForRoleAsync(bigId, roleId, [(subAssemblyId, 15m)], Cancellation);

        var tabLinks = CreateTabLinkRepository();
        await tabLinks.SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, bigId, Cancellation);

        await tabLinks.RemoveLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, Cancellation);

        (await tabLinks.GetForProductAsync(smallId, Cancellation)).Should().BeEmpty();

        // Now owns its own copy of what was effective a moment ago.
        var ownUsages = await CreateUsageRepository().GetForProductAsync(smallId, Cancellation);
        var usage = ownUsages.Should().ContainSingle().Subject;
        usage.SubAssembly!.Name.Should().Be("Ube Filling");
        usage.WeightPerPiece.Should().Be(15m);

        // Changing Big's recipe from now on no longer affects Small — it's independent.
        var subAssemblyId2 = await SeedSubAssemblyAsync("Cheese Filling", roleId, totalWeight: 100m, totalCost: 30m);
        await CreateUsageRepository().SetUsagesForRoleAsync(bigId, roleId, [(subAssemblyId2, 20m)], Cancellation);

        var smallUsagesAfter = await CreateUsageRepository().GetForProductAsync(smallId, Cancellation);
        smallUsagesAfter.Should().ContainSingle(u => u.SubAssembly!.Name == "Ube Filling");
    }

    [Fact]
    public async Task RemoveLinkAsync_on_an_unlinked_tab_is_a_no_op()
    {
        var roleId = await SeedRoleAsync("Filling");
        var productId = await SeedProductAsync("Solo");

        var act = () => CreateTabLinkRepository().RemoveLinkAsync(productId, ProductTabKind.SubAssemblyRole, roleId, Cancellation);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetUsagesForRoleAsync_rejects_editing_a_linked_tab()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Ube Filling", roleId, totalWeight: 100m, totalCost: 24m);
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        await CreateUsageRepository().SetUsagesForRoleAsync(bigId, roleId, [(subAssemblyId, 15m)], Cancellation);
        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, bigId, Cancellation);

        var act = () => CreateUsageRepository().SetUsagesForRoleAsync(smallId, roleId, [(subAssemblyId, 5m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_editing_a_linked_packaging_tab()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, WellKnownUnitOfMeasureIds.Piece, purchaseUnitQty: 1m, currentCost: 30m);
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        await CreatePackagingLineRepository().SetLinesAsync(bigId, [(boxId, WellKnownUnitOfMeasureIds.Piece, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)], Cancellation);
        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.Packaging, null, bigId, Cancellation);

        var act = () => CreatePackagingLineRepository().SetLinesAsync(smallId, [(boxId, WellKnownUnitOfMeasureIds.Piece, PackagingLineAuthoredField.PiecesPerPackagingUnit, 4m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_product_that_is_linked_to_by_another_products_tab_is_blocked()
    {
        var roleId = await SeedRoleAsync("Dough");
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        await CreateTabLinkRepository().SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, bigId, Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.Products.Where(p => p.Id == bigId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_a_product_cascades_to_its_own_tab_links()
    {
        var roleId = await SeedRoleAsync("Dough");
        var bigId = await SeedProductAsync("Big");
        var smallId = await SeedProductAsync("Small");
        var tabLinks = CreateTabLinkRepository();
        await tabLinks.SetLinkAsync(smallId, ProductTabKind.SubAssemblyRole, roleId, bigId, Cancellation);

        await CreateProductRepository().DeleteAsync(smallId, Cancellation);

        (await tabLinks.GetForProductAsync(smallId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task ProductCloner_CloneAsync_creates_an_independent_copy_with_no_links_of_its_own()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Ube Filling", roleId, totalWeight: 100m, totalCost: 24m);
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, WellKnownUnitOfMeasureIds.Piece, purchaseUnitQty: 1m, currentCost: 30m);
        var sourceId = await SeedProductAsync("Source");
        await CreateUsageRepository().SetUsagesForRoleAsync(sourceId, roleId, [(subAssemblyId, 15m)], Cancellation);
        await CreatePackagingLineRepository().SetLinesAsync(sourceId, [(boxId, WellKnownUnitOfMeasureIds.Piece, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m)], Cancellation);
        await CreateCostingParametersRepository().SetAsync(sourceId, laborCost: 5m, overheadCost: 3m, marginPercent: 40m, vatPercent: 12m, Cancellation);

        var newId = await CreateProductCloner().CloneAsync(sourceId, "Clone Of Source", Cancellation);

        (await CreateTabLinkRepository().GetForProductAsync(newId, Cancellation)).Should().BeEmpty();

        var usages = await CreateUsageRepository().GetForProductAsync(newId, Cancellation);
        usages.Should().ContainSingle(u => u.SubAssembly!.Name == "Ube Filling" && u.WeightPerPiece == 15m);

        var lines = await CreatePackagingLineRepository().GetForProductAsync(newId, Cancellation);
        lines.Should().ContainSingle(l => l.PackagingMaterial!.Name == "6-Piece Box");

        var costing = await CreateCostingParametersRepository().GetForProductAsync(newId, Cancellation);
        costing.Should().NotBeNull();
        costing!.MarginPercent.Should().Be(40m);

        // Editing the clone doesn't affect the source, and vice versa — no link exists.
        await CreateUsageRepository().SetUsagesForRoleAsync(newId, roleId, [(subAssemblyId, 99m)], Cancellation);
        var sourceUsagesAfter = await CreateUsageRepository().GetForProductAsync(sourceId, Cancellation);
        sourceUsagesAfter.Should().ContainSingle(u => u.WeightPerPiece == 15m);
    }

    [Fact]
    public async Task ProductCloner_CloneAsync_copies_the_sources_effective_data_when_the_source_itself_is_linked()
    {
        var roleId = await SeedRoleAsync("Dough");
        var subAssemblyId = await SeedSubAssemblyAsync("Bread Dough", roleId, totalWeight: 1000m, totalCost: 50m);
        var aId = await SeedProductAsync("A");
        var bId = await SeedProductAsync("B (linked to A)");
        await CreateUsageRepository().SetUsagesForRoleAsync(aId, roleId, [(subAssemblyId, 30m)], Cancellation);
        await CreateTabLinkRepository().SetLinkAsync(bId, ProductTabKind.SubAssemblyRole, roleId, aId, Cancellation);

        var cloneId = await CreateProductCloner().CloneAsync(bId, "Clone Of B", Cancellation);

        (await CreateTabLinkRepository().GetForProductAsync(cloneId, Cancellation)).Should().BeEmpty();
        var usages = await CreateUsageRepository().GetForProductAsync(cloneId, Cancellation);
        usages.Should().ContainSingle(u => u.SubAssembly!.Name == "Bread Dough" && u.WeightPerPiece == 30m);
    }
}
