using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialBrandPriceRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialBrandPriceRepository
{
    /// <summary>In the order they were added, each with its unit (if any) and that unit's unit of measure.</summary>
    public Task<IReadOnlyList<RawMaterialBrandPrice>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken) =>
        ReadAsync("WHERE p.raw_material_id = @rawMaterialId ORDER BY p.id", new { rawMaterialId }, cancellationToken);

    public Task<IReadOnlyList<RawMaterialBrandPrice>> GetAllAsync(CancellationToken cancellationToken) =>
        ReadAsync("ORDER BY p.raw_material_id, p.id", null, cancellationToken);

    /// <summary>Brand prices with their unit and its unit of measure, filtered and ordered by <paramref name="whereAndOrder"/>.</summary>
    private Task<IReadOnlyList<RawMaterialBrandPrice>> ReadAsync(string whereAndOrder, object? parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<RawMaterialBrandPrice>>(
            async dbContext => (await dbContext.Connection().QueryAsync<RawMaterialBrandPrice, RawMaterialUnit?, UnitOfMeasure?, RawMaterialBrandPrice>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<RawMaterialBrandPrice>("p")}, {Columns.Of<RawMaterialUnit>("u")}, {Columns.Of<UnitOfMeasure>("m")}
                    FROM raw_material_brand_prices p
                    LEFT JOIN raw_material_units u ON u.id = p.unit_id
                    LEFT JOIN units_of_measure m ON m.id = u.unit_of_measure_id
                    {whereAndOrder}
                    """,
                    parameters,
                    cancellationToken),
                (brandPrice, unit, unitOfMeasure) =>
                {
                    brandPrice.Unit = unit;
                    unit?.UnitOfMeasure = unitOfMeasure;
                    return brandPrice;
                },
                splitOn: "Id,Id")).AsList(),
            cancellationToken);

    public async Task SetAsync(int rawMaterialId, IReadOnlyList<RawMaterialBrandPrice> brandPrices, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The prices, the cost they give and its cascade commit together.
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var material = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == rawMaterialId, cancellationToken)
            ?? throw new BusinessRuleException($"Raw material {rawMaterialId} does not exist.");
        var costBefore = material.CostPerBaseUnit;

        await RemoveAllAsync(dbContext, rawMaterialId, cancellationToken);
        await AddAllAsync(dbContext, material, brandPrices, cancellationToken);
        var costChanged = await RepriceAsync(dbContext, material, costBefore, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (costChanged)
        {
            await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, rawMaterialId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Removes every brand price of the material, so the units they were in can be removed too.</summary>
    internal static Task RemoveAllAsync(TinapayanRMSDbContext dbContext, int rawMaterialId, CancellationToken cancellationToken) =>
        dbContext.RawMaterialBrandPrices.Where(p => p.RawMaterialId == rawMaterialId).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Adds <paramref name="brandPrices"/> to a purchased material that has none, and saves. Each must be
    /// positive, in one of the material's own units (or the base unit), and a brand is priced once per unit.
    /// </summary>
    internal static async Task AddAllAsync(
        TinapayanRMSDbContext dbContext, RawMaterial material, IReadOnlyList<RawMaterialBrandPrice> brandPrices, CancellationToken cancellationToken)
    {
        if (brandPrices.Count == 0)
        {
            return;
        }

        if (material.Source != RawMaterialSource.Purchased)
        {
            throw new BusinessRuleException("Only a purchased material has purchase prices.");
        }

        RawMaterialRules.EnsureDistinctBrandPrices(brandPrices.Select(p => (p.Brand, p.UnitId)));

        var unitIds = await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == material.Id)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var brandPrice in brandPrices)
        {
            RawMaterialRules.EnsureValidBrandPrice(brandPrice.Price);
            if (brandPrice.UnitId is { } unitId && !unitIds.Contains(unitId))
            {
                throw new BusinessRuleException("A price's unit must be one of that material's own units.");
            }

            // A copy: the caller's row is never attached, and never given an id by a save that then rolls back.
            dbContext.RawMaterialBrandPrices.Add(new RawMaterialBrandPrice
            {
                RawMaterialId = material.Id,
                Brand = RawMaterialRules.NormalizeBrand(brandPrice.Brand),
                UnitId = brandPrice.UnitId,
                Price = brandPrice.Price,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reprices a purchased material from its saved brand prices (ApplyBrandPrices: its preferred brand's, else the
    /// dearest) without saving. When
    /// its cost is no longer <paramref name="costBefore"/>, the brand price it now comes from is logged in
    /// price history, dated today. Returns whether an ingredient's cost changed, which the caller cascades
    /// before its transaction commits; a packaging material's needs nothing, since products cost their
    /// packaging lines on read.
    /// </summary>
    internal static async Task<bool> RepriceAsync(
        TinapayanRMSDbContext dbContext, RawMaterial material, decimal costBefore, CancellationToken cancellationToken)
    {
        if (material.Source != RawMaterialSource.Purchased)
        {
            return false;
        }

        var brandPrices = await dbContext.RawMaterialBrandPrices
            .Where(p => p.RawMaterialId == material.Id)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Brand, p.UnitId, p.Price, BaseUnitsPerUnit = p.Unit == null ? (decimal?)null : p.Unit.BaseUnitsPerUnit })
            .ToListAsync(cancellationToken);

        // The buffered cost only moves here on a first pricing, or back to 0 with the last price gone.
        var bufferedBefore = material.BufferedCostPerBaseUnit;
        var dearest = RawMaterialRules.ApplyBrandPrices(material, [.. brandPrices.Select(p => (p.Brand, p.Price, p.BaseUnitsPerUnit))]);
        if (material.CostPerBaseUnit == costBefore)
        {
            return material.BufferedCostPerBaseUnit != bufferedBefore && material.Type == RawMaterialType.Ingredient;
        }

        // Nothing to log when the last price went: the material is simply unpriced again.
        if (dearest is { } index)
        {
            dbContext.RawMaterialPriceHistories.Add(new RawMaterialPriceHistory
            {
                RawMaterialId = material.Id,
                Date = DateOnly.FromDateTime(DateTime.Today),
                UnitId = brandPrices[index].UnitId,
                Brand = brandPrices[index].Brand,
                Price = brandPrices[index].Price,
            });
        }

        return material.Type == RawMaterialType.Ingredient;
    }
}
