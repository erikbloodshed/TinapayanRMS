using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class IngredientRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IIngredientRepository
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

    public async Task AddAsync(Ingredient ingredient, CancellationToken cancellationToken)
    {
        IngredientRules.EnsureFieldsMatchSource(ingredient);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Ingredient ingredient, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var original = await dbContext.Ingredients
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == ingredient.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Ingredient {ingredient.Id} does not exist.");

        IngredientRules.EnsureSourceUnchanged(original.Source, ingredient.Source);
        IngredientRules.EnsureFieldsMatchSource(ingredient);

        dbContext.Ingredients.Update(ingredient);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Ingredients.Where(i => i.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
