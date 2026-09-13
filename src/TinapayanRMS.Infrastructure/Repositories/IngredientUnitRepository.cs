using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class IngredientUnitRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IIngredientUnitRepository
{
    public async Task<IReadOnlyList<IngredientUnit>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.IngredientUnits
            .AsNoTracking()
            .Include(u => u.UnitOfMeasure)
            .Where(u => u.IngredientId == ingredientId)
            .OrderByDescending(u => u.GramsPerUnit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(IngredientUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.GramsPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredientExists = await dbContext.Ingredients.AnyAsync(i => i.Id == unit.IngredientId, cancellationToken);
        if (!ingredientExists)
        {
            throw new InvalidOperationException($"Ingredient {unit.IngredientId} does not exist.");
        }

        await EnsureNotAlreadyAddedAsync(dbContext, unit.IngredientId, unit.UnitOfMeasureId, excludeId: 0, cancellationToken);
        UnitConversionRules.EnsureCanAddAlternativeUnit(
            await dbContext.IngredientUnits.CountAsync(u => u.IngredientId == unit.IngredientId, cancellationToken));

        dbContext.IngredientUnits.Add(new IngredientUnit
        {
            IngredientId = unit.IngredientId,
            UnitOfMeasureId = unit.UnitOfMeasureId,
            GramsPerUnit = unit.GramsPerUnit,
            Description = unit.Description,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(IngredientUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.GramsPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.IngredientUnits.FirstOrDefaultAsync(u => u.Id == unit.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient unit {unit.Id} does not exist.");

        await EnsureNotAlreadyAddedAsync(dbContext, existing.IngredientId, unit.UnitOfMeasureId, existing.Id, cancellationToken);

        var multiplierChanged = existing.GramsPerUnit != unit.GramsPerUnit;
        existing.UnitOfMeasureId = unit.UnitOfMeasureId;
        existing.GramsPerUnit = unit.GramsPerUnit;
        existing.Description = unit.Description;

        if (!multiplierChanged)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // One transaction for the unit, the cost and the lines' grams, so they can't disagree. It
        // commits before the cascade, which reads through its own connections.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // The ingredient's cost comes from its latest price, so a new multiplier only moves
            // CostPerGram when that price is in this unit.
            var latest = await dbContext.IngredientPriceHistories
                .Where(h => h.IngredientId == existing.IngredientId)
                .OrderByDescending(h => h.Date)
                .ThenByDescending(h => h.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest?.IngredientUnitId == existing.Id)
            {
                var ingredient = await dbContext.Ingredients.SingleAsync(i => i.Id == existing.IngredientId, cancellationToken);
                IngredientRules.ApplyLatestPrice(ingredient, latest.Price, existing.GramsPerUnit);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            // Recipe lines entered in this unit keep their quantity; their grams follow the new multiplier.
            var gramsPerUnit = existing.GramsPerUnit;
            await dbContext.SubAssemblyLines
                .Where(l => l.IngredientUnitId == existing.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.Grams, l => l.Quantity * gramsPerUnit), cancellationToken);
            await dbContext.IngredientBomLines
                .Where(l => l.IngredientUnitId == existing.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.Grams, l => l.Quantity * gramsPerUnit), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Every made ingredient using this one (its BOM lines' grams may just have changed) and
        // every sub-assembly using any of them, including through lines entered in this unit.
        await costPropagator.RecomputeAndPropagateAsync(existing.IngredientId, cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var unit = await dbContext.IngredientUnits
            .Include(u => u.UnitOfMeasure)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient unit {id} does not exist.");

        var priceCount = await dbContext.IngredientPriceHistories.CountAsync(h => h.IngredientUnitId == id, cancellationToken);
        if (priceCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {priceCount} price {(priceCount == 1 ? "entry" : "entries")}. Delete those first.");
        }

        var lineCount = await dbContext.SubAssemblyLines.CountAsync(l => l.IngredientUnitId == id, cancellationToken)
            + await dbContext.IngredientBomLines.CountAsync(l => l.IngredientUnitId == id, cancellationToken);
        if (lineCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {lineCount} recipe {(lineCount == 1 ? "line" : "lines")}. Change those to another unit first.");
        }

        dbContext.IngredientUnits.Remove(unit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>An ingredient can have each unit of measure only once; the unique index would otherwise report it as a duplicate name.</summary>
    private static async Task EnsureNotAlreadyAddedAsync(
        TinapayanRMSDbContext dbContext, int ingredientId, int unitOfMeasureId, int excludeId, CancellationToken cancellationToken)
    {
        var duplicate = await dbContext.IngredientUnits
            .AnyAsync(u => u.IngredientId == ingredientId && u.UnitOfMeasureId == unitOfMeasureId && u.Id != excludeId, cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException("This ingredient already has that unit.");
        }
    }
}
