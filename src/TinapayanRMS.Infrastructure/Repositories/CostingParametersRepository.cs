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

    public async Task SetAsync(
        int productId, decimal laborCost, decimal overheadCost, decimal administrativeCost, decimal marginPercent, decimal vatPercent, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await SetAsync(
            dbContext,
            new CostingParameters
            {
                ProductId = productId,
                LaborCost = laborCost,
                OverheadCost = overheadCost,
                AdministrativeCost = administrativeCost,
                MarginPercent = marginPercent,
                VatPercent = vatPercent,
            },
            cancellationToken);
    }

    /// <summary>Adds or updates <paramref name="costing"/>'s product's one row from its fields, and saves it. The entity itself is never attached.</summary>
    internal static async Task SetAsync(TinapayanRMSDbContext dbContext, CostingParameters costing, CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(
            costing.LaborCost, costing.OverheadCost, costing.AdministrativeCost, costing.MarginPercent, costing.VatPercent);

        var existing = await dbContext.CostingParameters.FirstOrDefaultAsync(c => c.ProductId == costing.ProductId, cancellationToken);
        if (existing is null)
        {
            existing = new CostingParameters { ProductId = costing.ProductId };
            dbContext.CostingParameters.Add(existing);
        }

        existing.LaborCost = costing.LaborCost;
        existing.OverheadCost = costing.OverheadCost;
        existing.AdministrativeCost = costing.AdministrativeCost;
        existing.MarginPercent = costing.MarginPercent;
        existing.VatPercent = costing.VatPercent;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
