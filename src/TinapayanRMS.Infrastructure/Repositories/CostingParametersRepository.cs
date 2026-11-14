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
        int productId, decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await SetAsync(
            dbContext,
            productId,
            new CostingParameters
            {
                LaborPercent = laborPercent,
                OverheadPercent = overheadPercent,
                AdministrativePercent = administrativePercent,
                MarginPercent = marginPercent,
            },
            cancellationToken);
    }

    /// <summary>
    /// Adds or updates <paramref name="productId"/>'s one row with <paramref name="costing"/>'s percentages, and saves it.
    /// Whose they were isn't read: a product's Save passes the form's and a clone its source's, so a percentage added to
    /// the entity reaches both through <see cref="CopyFields"/> alone. The entity itself is never attached.
    /// </summary>
    internal static async Task SetAsync(TinapayanRMSDbContext dbContext, int productId, CostingParameters costing, CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent);

        var existing = await dbContext.CostingParameters.FirstOrDefaultAsync(c => c.ProductId == productId, cancellationToken);
        CopyFields(costing, existing ?? dbContext.CostingParameters.Add(new CostingParameters { ProductId = productId }).Entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetForProductsAsync(
        IReadOnlyCollection<int> productIds,
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        CancellationToken cancellationToken)
    {
        CostingRules.EnsureValidParameters(laborPercent, overheadPercent, administrativePercent, marginPercent);
        if (productIds.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        List<int> ids = [.. productIds.Distinct()];
        var existing = await dbContext.CostingParameters.Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, cancellationToken);
        var costing = new CostingParameters
        {
            LaborPercent = laborPercent,
            OverheadPercent = overheadPercent,
            AdministrativePercent = administrativePercent,
            MarginPercent = marginPercent,
        };
        foreach (var productId in ids)
        {
            CopyFields(costing, existing.GetValueOrDefault(productId) ?? dbContext.CostingParameters.Add(new CostingParameters { ProductId = productId }).Entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The four percentages from <paramref name="costing"/> onto a product's stored row.</summary>
    private static void CopyFields(CostingParameters costing, CostingParameters row)
    {
        row.LaborPercent = costing.LaborPercent;
        row.OverheadPercent = costing.OverheadPercent;
        row.AdministrativePercent = costing.AdministrativePercent;
        row.MarginPercent = costing.MarginPercent;
    }
}
