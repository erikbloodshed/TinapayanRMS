using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Tests.Fixtures;

/// <summary>
/// Base for every Respawn-backed integration test class. The DbContext/DbContextFactory
/// plumbing (and the Respawn reset before each test) is identical across every one of
/// them, so it lives here once instead of being retyped per file.
///
/// Deliberately does NOT carry [Collection(DatabaseCollection.Name)] itself — that
/// attribute must stay on each concrete test class. xUnit's collection/fixture-sharing
/// discovery is not guaranteed to honor an attribute declared only on a base class, and
/// getting that wrong would silently break the Respawn-reset isolation this whole
/// hierarchy exists to guarantee (tests could then run in parallel against the shared
/// database instead of being serialized into one collection).
/// </summary>
public abstract class PersistenceTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected TinapayanRMSDbContext CreateDbContext() =>
        new(CreateDbContextOptions());

    protected IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() =>
        new TestDbContextFactory(CreateDbContextOptions());

    /// <summary>The ingredient cost cascade the raw material repositories hand ingredient cost changes to.</summary>
    protected IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

    protected RawMaterialBrandPriceRepository CreateBrandPriceRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    /// <summary>
    /// Prices a purchased material at a single, unnamed brand (<paramref name="unitId"/> one of its own units, or null for
    /// a price per base unit), replacing any it had, as the Purchase Cost Setup's Save does. The cost cascades.
    /// </summary>
    protected Task SetPriceAsync(int rawMaterialId, int? unitId, decimal price) =>
        CreateBrandPriceRepository().SetAsync(rawMaterialId, [new RawMaterialBrandPrice { UnitId = unitId, Price = price }], Cancellation);

    /// <summary>
    /// UnitOfMeasure has no migration-seeded rows (Phase: units are fully user-managed,
    /// no protected/well-known ids), so any test that needs a unit as an FK target seeds
    /// its own via this helper rather than relying on a shared baseline row.
    /// </summary>
    protected async Task<int> SeedUnitOfMeasureAsync(string name, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var unit = new UnitOfMeasure { Name = name, Abbreviation = abbreviation };
        dbContext.UnitsOfMeasure.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>
    /// For a helper that seeds several rows in the same test and just needs "any valid
    /// unit" for each (the unit itself is never the point of the test) -- reuses the
    /// existing row by name within the test instead of colliding with the unique Name
    /// index on a second SeedUnitOfMeasureAsync("piece", ...) call.
    /// </summary>
    protected async Task<int> GetOrCreateUnitOfMeasureAsync(string name, string abbreviation)
    {
        await using var dbContext = CreateDbContext();
        var existing = await dbContext.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Name == name, Cancellation);
        if (existing is not null)
        {
            return existing.Id;
        }

        var unit = new UnitOfMeasure { Name = name, Abbreviation = abbreviation };
        dbContext.UnitsOfMeasure.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>
    /// Gives an ingredient a "kilogram" alternative unit of 1,000 g and returns the unit's id, so a
    /// test can price it per kilogram (₱90 a kilogram is ₱0.09 a gram).
    /// </summary>
    protected async Task<int> AddKilogramUnitAsync(int ingredientId)
    {
        var kilogramId = await GetOrCreateUnitOfMeasureAsync("kilogram", "kg");

        await using var dbContext = CreateDbContext();
        var unit = new RawMaterialUnit { RawMaterialId = ingredientId, UnitOfMeasureId = kilogramId, BaseUnitsPerUnit = 1000m };
        dbContext.RawMaterialUnits.Add(unit);
        await dbContext.SaveChangesAsync(Cancellation);
        return unit.Id;
    }

    /// <summary>The id of the kilogram unit AddKilogramUnitAsync gave <paramref name="ingredientId"/>.</summary>
    protected async Task<int> KilogramUnitIdAsync(int ingredientId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == ingredientId && u.UnitOfMeasure!.Name == "kilogram")
            .Select(u => u.Id)
            .SingleAsync(Cancellation);
    }

    /// <summary>An empty product, for a test to fill.</summary>
    protected async Task<int> AddProductAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var product = new Product { Name = name };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(Cancellation);
        return product.Id;
    }

    /// <summary>A role for a test's sub-assemblies; <paramref name="isPackaging"/> makes it the role packaging sets go in.</summary>
    protected async Task<int> AddRoleAsync(string name, int sortOrder = 1, bool isPackaging = false)
    {
        await using var dbContext = CreateDbContext();
        var role = new SubAssemblyRole { Name = name, SortOrder = sortOrder, IsPackaging = isPackaging };
        dbContext.SubAssemblyRoles.Add(role);
        await dbContext.SaveChangesAsync(Cancellation);
        return role.Id;
    }

    /// <summary>A packaging category, which is all a packaging material can be filed under (RawMaterialRules.EnsureCategoryMatchesType).</summary>
    protected async Task<int> AddPackagingCategoryAsync(string name = "Boxes")
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = name, Type = RawMaterialType.Packaging };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>A purchased ingredient, in a shared "Ingredients" category, costing <paramref name="costPerGram"/> a gram with no buffer.</summary>
    protected async Task<int> AddIngredientAsync(string name, decimal costPerGram = 0m)
    {
        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Ingredients", Cancellation)
            ?? new RawMaterialCategory { Name = "Ingredients" };
        var ingredient = new RawMaterial
        {
            Name = name,
            Category = category,
            Source = RawMaterialSource.Purchased,
            CostPerBaseUnit = costPerGram,
            BufferedCostPerBaseUnit = costPerGram,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    /// <summary>Saves <paramref name="productId"/>'s recipe as the Products screen's Save does (ProductRepository.SaveAsync), keeping its name.</summary>
    protected async Task SaveRecipeAsync(int productId, RecipeDraft recipe)
    {
        var repository = new TinapayanRMS.Infrastructure.Repositories.ProductRepository(CreateDbContextFactory());
        var product = (await repository.GetByIdAsync(productId, Cancellation))!;
        await repository.SaveAsync(product, recipe, packagingLink: null, packagingLines: null, packs: null, costing: null, Cancellation);
    }

    /// <summary>The id of <paramref name="productId"/>'s own sub-assembly in the role <paramref name="roleId"/>, which it has one of.</summary>
    protected async Task<int> OwnedSubAssemblyIdAsync(int productId, int roleId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies
            .Where(s => s.OwnerProductId == productId && s.RoleId == roleId)
            .Select(s => s.Id)
            .SingleAsync(Cancellation);
    }

    /// <summary>The id of <paramref name="productId"/>'s usage of <paramref name="subAssemblyId"/>, which its add-ins hang off.</summary>
    protected async Task<int> UsageIdAsync(int productId, int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId && u.SubAssemblyId == subAssemblyId)
            .Select(u => u.Id)
            .SingleAsync(Cancellation);
    }

    /// <summary>
    /// A new product owning one sub-assembly, in the role <paramref name="roleId"/>, made of <paramref name="lines"/>
    /// (grams), used at <paramref name="weightPerPiece"/> grams a piece, saved through ProductRepository.SaveAsync.
    /// </summary>
    protected async Task<(int ProductId, int SubAssemblyId)> AddProductWithSubAssemblyAsync(
        string productName, int roleId, IReadOnlyList<RecipeLine> lines, decimal weightPerPiece = 10m)
    {
        var productId = await AddProductAsync(productName);
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, roleId, lines, UsageAuthoredField.WeightPerPiece, weightPerPiece)],
            []));
        return (productId, await OwnedSubAssemblyIdAsync(productId, roleId));
    }

    private DbContextOptions<TinapayanRMSDbContext> CreateDbContextOptions() =>
        new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options;

    private sealed class TestDbContextFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }
}
