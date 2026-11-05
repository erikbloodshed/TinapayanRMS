using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Writes that are all or nothing (2026-10-03). The cost cascade runs inside the transaction of the save that causes it:
/// these make it fail part-way, as a dropped connection would, and check the save went with it. Until then the save was
/// committed first and the cascade ran after, on contexts of its own, so a failure left the stored costs above it stale for
/// good. Unlinking a product's packaging is one transaction too. And Settings' Recalculate Costs puts right a stored cost
/// changed outside the app.
/// </summary>
[Collection(DatabaseCollection.B)]
public class CostCascadeTransactionPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_price_whose_cascade_fails_is_not_saved_either()
    {
        var (flourId, syrupId) = await SeedFlourAndSyrupAsync();
        var failing = FactoryFailingWhen(entry => entry is { Entity: RawMaterial { Source: RawMaterialSource.Made }, State: EntityState.Modified });

        var act = () => new RawMaterialBrandPriceRepository(failing).SetAsync(flourId, [new RawMaterialBrandPrice { Price = 0.08m }], Cancellation);

        await act.Should().ThrowAsync<SimulatedFailureException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == flourId, Cancellation)).CostPerBaseUnit.Should().Be(0.05m, "the new price went with its cascade");
        (await dbContext.RawMaterialBrandPrices.SingleAsync(p => p.RawMaterialId == flourId, Cancellation)).Price.Should().Be(0.05m);
        (await dbContext.RawMaterialPriceHistories.CountAsync(h => h.RawMaterialId == flourId, Cancellation)).Should().Be(1, "only the first price was logged");
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation)).CostPerBaseUnit.Should().Be(0.05m);
    }

    [Fact]
    public async Task A_sub_assembly_line_edit_whose_made_ingredients_cascade_fails_is_not_saved_either()
    {
        var (flourId, syrupId) = await SeedFlourAndSyrupAsync();
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var recipeId = await RecipeIdOfAsync(syrupId);
        SubAssembly recipe;
        await using (var read = CreateDbContext())
        {
            recipe = await read.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == recipeId, Cancellation);
        }

        var failing = FactoryFailingWhen(entry => entry is { Entity: RawMaterial { Source: RawMaterialSource.Made }, State: EntityState.Modified });

        var act = () => new SubAssemblyRepository(failing).SaveStandaloneAsync(recipe, [new RecipeLine(flourId, 1000m), new RecipeLine(sugarId, 1000m)], Cancellation);

        await act.Should().ThrowAsync<SimulatedFailureException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == recipeId).Select(l => l.IngredientId).ToListAsync(Cancellation))
            .Should().Equal([flourId], "the old lines' delete went with the made ingredient's cascade too");
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation)).CostPerBaseUnit.Should().Be(0.05m);
    }

    [Fact]
    public async Task A_save_whose_cascade_fails_leaves_the_material_as_it_was()
    {
        var (_, syrupId) = await SeedFlourAndSyrupAsync();
        var repository = new RawMaterialRepository(CreateDbContextFactory());
        var syrup = (await repository.GetByIdAsync(syrupId, Cancellation))!;
        syrup.Name = "Light Syrup";
        syrup.WeightLossPercent = 20m;
        var failing = FactoryFailingWhen(entry => entry is { Entity: RawMaterial { Source: RawMaterialSource.Made }, State: EntityState.Modified }
            && entry.Property(nameof(RawMaterial.CostPerBaseUnit)).IsModified);

        var act = () => new RawMaterialRepository(failing).SaveAsync(syrup, [], brandPrices: null, addIns: null, Cancellation);

        await act.Should().ThrowAsync<SimulatedFailureException>();
        var stored = (await repository.GetByIdAsync(syrupId, Cancellation))!;
        stored.Name.Should().Be("Syrup", "the form's fields went with the cascade their weight loss caused");
        stored.WeightLossPercent.Should().Be(0m);
        stored.CostPerBaseUnit.Should().Be(0.05m);
    }

    [Fact]
    public async Task Unlinking_packaging_whose_lines_fail_to_save_keeps_the_link()
    {
        var (smallId, bigId) = await SeedSmallLinkedToBigsPackagingAsync();
        var failing = FactoryFailingWhen(entry => entry is { Entity: PackagingLine, State: EntityState.Added });

        var act = () => new PackagingLinkRepository(failing).RemoveLinkAsync(smallId, Cancellation);

        await act.Should().ThrowAsync<SimulatedFailureException>();
        var link = await new PackagingLinkRepository(CreateDbContextFactory()).GetForProductAsync(smallId, Cancellation);
        link!.LinkedProductId.Should().Be(bigId, "the link stays until the lines it showed are saved in its place");
    }

    [Fact]
    public async Task Unlinking_packaging_gives_the_product_the_lines_it_showed()
    {
        var (smallId, _) = await SeedSmallLinkedToBigsPackagingAsync();

        await new PackagingLinkRepository(CreateDbContextFactory()).RemoveLinkAsync(smallId, Cancellation);

        (await new PackagingLinkRepository(CreateDbContextFactory()).GetForProductAsync(smallId, Cancellation)).Should().BeNull();
        (await new PackagingLineRepository(CreateDbContextFactory()).GetForProductAsync(smallId, Cancellation))
            .Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m);
    }

    [Fact]
    public async Task Recalculate_costs_puts_right_stored_costs_changed_outside_the_app_and_says_how_many()
    {
        var (_, syrupId) = await SeedFlourAndSyrupAsync();
        var roleId = await AddRoleAsync("Dough");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [new RecipeLine(syrupId, 1000m)]);

        // As a script run on the database might leave them.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == syrupId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.CostPerBaseUnit, 9m).SetProperty(m => m.BufferedCostPerBaseUnit, 9m), Cancellation);
            await dbContext.SubAssemblies.Where(s => s.Id == doughId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.TotalCost, 1m), Cancellation);
        }

        var result = await new CostRecalculator(CreateDbContextFactory()).RecalculateAllAsync(Cancellation);

        result.Should().Be(new CostRecalculation(IngredientsCorrected: 1, SubAssembliesCorrected: 1));
        await using var check = CreateDbContext();
        var syrup = await check.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == syrupId, Cancellation);
        syrup.CostPerBaseUnit.Should().Be(0.05m, "₱50 of flour over the 1,000 g it makes");
        syrup.BufferedCostPerBaseUnit.Should().Be(0.05m);
        (await check.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation)).TotalCost.Should().Be(50m);

        var again = await new CostRecalculator(CreateDbContextFactory()).RecalculateAllAsync(Cancellation);

        again.Should().Be(new CostRecalculation(0, 0), "nothing's out of date any more");
        (await check.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == syrupId, Cancellation)).CostUpdatedAt
            .Should().Be(syrup.CostUpdatedAt, "a cost recalculated to what it already was hasn't changed");
    }

    /// <summary>Flour priced at ₱0.05 a gram, and Syrup made from a recipe of 1,000 g of it with no weight loss: ₱0.05 a gram too.</summary>
    private async Task<(int FlourId, int SyrupId)> SeedFlourAndSyrupAsync()
    {
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);

        var syrupId = await AddMadeIngredientAsync("Syrup", [(flourId, 1000m)]);
        return (flourId, syrupId);
    }

    /// <summary>Big takes 2 boxes a piece; Small's packaging links Big's.</summary>
    private async Task<(int SmallId, int BigId)> SeedSmallLinkedToBigsPackagingAsync()
    {
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync();

        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial { Name = "Box", Type = RawMaterialType.Packaging, CategoryId = categoryId, BaseUnitId = pieceId, CostPerBaseUnit = 5m, BufferedCostPerBaseUnit = 5m };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        var bigId = await AddProductAsync("Big");
        var smallId = await AddProductAsync("Small");
        await new PackagingLineRepository(CreateDbContextFactory()).SetLinesAsync(
            bigId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)], Cancellation);
        await new PackagingLinkRepository(CreateDbContextFactory()).SetLinkAsync(smallId, bigId, Cancellation);
        return (smallId, bigId);
    }

    /// <summary>A factory whose contexts fail any save that <paramref name="fails"/> picks an entry of, at the moment it would be written.</summary>
    private FailingFactory FactoryFailingWhen(Func<EntityEntry, bool> fails)
    {
        string connectionString;
        using (var dbContext = CreateDbContext())
        {
            connectionString = dbContext.Database.GetConnectionString()!;
        }

        var options = new DbContextOptionsBuilder<TinapayanRMSDbContext>();
        TinapayanRMSDbContext.Configure(options, connectionString).AddInterceptors(new FailingSave(fails));
        return new FailingFactory(options.Options);
    }

    private sealed class FailingFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }

    private sealed class FailingSave(Func<EntityEntry, bool> fails) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries().Any(fails)
                ? throw new SimulatedFailureException()
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class SimulatedFailureException : Exception
    {
        public SimulatedFailureException()
            : base("Simulated: the connection dropped part-way through.")
        {
        }
    }
}
