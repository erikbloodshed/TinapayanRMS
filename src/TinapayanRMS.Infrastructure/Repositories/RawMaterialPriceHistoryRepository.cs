using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// The log of prices a material has been costed at. Entries are added by RawMaterialBrandPriceRepository
/// when a material's cost moves; deleting one only tidies the log, and never reprices anything.
/// </summary>
public sealed class RawMaterialPriceHistoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialPriceHistoryRepository
{
    /// <summary>Newest first, each with its unit (if any) and that unit's unit of measure.</summary>
    public Task<IReadOnlyList<RawMaterialPriceHistory>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken) =>
        GetAsync("WHERE h.raw_material_id = @rawMaterialId", new { rawMaterialId }, cancellationToken);

    /// <summary>Every material's entries, grouped by material and newest first within each, each with its unit as above.</summary>
    public Task<IReadOnlyList<RawMaterialPriceHistory>> GetAllAsync(CancellationToken cancellationToken) =>
        GetAsync(string.Empty, null, cancellationToken);

    private Task<IReadOnlyList<RawMaterialPriceHistory>> GetAsync(string filter, object? parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<RawMaterialPriceHistory>>(
            async dbContext => (await dbContext.Connection().QueryAsync<RawMaterialPriceHistory, RawMaterialUnit?, UnitOfMeasure?, RawMaterialPriceHistory>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<RawMaterialPriceHistory>("h")}, {Columns.Of<RawMaterialUnit>("u")}, {Columns.Of<UnitOfMeasure>("m")}
                    FROM raw_material_price_histories h
                    LEFT JOIN raw_material_units u ON u.id = h.unit_id
                    LEFT JOIN units_of_measure m ON m.id = u.unit_of_measure_id
                    {filter}
                    ORDER BY h.raw_material_id, h.date DESC, h.id DESC
                    """,
                    parameters,
                    cancellationToken),
                (entry, unit, unitOfMeasure) =>
                {
                    entry.Unit = unit;
                    unit?.UnitOfMeasure = unitOfMeasure;
                    return entry;
                },
                splitOn: "Id,Id")).AsList(),
            cancellationToken);

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await dbContext.RawMaterialPriceHistories.Where(h => h.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new BusinessRuleException($"Price history entry {id} does not exist.");
        }
    }
}
