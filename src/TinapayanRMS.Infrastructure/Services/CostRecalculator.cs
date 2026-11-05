using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Settings' Recalculate Costs (2026-10-03): every made and linked ingredient worked out again by the cascade's own code
/// from what's stored, in dependency order and one transaction. A safety net, not part of any save: since the same day the
/// cascade commits with the save that causes it, so a stored cost can only go wrong through a change made outside the app.
/// Purchased materials aren't touched: their cost comes straight from their own saved prices. Nothing else is stored to
/// put right: sub-assemblies and products are costed from their lines as they're read.
/// </summary>
public sealed class CostRecalculator(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ICostRecalculator
{
    public async Task<CostRecalculation> RecalculateAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);

        var ingredients = await IngredientCostPropagator.RecomputeAllAsync(dbContext, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CostRecalculation(ingredients);
    }
}
