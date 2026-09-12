using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class CostingParametersRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ICostingParametersRepository
{
    public async Task<CostingParameters?> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.CostingParameters
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProductId == productId, cancellationToken);
    }

    public async Task SetAsync(int productId, decimal laborCost, decimal overheadCost, decimal marginPercent, decimal vatPercent, CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(laborCost, overheadCost, marginPercent, vatPercent);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.CostingParameters.FirstOrDefaultAsync(c => c.ProductId == productId, cancellationToken);
        if (existing is null)
        {
            dbContext.CostingParameters.Add(new CostingParameters
            {
                ProductId = productId,
                LaborCost = laborCost,
                OverheadCost = overheadCost,
                MarginPercent = marginPercent,
                VatPercent = vatPercent,
            });
        }
        else
        {
            existing.LaborCost = laborCost;
            existing.OverheadCost = overheadCost;
            existing.MarginPercent = marginPercent;
            existing.VatPercent = vatPercent;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
