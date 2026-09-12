using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class UnitOfMeasureRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IUnitOfMeasureRepository
{
    public async Task<IReadOnlyList<UnitOfMeasure>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.UnitsOfMeasure
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken)
    {
        unitOfMeasure.Name = UnitOfMeasureRules.NormalizeName(unitOfMeasure.Name);
        UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(unitOfMeasure.IsLengthUnit, unitOfMeasure.MillimetersPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.UnitsOfMeasure.Add(unitOfMeasure);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken)
    {
        UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(unitOfMeasure.IsLengthUnit, unitOfMeasure.MillimetersPerUnit);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == unitOfMeasure.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Unit of measure {unitOfMeasure.Id} does not exist.");

        existing.Name = UnitOfMeasureRules.NormalizeName(unitOfMeasure.Name);
        existing.Abbreviation = unitOfMeasure.Abbreviation;
        existing.IsLengthUnit = unitOfMeasure.IsLengthUnit;
        existing.MillimetersPerUnit = unitOfMeasure.MillimetersPerUnit;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.UnitsOfMeasure.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
