using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingMaterialUnitRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingMaterialUnitRepository
{
    public async Task<IReadOnlyList<PackagingMaterialUnit>> GetForPackagingMaterialAsync(int packagingMaterialId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingMaterialUnits
            .AsNoTracking()
            .Include(u => u.UnitOfMeasure)
            .Where(u => u.PackagingMaterialId == packagingMaterialId)
            .OrderByDescending(u => u.BaseUnitsPerUnit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PackagingMaterialUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.BaseUnitsPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var baseUnitId = await dbContext.PackagingMaterials
            .Where(m => m.Id == unit.PackagingMaterialId)
            .Select(m => (int?)m.BaseUnitId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material {unit.PackagingMaterialId} does not exist.");

        PackagingMaterialRules.EnsureNotTheBaseUnit(baseUnitId, unit.UnitOfMeasureId);
        await EnsureNotAlreadyAddedAsync(dbContext, unit.PackagingMaterialId, unit.UnitOfMeasureId, excludeId: 0, cancellationToken);
        UnitConversionRules.EnsureCanAddAlternativeUnit(
            await dbContext.PackagingMaterialUnits.CountAsync(u => u.PackagingMaterialId == unit.PackagingMaterialId, cancellationToken));

        dbContext.PackagingMaterialUnits.Add(new PackagingMaterialUnit
        {
            PackagingMaterialId = unit.PackagingMaterialId,
            UnitOfMeasureId = unit.UnitOfMeasureId,
            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
            Description = unit.Description,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PackagingMaterialUnit unit, CancellationToken cancellationToken)
    {
        UnitConversionRules.EnsureValidMultiplier(unit.BaseUnitsPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.PackagingMaterialUnits
            .Include(u => u.PackagingMaterial)
            .FirstOrDefaultAsync(u => u.Id == unit.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material unit {unit.Id} does not exist.");

        PackagingMaterialRules.EnsureNotTheBaseUnit(existing.PackagingMaterial!.BaseUnitId, unit.UnitOfMeasureId);
        await EnsureNotAlreadyAddedAsync(dbContext, existing.PackagingMaterialId, unit.UnitOfMeasureId, existing.Id, cancellationToken);

        var multiplierChanged = existing.BaseUnitsPerUnit != unit.BaseUnitsPerUnit;
        existing.UnitOfMeasureId = unit.UnitOfMeasureId;
        existing.BaseUnitsPerUnit = unit.BaseUnitsPerUnit;
        existing.Description = unit.Description;

        if (multiplierChanged)
        {
            // The material's cost comes from its latest price, so a new multiplier only moves
            // CostPerBaseUnit when that price is in this unit. Packaging lines need nothing: their
            // cost is worked out from the unit's multiplier each time a product is read.
            var latest = await dbContext.PackagingPriceHistories
                .Where(h => h.PackagingMaterialId == existing.PackagingMaterialId)
                .OrderByDescending(h => h.Date)
                .ThenByDescending(h => h.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest?.PackagingMaterialUnitId == existing.Id)
            {
                PackagingMaterialRules.ApplyLatestPrice(existing.PackagingMaterial, latest.Price, existing.BaseUnitsPerUnit);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var unit = await dbContext.PackagingMaterialUnits
            .Include(u => u.UnitOfMeasure)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Packaging material unit {id} does not exist.");

        var priceCount = await dbContext.PackagingPriceHistories.CountAsync(h => h.PackagingMaterialUnitId == id, cancellationToken);
        if (priceCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {priceCount} price {(priceCount == 1 ? "entry" : "entries")}. Delete those first.");
        }

        var lineCount = await dbContext.PackagingLines.CountAsync(l => l.PackagingMaterialUnitId == id, cancellationToken);
        if (lineCount > 0)
        {
            throw new InvalidOperationException(
                $"'{unit.UnitOfMeasure!.Name}' is used by {lineCount} packaging {(lineCount == 1 ? "line" : "lines")}. Change those to another unit first.");
        }

        dbContext.PackagingMaterialUnits.Remove(unit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A material can have each unit of measure only once; the unique index would otherwise report it as a duplicate name.</summary>
    private static async Task EnsureNotAlreadyAddedAsync(
        TinapayanRMSDbContext dbContext, int packagingMaterialId, int unitOfMeasureId, int excludeId, CancellationToken cancellationToken)
    {
        var duplicate = await dbContext.PackagingMaterialUnits
            .AnyAsync(u => u.PackagingMaterialId == packagingMaterialId && u.UnitOfMeasureId == unitOfMeasureId && u.Id != excludeId, cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException("This material already has that unit.");
        }
    }
}
