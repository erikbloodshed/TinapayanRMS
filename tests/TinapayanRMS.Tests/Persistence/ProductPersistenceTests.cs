using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class ProductPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

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
        var category = new RawMaterialCategory { Name = name };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> SeedPackagingMaterialAsync(string name, int categoryId, int standardUnitId, decimal purchaseUnitQty, decimal currentCost)
    {
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            CategoryId = categoryId,
            BaseUnitId = standardUnitId,
            // Priced as currentCost per purchaseUnitQty base units, as these tests were written.
            CostPerBaseUnit = currentCost / purchaseUnitQty,
            // No buffer, so products are costed with the cost itself.
            BufferedCostPerBaseUnit = currentCost / purchaseUnitQty,
        };
        dbContext.RawMaterials.Add(material);
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
    public async Task SetUsagesForRoleAsync_stores_usages_with_the_sub_assembly_populated_on_read()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, totalWeight: 100m, totalCost: 24m);
        var productId = await SeedProductAsync("Ensaymada");

        await CreateUsageRepository().SetUsagesForRoleAsync(productId, roleId, [(subAssemblyId, 10m)], Cancellation);

        var usages = await CreateUsageRepository().GetForProductAsync(productId, Cancellation);
        var usage = usages.Should().ContainSingle().Subject;
        usage.WeightPerPiece.Should().Be(10m);
        usage.SubAssembly.Should().NotBeNull();
        usage.SubAssembly!.Name.Should().Be("Custard");
    }

    [Fact]
    public async Task SetUsagesForRoleAsync_rejects_the_same_sub_assembly_twice()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, totalWeight: 100m, totalCost: 24m);
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateUsageRepository().SetUsagesForRoleAsync(productId, roleId,
        [
            (subAssemblyId, 10m),
            (subAssemblyId, 20m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetUsagesForRoleAsync_rejects_a_sub_assembly_that_belongs_to_a_different_role()
    {
        var fillingRoleId = await SeedRoleAsync("Filling");
        var doughRoleId = await SeedRoleAsync("Dough", sortOrder: 2);
        var doughId = await SeedSubAssemblyAsync("Bread Dough", doughRoleId, totalWeight: 1000m, totalCost: 50m);
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateUsageRepository().SetUsagesForRoleAsync(productId, fillingRoleId, [(doughId, 10m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetUsagesForRoleAsync_rejects_a_sub_assembly_that_does_not_exist_at_all()
    {
        var fillingRoleId = await SeedRoleAsync("Filling");
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateUsageRepository().SetUsagesForRoleAsync(productId, fillingRoleId, [(999_999, 10m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_sub_assembly_in_use_by_a_product_is_blocked()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, totalWeight: 100m, totalCost: 24m);
        var productId = await SeedProductAsync("Ensaymada");
        await CreateUsageRepository().SetUsagesForRoleAsync(productId, roleId, [(subAssemblyId, 10m)], Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task SetLinesAsync_stores_a_line_entered_in_one_of_the_materials_units()
    {
        var categoryId = await SeedPackagingCategoryAsync("Ribbons");
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var meterUnitOfMeasureId = await SeedUnitOfMeasureAsync("meter", "m");
        var ribbonId = await SeedPackagingMaterialAsync("Satin Ribbon", categoryId, centimeterId, purchaseUnitQty: 1000m, currentCost: 45m);
        var meterId = await AddPackagingMaterialUnitAsync(ribbonId, meterUnitOfMeasureId, baseUnitsPerUnit: 100m);
        var productId = await SeedProductAsync("Gift Box Set");

        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (ribbonId, meterId, PackagingLineAuthoredField.QtyPerPiece, 0.127m),
        ], Cancellation);

        var line = (await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        line.QtyPerPiece.Should().Be(0.127m, "quantities are stored as entered, in the line's unit");
        line.Unit!.UnitOfMeasure!.Abbreviation.Should().Be("m");
        line.PackagingMaterial!.Units.Should().ContainSingle("the Products editor needs the material's units");
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_unit_that_belongs_to_another_material()
    {
        var categoryId = await SeedPackagingCategoryAsync("Ribbons");
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var meterUnitOfMeasureId = await SeedUnitOfMeasureAsync("meter", "m");
        var ribbonId = await SeedPackagingMaterialAsync("Satin Ribbon", categoryId, centimeterId, purchaseUnitQty: 1000m, currentCost: 45m);
        var twineId = await SeedPackagingMaterialAsync("Twine", categoryId, centimeterId, purchaseUnitQty: 1000m, currentCost: 20m);
        var twineMeterId = await AddPackagingMaterialUnitAsync(twineId, meterUnitOfMeasureId, baseUnitsPerUnit: 100m);
        var productId = await SeedProductAsync("Gift Box Set");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (ribbonId, twineMeterId, PackagingLineAuthoredField.QtyPerPiece, 0.127m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<int> AddPackagingMaterialUnitAsync(int packagingMaterialId, int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        await using var dbContext = CreateDbContext();
        var unit = new RawMaterialUnit { RawMaterialId = packagingMaterialId, UnitOfMeasureId = unitOfMeasureId, BaseUnitsPerUnit = baseUnitsPerUnit };
        dbContext.RawMaterialUnits.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    [Fact]
    public async Task SetLinesAsync_stores_lines_and_derives_the_reciprocal_field()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");

        // Authored as "6 pieces per box".
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
        ], Cancellation);

        var lines = await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation);
        var line = lines.Should().ContainSingle().Subject;
        line.PiecesPerPackagingUnit.Should().Be(6m);
        // The repository computes the full-precision reciprocal (1/6 = 0.1666...), but the
        // decimal(18,4) column rounds it to 4 places on the round trip through SQL Server.
        line.QtyPerPiece.Should().Be(0.1667m);
        // The line's quantities are in the material's standard unit, which the Products grid shows.
        line.PackagingMaterial!.BaseUnit!.Id.Should().Be(pieceId);
    }

    [Fact]
    public async Task SetLinesAsync_rejects_the_same_packaging_material_twice()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 4m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_packaging_material_in_use_by_a_product_is_blocked()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
        ], Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.RawMaterials.Where(m => m.Id == boxId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_a_product_cascades_to_its_usages_lines_and_costing_parameters()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, totalWeight: 100m, totalCost: 24m);
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");

        await CreateUsageRepository().SetUsagesForRoleAsync(productId, roleId, [(subAssemblyId, 10m)], Cancellation);
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
        ], Cancellation);
        await CreateCostingParametersRepository().SetAsync(productId, laborCost: 5m, overheadCost: 3m, marginPercent: 40m, vatPercent: 12m, Cancellation);

        await CreateProductRepository().DeleteAsync(productId, Cancellation);

        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task CostingParametersRepository_GetForProductAsync_returns_null_before_any_are_set()
    {
        var productId = await SeedProductAsync("Ensaymada");

        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task CostingParametersRepository_SetAsync_upserts_a_single_row()
    {
        var productId = await SeedProductAsync("Ensaymada");
        var repository = CreateCostingParametersRepository();

        await repository.SetAsync(productId, laborCost: 5m, overheadCost: 3m, marginPercent: 40m, vatPercent: 12m, Cancellation);
        await repository.SetAsync(productId, laborCost: 6m, overheadCost: 3m, marginPercent: 35m, vatPercent: 12m, Cancellation);

        var parameters = await repository.GetForProductAsync(productId, Cancellation);
        parameters.Should().NotBeNull();
        parameters!.LaborCost.Should().Be(6m);
        parameters.MarginPercent.Should().Be(35m);

        await using var dbContext = CreateDbContext();
        (await dbContext.CostingParameters.CountAsync(c => c.ProductId == productId, Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task CostingParametersRepository_SetAsync_rejects_negative_values()
    {
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateCostingParametersRepository().SetAsync(productId, laborCost: -1m, overheadCost: 0m, marginPercent: 0m, vatPercent: 0m, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ProductRepository_AddAsync_GetAllAsync_GetByIdAsync_and_UpdateAsync_round_trip_a_product()
    {
        var repository = CreateProductRepository();

        await repository.AddAsync(new Product { Name = "Special Ensaymada" }, Cancellation);

        var all = await repository.GetAllAsync(Cancellation);
        var added = all.Should().ContainSingle().Subject;
        added.Name.Should().Be("Special Ensaymada");

        var byId = await repository.GetByIdAsync(added.Id, Cancellation);
        byId.Should().NotBeNull();
        byId!.Name.Should().Be("Special Ensaymada");

        byId.Name = "Special Ensaymada Big";
        await repository.UpdateAsync(byId, Cancellation);

        (await repository.GetByIdAsync(added.Id, Cancellation))!.Name.Should().Be("Special Ensaymada Big");
    }

    [Fact]
    public async Task ProductRepository_GetByIdAsync_returns_null_for_a_nonexistent_product()
    {
        (await CreateProductRepository().GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task ProductRepository_UpdateAsync_rejects_a_nonexistent_product()
    {
        var act = () => CreateProductRepository().UpdateAsync(new Product { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetUsagesForRoleAsync_rejects_a_nonexistent_product()
    {
        var roleId = await SeedRoleAsync("Filling");
        var subAssemblyId = await SeedSubAssemblyAsync("Custard", roleId, totalWeight: 100m, totalCost: 24m);

        var act = () => CreateUsageRepository().SetUsagesForRoleAsync(999_999, roleId, [(subAssemblyId, 10m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_nonexistent_product()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);

        var act = () => CreatePackagingLineRepository().SetLinesAsync(999_999,
        [
            (boxId, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_an_ingredient_on_a_packaging_line()
    {
        var productId = await SeedProductAsync("Ensaymada");
        var categoryId = await SeedPackagingCategoryAsync("Dairy");
        int butterId;
        await using (var dbContext = CreateDbContext())
        {
            var butter = new RawMaterial { Type = RawMaterialType.Ingredient, Name = "Butter", CategoryId = categoryId };
            dbContext.RawMaterials.Add(butter);
            await dbContext.SaveChangesAsync(Cancellation);
            butterId = butter.Id;
        }

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (butterId, null, PackagingLineAuthoredField.QtyPerPiece, 1m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'Butter' is an ingredient*");
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_nonexistent_packaging_material()
    {
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (999_999, null, PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m),
        ], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
