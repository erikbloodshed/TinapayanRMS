using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IRawMaterialRepository
{
    public async Task<IReadOnlyList<RawMaterial>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(dbContext.RawMaterials)
            .OrderBy(m => m.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<RawMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(dbContext.RawMaterials)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task AddAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddAsync(dbContext, material, cancellationToken);
    }

    public async Task UpdateAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await UpdateAsync(dbContext, material, cancellationToken))
        {
            await costPropagator.RecomputeAndPropagateAsync(material.Id, cancellationToken);
        }
    }

    public async Task<int> SaveAsync(
        RawMaterial material,
        IReadOnlyList<RawMaterialUnit> units,
        (DateOnly Date, int? UnitOfMeasureId, decimal Price)? newPrice,
        IReadOnlyList<(int ComponentIngredientId, int? UnitId, decimal Quantity)>? bomLines,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for every part, so a part that's rejected leaves nothing half-saved. Each
        // step saves as it goes, so the checks of the steps after it see what it wrote. The cost
        // cascade runs once, after the commit, since it reads through its own connections.
        int id;
        var costChanged = false;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // A copy of the form's fields: the caller's entity is never attached, and never given an
            // id by a save that then rolls back.
            var fields = new RawMaterial
            {
                Id = material.Id,
                Name = material.Name,
                Type = material.Type,
                CategoryId = material.CategoryId,
                Source = material.Source,
                WeightLossPercent = material.WeightLossPercent,
                BaseUnitId = material.BaseUnitId,
                CostBufferPercent = material.CostBufferPercent,
            };

            if (fields.Id == 0)
            {
                await AddAsync(dbContext, fields, cancellationToken);
            }
            else
            {
                costChanged = await UpdateAsync(dbContext, fields, cancellationToken);
            }

            id = fields.Id;
            costChanged |= await SaveUnitsAsync(dbContext, id, units, cancellationToken);

            if (newPrice is { } price)
            {
                var entry = new RawMaterialPriceHistory
                {
                    RawMaterialId = id,
                    Date = price.Date,
                    UnitId = await FindUnitIdAsync(dbContext, id, price.UnitOfMeasureId, cancellationToken),
                    Price = price.Price,
                };
                costChanged |= await RawMaterialPriceHistoryRepository.AddAsync(dbContext, entry, cancellationToken);
            }

            if (bomLines is not null)
            {
                await IngredientBomLineRepository.SetLinesAsync(dbContext, id, bomLines, cancellationToken);
                costChanged = true;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (costChanged)
        {
            await costPropagator.RecomputeAndPropagateAsync(id, cancellationToken);
        }

        return id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.RawMaterials.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Adds <paramref name="material"/> and saves it, rejecting fields that don't fit its type or source.</summary>
    internal static async Task AddAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        RawMaterialRules.EnsureFieldsMatchType(material);
        RawMaterialRules.EnsureFieldsMatchSource(material);
        RawMaterialRules.EnsureValidCostBuffer(material.CostBufferPercent);

        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Copies the form's fields onto the stored material and saves it. Returns whether the caller must
    /// cascade its cost: a made ingredient's weight loss percent or an ingredient's buffer may have
    /// just changed.
    /// </summary>
    internal static async Task<bool> UpdateAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        RawMaterialRules.EnsureFieldsMatchType(material);
        RawMaterialRules.EnsureFieldsMatchSource(material);
        RawMaterialRules.EnsureValidCostBuffer(material.CostBufferPercent);

        var existing = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == material.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Raw material {material.Id} does not exist.");

        RawMaterialRules.EnsureTypeUnchanged(existing.Type, material.Type);
        RawMaterialRules.EnsureSourceUnchanged(existing.Source, material.Source);

        // Only a packaging material can get here: an ingredient's is always null (EnsureFieldsMatchType).
        if (existing.BaseUnitId != material.BaseUnitId)
        {
            RawMaterialRules.EnsureBaseUnitCanChange(
                hasUnits: await dbContext.RawMaterialUnits.AnyAsync(u => u.RawMaterialId == existing.Id, cancellationToken),
                hasPrices: await dbContext.RawMaterialPriceHistories.AnyAsync(h => h.RawMaterialId == existing.Id, cancellationToken),
                hasLines: await dbContext.PackagingLines.AnyAsync(l => l.PackagingMaterialId == existing.Id, cancellationToken));
        }

        // Field by field, not a blind Update(): CostPerBaseUnit and BufferedCostPerBaseUnit are owned
        // by the price-history and BOM rollups, and the units by RawMaterialUnitRepository, not by this form.
        existing.Name = material.Name;
        existing.CategoryId = material.CategoryId;
        existing.WeightLossPercent = material.WeightLossPercent;
        existing.BaseUnitId = material.BaseUnitId;

        // A purchased material's buffered cost follows its buffer here; a made one's is a BOM rollup,
        // which the cascade redoes.
        var bufferChanged = existing.CostBufferPercent != material.CostBufferPercent;
        existing.CostBufferPercent = material.CostBufferPercent;
        if (bufferChanged && existing.Source == RawMaterialSource.Purchased)
        {
            existing.BufferedCostPerBaseUnit = RawMaterialRules.BufferedCost(existing.CostPerBaseUnit, existing.CostBufferPercent);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // WeightLossPercent may have just changed; the cascade recomputes the cost from its current BOM.
        // A new buffer on an ingredient reaches the made ingredients and sub-assemblies using it; a
        // packaging material's needs nothing, since products cost their packaging lines on read.
        return existing.Source == RawMaterialSource.Made
            || (bufferChanged && existing.Type == RawMaterialType.Ingredient);
    }

    /// <summary>
    /// Makes the material's alternative units match <paramref name="units"/>: removes the saved ones
    /// missing from it, then updates those with an id, then adds those without. Removing first lets a
    /// replacement fit within the limit of two, and lets a removed unit of measure come back as a new
    /// row. Returns whether a corrected multiplier changed an ingredient's cost.
    /// </summary>
    private static async Task<bool> SaveUnitsAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, IReadOnlyList<RawMaterialUnit> units, CancellationToken cancellationToken)
    {
        var keptIds = units.Where(u => u.Id != 0).Select(u => u.Id).ToList();
        var removedIds = await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == rawMaterialId && !keptIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var removedId in removedIds)
        {
            await RawMaterialUnitRepository.DeleteAsync(dbContext, removedId, cancellationToken);
        }

        var costChanged = false;
        foreach (var unit in units.Where(u => u.Id != 0))
        {
            costChanged |= await RawMaterialUnitRepository.UpdateAsync(dbContext, ForMaterial(unit, rawMaterialId), cancellationToken);
        }

        foreach (var unit in units.Where(u => u.Id == 0))
        {
            await RawMaterialUnitRepository.AddAsync(dbContext, ForMaterial(unit, rawMaterialId), cancellationToken);
        }

        return costChanged;

        // The caller's units are never changed: a new material's didn't know its id yet.
        static RawMaterialUnit ForMaterial(RawMaterialUnit unit, int rawMaterialId) => new()
        {
            Id = unit.Id,
            RawMaterialId = rawMaterialId,
            UnitOfMeasureId = unit.UnitOfMeasureId,
            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
            Description = unit.Description,
        };
    }

    /// <summary>
    /// The id of the material's unit in <paramref name="unitOfMeasureId"/>, or null for a price per base
    /// unit. A new price names its unit by unit of measure because a unit added in the same save had no
    /// id yet; a material has each unit of measure at most once.
    /// </summary>
    private static async Task<int?> FindUnitIdAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, int? unitOfMeasureId, CancellationToken cancellationToken)
    {
        if (unitOfMeasureId is null)
        {
            return null;
        }

        return await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == rawMaterialId && u.UnitOfMeasureId == unitOfMeasureId)
            .Select(u => (int?)u.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A price's unit must be one of that material's own units.");
    }

    /// <summary>A material with what the screens show of it: its category, base unit, and alternative units (largest first).</summary>
    private static IQueryable<RawMaterial> WithDetails(IQueryable<RawMaterial> materials) =>
        materials
            .AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.BaseUnit)
            .Include(m => m.Units.OrderByDescending(u => u.BaseUnitsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure);
}
