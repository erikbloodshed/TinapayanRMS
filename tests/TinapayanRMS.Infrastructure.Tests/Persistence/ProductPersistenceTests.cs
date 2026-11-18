using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ProductPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private PackagingLineRepository CreatePackagingLineRepository() => new(CreateDbContextFactory());

    private CostingParametersRepository CreateCostingParametersRepository() => new(CreateDbContextFactory());

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

    private Task<int> SeedProductAsync(string name) => AddProductAsync(name);

    [Test]
    public async Task Deleting_a_sub_assembly_in_use_by_a_product_is_blocked()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Test]
    [Arguments(PackagingLineAuthoredField.QtyPerUnit)]
    [Arguments(PackagingLineAuthoredField.UnitsPerPackagingUnit)]
    public async Task SetLinesAsync_refuses_a_line_of_0_by_saying_so_rather_than_dividing_by_it(PackagingLineAuthoredField authoredField)
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("Gift Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Gift Box Set");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId, [(boxId, null, authoredField, 0m)], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("A packaging line's quantity must be positive.");
    }

    [Test]
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
            (ribbonId, meterId, PackagingLineAuthoredField.QtyPerUnit, 0.127m),
        ], Cancellation);

        var line = (await CreatePackagingLineRepository().GetEffectiveForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        line.QtyPerUnit.Should().Be(0.127m, "quantities are stored as entered, in the line's unit");
        line.Unit!.UnitOfMeasure!.Abbreviation.Should().Be("m");
        line.PackagingMaterial!.Units.Should().ContainSingle("the Products editor needs the material's units");
    }

    [Test]
    public async Task GetForProductAsync_reads_each_line_with_its_materials_details_and_its_own_unit()
    {
        var ribbonsId = await SeedPackagingCategoryAsync("Ribbons");
        var boxesId = await SeedPackagingCategoryAsync("Boxes");
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var ribbonId = await SeedPackagingMaterialAsync("Satin Ribbon", ribbonsId, centimeterId, purchaseUnitQty: 1000m, currentCost: 45m);
        var meterId = await AddPackagingMaterialUnitAsync(ribbonId, await SeedUnitOfMeasureAsync("meter", "m"), baseUnitsPerUnit: 100m);
        await AddPackagingMaterialUnitAsync(ribbonId, await SeedUnitOfMeasureAsync("roll", "roll"), baseUnitsPerUnit: 1000m);
        var boxId = await SeedPackagingMaterialAsync("Gift Box", boxesId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Gift Box Set");
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (ribbonId, meterId, PackagingLineAuthoredField.QtyPerUnit, 0.3m),
            (boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m),
        ], Cancellation);

        var lines = await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation);

        lines.Select(l => (l.PackagingMaterial!.Name, l.PackagingMaterial.Category!.Name, l.PackagingMaterial.BaseUnit!.Abbreviation, l.Unit?.UnitOfMeasure!.Abbreviation))
            .Should().Equal(("Satin Ribbon", "Ribbons", "cm", "m"), ("Gift Box", "Boxes", "pc", null));
        lines[0].PackagingMaterial!.Units.Select(u => u.UnitOfMeasure!.Abbreviation).Should().Equal(["roll", "m"], "largest first");
        lines[1].PackagingMaterial!.Units.Should().BeEmpty();
    }

    [Test]
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
            (ribbonId, twineMeterId, PackagingLineAuthoredField.QtyPerUnit, 0.127m),
        ], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    private async Task<int> AddPackagingMaterialUnitAsync(int packagingMaterialId, int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        await using var dbContext = CreateDbContext();
        var unit = new RawMaterialUnit { RawMaterialId = packagingMaterialId, UnitOfMeasureId = unitOfMeasureId, BaseUnitsPerUnit = baseUnitsPerUnit };
        dbContext.RawMaterialUnits.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    [Test]
    public async Task SetLinesAsync_stores_lines_and_derives_the_reciprocal_field()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");

        // Authored as "6 pieces per box".
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
        ], Cancellation);

        var lines = await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation);
        var line = lines.Should().ContainSingle().Subject;
        line.UnitsPerPackagingUnit.Should().Be(6m);
        // The repository computes the full-precision reciprocal (1/6 = 0.1666...), but the
        // numeric(18,4) column rounds it to 4 places on the round trip through the database.
        line.QtyPerUnit.Should().Be(0.1667m);
        // The line's quantities are in the material's standard unit, which the Products grid shows.
        line.PackagingMaterial!.BaseUnit!.Id.Should().Be(pieceId);
    }

    [Test]
    public async Task SetLinesAsync_rejects_the_same_packaging_material_twice()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 4m),
        ], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task Deleting_a_packaging_material_in_use_by_a_product_is_blocked()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var productId = await SeedProductAsync("Ensaymada");
        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
        ], Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.RawMaterials.Where(m => m.Id == boxId).ExecuteDeleteAsync(Cancellation);

        await act.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Test]
    public async Task Deleting_a_product_permanently_cascades_to_its_usages_lines_and_costing_parameters()
    {
        var roleId = await AddRoleAsync("Filling");
        var sugarId = await AddIngredientAsync("Sugar");
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(sugarId, 100m)]);

        await CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
        ], Cancellation);
        await CreateCostingParametersRepository().SetAsync(productId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, Cancellation);

        await CreateProductRepository().DeletePermanentlyAsync(productId, Cancellation);

        (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreatePackagingLineRepository().GetForProductAsync(productId, Cancellation)).Should().BeEmpty();
        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Test]
    public async Task CostingParametersRepository_GetForProductAsync_returns_null_before_any_are_set()
    {
        var productId = await SeedProductAsync("Ensaymada");

        (await CreateCostingParametersRepository().GetForProductAsync(productId, Cancellation)).Should().BeNull();
    }

    [Test]
    public async Task CostingParametersRepository_SetAsync_upserts_a_single_row()
    {
        var productId = await SeedProductAsync("Ensaymada");
        var repository = CreateCostingParametersRepository();

        await repository.SetAsync(productId, laborPercent: 5m, overheadPercent: 3m, administrativePercent: 1m, marginPercent: 40m, Cancellation);
        await repository.SetAsync(productId, laborPercent: 6m, overheadPercent: 3m, administrativePercent: 1.5m, marginPercent: 35m, Cancellation);

        var parameters = await repository.GetForProductAsync(productId, Cancellation);
        parameters.Should().NotBeNull();
        parameters!.LaborPercent.Should().Be(6m);
        parameters.AdministrativePercent.Should().Be(1.5m);
        parameters.MarginPercent.Should().Be(35m);

        await using var dbContext = CreateDbContext();
        (await dbContext.CostingParameters.CountAsync(c => c.ProductId == productId, Cancellation)).Should().Be(1);
    }

    [Test]
    public async Task CostingParametersRepository_SetAsync_rejects_negative_values()
    {
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateCostingParametersRepository().SetAsync(productId, laborPercent: -1m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task CostingParametersRepository_SetAsync_rejects_a_negative_administrative_cost()
    {
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreateCostingParametersRepository().SetAsync(productId, laborPercent: 0m, overheadPercent: 0m, administrativePercent: -1m, marginPercent: 0m, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
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

    [Test]
    public async Task ProductRepository_stores_the_description_and_stamps_each_save()
    {
        var repository = CreateProductRepository();
        var product = new Product { Name = "Cream Loaf", Description = "Soft bread with a creamy filling" };

        await repository.AddAsync(product, Cancellation);

        var added = (await repository.GetByIdAsync(product.Id, Cancellation))!;
        added.Description.Should().Be("Soft bread with a creamy filling");
        added.UpdatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));

        // Pushed back, so the update's stamp is plainly a new one.
        await using (var dbContext = CreateDbContext())
        {
            var stored = await dbContext.Products.SingleAsync(p => p.Id == product.Id, Cancellation);
            stored.UpdatedAt = new DateTime(2026, 1, 1, 8, 0, 0);
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await repository.UpdateAsync(new Product { Id = product.Id, Name = "Cream Loaf", Description = null }, Cancellation);

        var updated = (await repository.GetByIdAsync(product.Id, Cancellation))!;
        updated.Description.Should().BeNull();
        updated.UpdatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task ProductRepository_stores_the_current_price_and_clears_it()
    {
        var repository = CreateProductRepository();
        var product = new Product { Name = "Cream Loaf", CurrentPrice = 28.75m };

        await repository.AddAsync(product, Cancellation);
        (await repository.GetByIdAsync(product.Id, Cancellation))!.CurrentPrice.Should().Be(28.75m);

        await repository.UpdateAsync(new Product { Id = product.Id, Name = "Cream Loaf", CurrentPrice = 30m }, Cancellation);
        (await repository.GetByIdAsync(product.Id, Cancellation))!.CurrentPrice.Should().Be(30m);

        await repository.UpdateAsync(new Product { Id = product.Id, Name = "Cream Loaf", CurrentPrice = null }, Cancellation);
        (await repository.GetByIdAsync(product.Id, Cancellation))!.CurrentPrice.Should().BeNull();
    }

    [Test]
    public async Task ProductRepository_GetByIdAsync_returns_null_for_a_nonexistent_product()
    {
        (await CreateProductRepository().GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Test]
    public async Task ProductRepository_UpdateAsync_rejects_a_nonexistent_product()
    {
        var act = () => CreateProductRepository().UpdateAsync(new Product { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task SetLinesAsync_rejects_a_nonexistent_product()
    {
        var categoryId = await SeedPackagingCategoryAsync("Boxes");
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var boxId = await SeedPackagingMaterialAsync("6-Piece Box", categoryId, pieceId, purchaseUnitQty: 1m, currentCost: 30m);

        var act = () => CreatePackagingLineRepository().SetLinesAsync(999_999,
        [
            (boxId, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
        ], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
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
            (butterId, null, PackagingLineAuthoredField.QtyPerUnit, 1m),
        ], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("'Butter' is an ingredient*");
    }

    [Test]
    public async Task SetLinesAsync_rejects_a_nonexistent_packaging_material()
    {
        var productId = await SeedProductAsync("Ensaymada");

        var act = () => CreatePackagingLineRepository().SetLinesAsync(productId,
        [
            (999_999, null, PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m),
        ], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task RenameAsync_changes_the_name_and_nothing_else()
    {
        var clock = new TestClock(new DateTime(2026, 10, 1, 9, 0, 0));
        var repository = new ProductRepository(CreateDbContextFactory(clock));
        var id = await repository.SaveAsync(
            new Product { Name = "Mamon", Description = "Soft chiffon cake", QuantityRequired = 6, CurrentPrice = 120m },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        var before = (await repository.GetByIdAsync(id, Cancellation))!;

        clock.Advance(TimeSpan.FromHours(1));
        await repository.RenameAsync(id, "Mamon Tostado", Cancellation);

        var renamed = (await repository.GetByIdAsync(id, Cancellation))!;
        renamed.Name.Should().Be("Mamon Tostado");
        renamed.Description.Should().Be("Soft chiffon cake");
        renamed.QuantityRequired.Should().Be(6);
        renamed.CurrentPrice.Should().Be(120m);
        renamed.Stage.Should().Be(before.Stage);
        before.UpdatedAt.Should().Be(clock.Now - TimeSpan.FromHours(1));
        renamed.UpdatedAt.Should().Be(clock.Now, "a rename is a change to the product");
    }

    [Test]
    public async Task RenameAsync_to_another_products_name_is_refused()
    {
        var repository = CreateProductRepository();
        await SeedProductAsync("Mamon");
        var ensaymadaId = await SeedProductAsync("Ensaymada");

        var act = () => repository.RenameAsync(ensaymadaId, "Mamon", Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
        (await repository.GetByIdAsync(ensaymadaId, Cancellation))!.Name.Should().Be("Ensaymada");
    }
}
