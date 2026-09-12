using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class IngredientRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IngredientCostPropagator costPropagator) : IIngredientRepository
{
    public async Task<IReadOnlyList<Ingredient>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Ingredients
            .AsNoTracking()
            .Include(i => i.IngredientCategory)
            .Include(i => i.PurchaseUnit)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Ingredient?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Ingredients
            .AsNoTracking()
            .Include(i => i.IngredientCategory)
            .Include(i => i.PurchaseUnit)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task AddAsync(Ingredient ingredient, CancellationToken cancellationToken)
    {
        IngredientRules.EnsureFieldsMatchSource(ingredient);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Ingredient ingredient, CancellationToken cancellationToken)
    {
        IngredientRules.EnsureFieldsMatchSource(ingredient);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredient.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {ingredient.Id} does not exist.");

        IngredientRules.EnsureSourceUnchanged(existing.Source, ingredient.Source);

        // Field by field, not a blind Update(): CurrentCost/CostPerGram are owned by the
        // price-history and BOM rollups, not by this form.
        existing.Name = ingredient.Name;
        existing.IngredientCategoryId = ingredient.IngredientCategoryId;
        existing.PurchaseUnitId = ingredient.PurchaseUnitId;
        existing.PurchaseUnitQty = ingredient.PurchaseUnitQty;
        existing.WeightLossPercent = ingredient.WeightLossPercent;

        if (existing.Source == IngredientSource.Purchased)
        {
            // PurchaseUnitQty may have just changed with no new price entry —
            // CostPerGram would otherwise stay stale against the old quantity.
            IngredientRules.RecomputeCostPerGramFromCurrentCost(existing);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (existing.Source == IngredientSource.Made)
        {
            // WeightLossPercent may have just changed — recompute from its current BOM.
            await costPropagator.RecomputeAndPropagateAsync(existing.Id, cancellationToken);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Ingredients.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
