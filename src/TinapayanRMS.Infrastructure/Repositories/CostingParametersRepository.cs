using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class CostingParametersRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ICostingParametersRepository
{
    // ProductId is unique, so there's at most one row.
    public Task<CostingParameters?> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.QuerySingleOrDefaultAsync<CostingParameters>(
            $"SELECT {Columns.Of<CostingParameters>("c")} FROM costing_parameters c WHERE c.product_id = @productId",
            new { productId },
            cancellationToken);

    public async Task<IReadOnlyList<CostingParameters>> GetForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productIds.Count == 0
            ? []
            : await dbContextFactory.QueryAsync<CostingParameters>(
                $"SELECT {Columns.Of<CostingParameters>("c")} FROM costing_parameters c WHERE c.product_id IN ({DapperReads.IdsTable})",
                new { ids = DapperReads.IdList(productIds) },
                cancellationToken);

    public async Task SetAsync(
        int productId, decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent, decimal vatPercent, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await SetAsync(
            dbContext,
            new CostingParameters
            {
                ProductId = productId,
                LaborPercent = laborPercent,
                OverheadPercent = overheadPercent,
                AdministrativePercent = administrativePercent,
                MarginPercent = marginPercent,
                VatPercent = vatPercent,
            },
            cancellationToken);
    }

    /// <summary>Adds or updates <paramref name="costing"/>'s product's one row from its fields, and saves it. The entity itself is never attached.</summary>
    internal static async Task SetAsync(TinapayanRMSDbContext dbContext, CostingParameters costing, CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(
            costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent, costing.VatPercent);

        var existing = await dbContext.CostingParameters.FirstOrDefaultAsync(c => c.ProductId == costing.ProductId, cancellationToken);
        if (existing is null)
        {
            existing = new CostingParameters { ProductId = costing.ProductId };
            dbContext.CostingParameters.Add(existing);
        }

        existing.LaborPercent = costing.LaborPercent;
        existing.OverheadPercent = costing.OverheadPercent;
        existing.AdministrativePercent = costing.AdministrativePercent;
        existing.MarginPercent = costing.MarginPercent;
        existing.VatPercent = costing.VatPercent;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
