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
            .Include(i => i.Units.OrderByDescending(u => u.GramsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Ingredient?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Ingredients
            .AsNoTracking()
            .Include(i => i.IngredientCategory)
            .Include(i => i.Units.OrderByDescending(u => u.GramsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure)
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

        // Field by field, not a blind Update(): CostPerGram is owned by the price-history and
        // BOM rollups, and the units by IngredientUnitRepository, not by this form.
        existing.Name = ingredient.Name;
        existing.IngredientCategoryId = ingredient.IngredientCategoryId;
        existing.WeightLossPercent = ingredient.WeightLossPercent;

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
