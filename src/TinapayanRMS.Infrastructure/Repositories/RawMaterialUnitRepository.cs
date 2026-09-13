using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialUnitRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IRawMaterialUnitRepository
{
    public async Task<IReadOnlyList<RawMaterialUnit>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RawMaterialUnits
            .AsNoTracking()
            .Include(u => u.UnitOfMeasure)
            .Where(u => u.RawMaterialId == rawMaterialId)
            .OrderByDescending(u => u.BaseUnitsPerUnit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RawMaterialUnit unit, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddAsync(dbContext, unit, cancellationToken);
    }

    public async Task UpdateAsync(RawMaterialUnit unit, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for the unit, the cost and the lines' grams, so they can't disagree. It
        // commits before the cascade, which reads through its own connections.
        bool costChanged;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            costChanged = await UpdateAsync(dbContext, unit, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (costChanged)
        {
            // Every made ingredient using this one (its BOM lines' grams may just have changed) and
            // every sub-assembly using any of them, including through lines entered in this unit.
            await costPropagator.RecomputeAndPropagateAsync(unit.RawMaterialId, cancellationToken);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await DeleteAsync(dbContext, id, cancellationToken);
    }

    /// <summary>
    /// Adds the unit and saves it, rejecting a non-positive multiplier, a packaging material's own base
    /// unit, a unit the material already has, and a third unit.
    /// </summary>
    internal static async Task AddAsync(TinapayanRMSDbContext dbContext, RawMaterialUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.BaseUnitsPerUnit);

        var material = await dbContext.RawMaterials
            .Where(m => m.Id == unit.RawMaterialId)
            .Select(m => new { m.BaseUnitId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Raw material {unit.RawMaterialId} does not exist.");

        RawMaterialRules.EnsureNotTheBaseUnit(material.BaseUnitId, unit.UnitOfMeasureId);
        await EnsureNotAlreadyAddedAsync(dbContext, unit.RawMaterialId, unit.UnitOfMeasureId, excludeId: 0, cancellationToken);
        UnitConversionRules.EnsureCanAddAlternativeUnit(
            await dbContext.RawMaterialUnits.CountAsync(u => u.RawMaterialId == unit.RawMaterialId, cancellationToken));

        dbContext.RawMaterialUnits.Add(new RawMaterialUnit
        {
            RawMaterialId = unit.RawMaterialId,
            UnitOfMeasureId = unit.UnitOfMeasureId,
            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
            Description = unit.Description,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Changes the unit, multiplier or description of <paramref name="unit"/>, which must belong to its
    /// RawMaterialId, and saves it. A new multiplier reprices the material when its latest price is in
    /// this unit, and for an ingredient moves the grams of every recipe line entered in it. Returns
    /// whether that changed an ingredient's cost, for the caller to cascade once its transaction commits.
    /// </summary>
    internal static async Task<bool> UpdateAsync(TinapayanRMSDbContext dbContext, RawMaterialUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.BaseUnitsPerUnit);

        var existing = await dbContext.RawMaterialUnits
            .Include(u => u.RawMaterial)
            .FirstOrDefaultAsync(u => u.Id == unit.Id && u.RawMaterialId == unit.RawMaterialId, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material unit {unit.Id} does not exist.");

        var material = existing.RawMaterial!;
        RawMaterialRules.EnsureNotTheBaseUnit(material.BaseUnitId, unit.UnitOfMeasureId);
        await EnsureNotAlreadyAddedAsync(dbContext, existing.RawMaterialId, unit.UnitOfMeasureId, existing.Id, cancellationToken);

        var multiplierChanged = existing.BaseUnitsPerUnit != unit.BaseUnitsPerUnit;
        existing.UnitOfMeasureId = unit.UnitOfMeasureId;
        existing.BaseUnitsPerUnit = unit.BaseUnitsPerUnit;
        existing.Description = unit.Description;

        if (multiplierChanged)
        {
            // The material's cost comes from its latest price, so a new multiplier only moves
            // CostPerBaseUnit when that price is in this unit.
            var latest = await dbContext.RawMaterialPriceHistories
                .Where(h => h.RawMaterialId == existing.RawMaterialId)
                .OrderByDescending(h => h.Date)
                .ThenByDescending(h => h.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest?.UnitId == existing.Id)
            {
                RawMaterialRules.ApplyLatestPrice(material, latest.Price, existing.BaseUnitsPerUnit);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Packaging lines need nothing: their cost is worked out from the unit's multiplier each
        // time a product is read.
        if (!multiplierChanged || material.Type == RawMaterialType.Packaging)
        {
            return false;
        }

        // Recipe lines entered in this unit keep their quantity; their grams follow the new multiplier.
        var gramsPerUnit = existing.BaseUnitsPerUnit;
        await dbContext.SubAssemblyLines
            .Where(l => l.UnitId == existing.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.Grams, l => l.Quantity * gramsPerUnit), cancellationToken);
        await dbContext.IngredientBomLines
            .Where(l => l.UnitId == existing.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.Grams, l => l.Quantity * gramsPerUnit), cancellationToken);

        return true;
    }

    /// <summary>Removes the unit and saves, unless a price entry, recipe line or packaging line still uses it.</summary>
    internal static async Task DeleteAsync(TinapayanRMSDbContext dbContext, int id, CancellationToken cancellationToken)
    {
        var unit = await dbContext.RawMaterialUnits
            .Include(u => u.UnitOfMeasure)
            .Include(u => u.RawMaterial)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material unit {id} does not exist.");

        var priceCount = await dbContext.RawMaterialPriceHistories.CountAsync(h => h.UnitId == id, cancellationToken);
        if (priceCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {priceCount} price {(priceCount == 1 ? "entry" : "entries")}. Delete those first.");
        }

        // An ingredient's units can only be on recipe lines, a packaging material's on packaging lines.
        var (lineCount, lineKind) = unit.RawMaterial!.Type == RawMaterialType.Ingredient
            ? (await dbContext.SubAssemblyLines.CountAsync(l => l.UnitId == id, cancellationToken)
                + await dbContext.IngredientBomLines.CountAsync(l => l.UnitId == id, cancellationToken), "recipe")
            : (await dbContext.PackagingLines.CountAsync(l => l.UnitId == id, cancellationToken), "packaging");
        if (lineCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {lineCount} {lineKind} {(lineCount == 1 ? "line" : "lines")}. Change those to another unit first.");
        }

        dbContext.RawMaterialUnits.Remove(unit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A material can have each unit of measure only once; the unique index would otherwise report it as a duplicate name.</summary>
    private static async Task EnsureNotAlreadyAddedAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, int unitOfMeasureId, int excludeId, CancellationToken cancellationToken)
    {
        RawMaterialRules.EnsureUnitNotAlreadyAdded(await dbContext.RawMaterialUnits
            .AnyAsync(u => u.RawMaterialId == rawMaterialId && u.UnitOfMeasureId == unitOfMeasureId && u.Id != excludeId, cancellationToken));
    }
}
