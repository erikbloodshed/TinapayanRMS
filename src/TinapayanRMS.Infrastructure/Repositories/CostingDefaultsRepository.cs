using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class CostingDefaultsRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ICostingDefaultsRepository
{
    public async Task<CostingDefaults> GetAsync(CancellationToken cancellationToken) =>
        await dbContextFactory.QuerySingleOrDefaultAsync<CostingDefaults>(
            $"SELECT {Columns.Of<CostingDefaults>("d")} FROM costing_defaults d WHERE d.id = @id",
            new { id = CostingDefaults.SingletonId },
            cancellationToken)
        ?? new CostingDefaults();

    public async Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(
            defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent);
        ProductRules.EnsureValidDefaultShrinkage(defaults.ShrinkagePercent);
        RawMaterialRules.EnsureValidCostBuffer(defaults.CostBufferPercent);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.CostingDefaults.FirstOrDefaultAsync(d => d.Id == CostingDefaults.SingletonId, cancellationToken);
        if (existing is null)
        {
            existing = new CostingDefaults();
            dbContext.CostingDefaults.Add(existing);
        }

        existing.LaborPercent = defaults.LaborPercent;
        existing.OverheadPercent = defaults.OverheadPercent;
        existing.AdministrativePercent = defaults.AdministrativePercent;
        existing.MarginPercent = defaults.MarginPercent;
        existing.VatPercent = defaults.VatPercent;
        existing.ShrinkagePercent = defaults.ShrinkagePercent;
        existing.NewProductStage = defaults.NewProductStage;
        existing.CostBufferPercent = defaults.CostBufferPercent;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
