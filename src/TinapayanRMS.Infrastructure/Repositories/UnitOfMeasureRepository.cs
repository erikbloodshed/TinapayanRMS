using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class UnitOfMeasureRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IUnitOfMeasureRepository
{
    public Task<IReadOnlyList<UnitOfMeasure>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<UnitOfMeasure>(
            $"SELECT {Columns.Of<UnitOfMeasure>("u")} FROM UnitsOfMeasure u ORDER BY u.Name", null, cancellationToken);

    public async Task AddAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken)
    {
        unitOfMeasure.Name = UnitOfMeasureRules.NormalizeName(unitOfMeasure.Name);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.UnitsOfMeasure.Add(unitOfMeasure);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.UnitsOfMeasure.FirstOrDefaultAsync(u => u.Id == unitOfMeasure.Id, cancellationToken)
            ?? throw new BusinessRuleException($"Unit of measure {unitOfMeasure.Id} does not exist.");

        existing.Name = UnitOfMeasureRules.NormalizeName(unitOfMeasure.Name);
        existing.Abbreviation = unitOfMeasure.Abbreviation;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.UnitsOfMeasure.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
